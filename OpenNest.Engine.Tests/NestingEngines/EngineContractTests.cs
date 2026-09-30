using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using Xunit;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>Host contract only; engines retain their own packing-quality regressions.</summary>
public abstract class EngineContractTests<TEngine> where TEngine : INestingEngine, new()
{
    [Fact]
    public void ContractPublicConstructor() => Assert.IsAssignableFrom<INestingEngine>(Activator.CreateInstance(typeof(TEngine)));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ContractQuadrants(int quadrant)
    {
        var job = Job([Part("disc", Disc(2), 3), Part("ell", LShape(6, 5, 2), 3)],
            [Stock("s", 20, 30, 0.2, new Spacing(0.2, 0.3, 0.4, 0.5), quadrant)]);
        var result = new TEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void ContractOverflowIndicesAndProgress()
    {
        var job = Job([Part("p", Rectangle(8, 8), 3)], [Stock("s", 10, 10)]);
        var commits = new List<NestJobProgress>();
        var result = new TEngine().Solve(job, new Capture(p => { if (p.Stage == NestJobStage.PlateCommitted) commits.Add(p); }));
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(3, result.Plates.Count);
        Assert.Equal(Enumerable.Range(0, 3), result.Plates.Select(p => p.PlateIndex));
        Assert.Equal(3, commits.Count);
        Assert.Equal(Enumerable.Range(0, 3), commits.Select(p => p.PlateIndex));
        Assert.Equal(Enumerable.Range(1, 3), commits.Select(p => p.CommittedPlates));
        Assert.Equal(Enumerable.Range(1, 3), commits.Select(p => p.CommittedParts));
        Assert.All(result.StockUsage, s => Assert.Null(s.Remaining));
    }

    [Fact]
    public void ContractOversize()
    {
        var job = Job([Part("huge", Rectangle(50, 50), 1), Part("small", Rectangle(2, 2), 2)], [Stock("s", 10, 10)]);
        var result = new TEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(1, result.Fulfillment.Single(f => f.PartId == "huge").Unplaced);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
    }

    [Fact]
    public void ContractLowerNumberPriorityWins()
    {
        var job = Job([Part("low", Rectangle(8, 8), 1, priority: 9), Part("high", Rectangle(8, 8), 1, priority: 0)],
            [Stock("s", 10, 10, quantity: 1)]);
        var result = new TEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal("high", Assert.Single(Assert.Single(result.Plates).Placements).PartId);
    }

    [Fact]
    public void ContractEtchOutsideSheetIsIgnored()
    {
        var etched = NotchedPartWithEtch();
        etched.Codes.Add(new RapidMove(5, 5));
        etched.Codes.Add(new LinearMove(100, 100) { Layer = LayerType.Scribe });
        var job = Job([Part("p", etched, 1, RotationPolicy.Fixed(0))], [Stock("s", 10.4, 10.4, quantity: 1)]);
        var result = new TEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void ContractDeterminism()
    {
        NestJob Build() => Job([Part("disc", Disc(2.5), 12), Part("ell", LShape(9, 7, 3), 12), Part("tri", Triangle(7, 7), 12)],
            [Stock("a", 30, 45, 0.3), Stock("b", 40, 40, 0.3)]);
        var engine = new TEngine();
        var job = Build();
        var first = engine.Solve(job);
        var second = engine.Solve(job);
        var third = new TEngine().Solve(Build());
        foreach (var result in new[] { first, second, third }) LayoutAssert.Valid(job, result);
        Assert.Equal(Describe(first), Describe(second));
        Assert.Equal(Describe(first), Describe(third));
    }

    [Fact]
    public void ContractCancellationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var job = Job([Part("p", Rectangle(2, 2), 5)], [Stock("s", 10, 10)]);
        Assert.ThrowsAny<OperationCanceledException>(() => new TEngine().Solve(job, token: cancellation.Token));
    }

    [Fact]
    public void ContractCancellationDuringSolveThrows()
    {
        using var cancellation = new CancellationTokenSource();
        var job = Job([Part("p", Rectangle(2, 2), 20)], [Stock("s", 10, 10)]);
        var progress = new Capture(p =>
        {
            if (p.Stage == NestJobStage.EvaluatingCandidate) cancellation.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() => new TEngine().Solve(job, progress, cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContractStockAndPlateLimits(bool plateLimit)
    {
        var job = Job([Part("p", Rectangle(8, 8), 3)],
            [Stock("s", 10, 10, quantity: plateLimit ? null : 1)],
            new NestJobOptions(maxPlates: plateLimit ? 1 : null));
        var result = new TEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Single(result.Plates);
        Assert.Equal(2, Assert.Single(result.Fulfillment).Unplaced);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(plateLimit ? NestJobStopReason.PlateLimitReached : NestJobStopReason.StockExhausted, result.StopReason);
    }

    private static string Describe(NestJobResult result) => System.Text.Json.JsonSerializer.Serialize(result);
    private sealed class Capture(Action<NestJobProgress> action) : IProgress<NestJobProgress>
    { public void Report(NestJobProgress value) => action(value); }
}

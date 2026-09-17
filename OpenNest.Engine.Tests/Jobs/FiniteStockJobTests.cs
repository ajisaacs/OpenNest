using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class FiniteStockJobTests
{
    internal static NestJob Job(int? stock = 3, NestJobOptions? options = null) => new(
        new[] { new NestJobPart("p", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle()), 3) },
        new[] { new NestPlateStock("s", new Size(100, 200), stock) }, options);

    internal sealed class Nester(Func<PlatePlacementRequest, PlateCandidate> place) : IPlateNester
    {
        public int Calls { get; private set; }
        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) { Calls++; return place(request); }
    }

    internal static PlateCandidate One(PlatePlacementRequest request) => new(new[]
        { new NestJobPlacement(request.Parts[0].Id, 99, 1, 2, 0) });

    [Theory]
    [InlineData(3, 3, 0, NestJobStatus.Complete, NestJobStopReason.Completed)]
    [InlineData(2, 2, 1, NestJobStatus.Incomplete, NestJobStopReason.StockExhausted)]
    [InlineData(0, 0, 3, NestJobStatus.Incomplete, NestJobStopReason.StockExhausted)]
    public void DemandAndPhysicalStockAreAccountedFromPlacements(int stock, int placed, int left,
        NestJobStatus status, NestJobStopReason reason)
    {
        var requests = new List<int>();
        var nester = new Nester(r => { requests.Add(r.Parts[0].Quantity); return One(r); });
        var job = Job(stock);
        var result = new NestJobRunner(_ => nester).Solve(job);
        Assert.Equal(status, result.Status);
        Assert.Equal(reason, result.StopReason);
        Assert.Equal(placed, result.Plates.Count);
        Assert.All(result.Plates, p => Assert.Single(p.Placements));
        Assert.Equal(Enumerable.Range(0, placed), result.Plates.SelectMany(p => p.Placements).Select(p => p.InstanceIndex));
        Assert.Equal(Enumerable.Range(0, placed).Select(i => 3 - i), requests);
        Assert.Equal(new PartFulfillment("p", 3, placed, left), Assert.Single(result.Fulfillment));
        Assert.Equal(new StockUsage("s", placed, stock - placed), Assert.Single(result.StockUsage));
        Assert.Equal(3, job.Parts[0].Quantity);
        Assert.Equal(stock, job.Plates[0].Quantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public void NoPlacementStopsWithoutConsumingStock(int? stock)
    {
        var nester = new Nester(_ => new PlateCandidate(Array.Empty<NestJobPlacement>()));
        var result = new NestJobRunner(_ => nester).Solve(Job(stock));
        Assert.Equal(1, nester.Calls);
        Assert.Empty(result.Plates);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
        Assert.Equal(new StockUsage("s", 0, stock), Assert.Single(result.StockUsage));
    }

    [Fact]
    public void PlateLimitStopsUnlimitedStock()
    {
        var result = new NestJobRunner(_ => new Nester(One)).Solve(Job(null, new NestJobOptions(maxPlates: 2)));
        Assert.Equal(2, result.Plates.Count);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
        Assert.Equal(new StockUsage("s", 2, null), Assert.Single(result.StockUsage));
    }

    [Fact]
    public void CancellationImmediatelyAfterEngineReturnThrowsWithoutCommit()
    {
        using var cts = new CancellationTokenSource();
        var commits = new List<NestJobProgress>();
        var nester = new Nester(r => { cts.Cancel(); return One(r); });
        Assert.Throws<OperationCanceledException>(() => new NestJobRunner(_ => nester)
            .Solve(Job(), new InlineProgress(commits.Add), cts.Token));
        Assert.DoesNotContain(commits, p => p.Stage == NestJobStage.PlateCommitted);
    }

    [Fact]
    public void InitialCancellationSkipsEngine()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new NestJobRunner(_ => throw new Exception("called"))
            .Solve(Job(), token: cts.Token));
    }

    [Theory]
    [InlineData("unknown", 0, 0, 0, 1)]
    [InlineData("p", double.NaN, 0, 0, 1)]
    [InlineData("p", 0, double.PositiveInfinity, 0, 1)]
    [InlineData("p", 0, 0, double.NaN, 1)]
    [InlineData("p", 0, 0, 0, 4)]
    public void InvalidCandidateThrows(string id, double x, double y, double rotation, int count)
    {
        var nester = new Nester(_ => new PlateCandidate(Enumerable.Range(0, count)
            .Select(i => new NestJobPlacement(id, i, x, y, rotation))));
        Assert.Throws<InvalidOperationException>(() => new NestJobRunner(_ => nester).Solve(Job()));
    }

    [Fact]
    public void NullCandidateAndUnknownStrategyAreExplicitErrors()
    {
        Assert.Throws<InvalidOperationException>(() => new NestJobRunner(_ => new Nester(_ => null!)).Solve(Job()));
        Assert.Throws<NotSupportedException>(() => new NestJobRunner(_ => null!).Solve(Job(options: new NestJobOptions("missing"))));
    }

    [Fact]
    public void MixedStockIsNotSilentlyIgnored()
    {
        var job = Job();
        var mixed = new NestJob(job.Parts, job.Plates.Concat(new[] { new NestPlateStock("other", new Size(10, 20), 2) }));
        Assert.Throws<NotSupportedException>(() => new NestJobRunner(_ => new Nester(One)).Solve(mixed));
    }

    [Theory]
    [InlineData(0, 20, 0, 1)]
    [InlineData(10, double.NaN, 0, 1)]
    [InlineData(10, 20, -1, 1)]
    [InlineData(10, 20, double.PositiveInfinity, 1)]
    [InlineData(10, 20, 0, 5)]
    public void InvalidStockSettingsRejected(double width, double length, double spacing, int quadrant)
    {
        var job = new NestJob(Job().Parts, new[] { new NestPlateStock("s", new Size(width, length), 1, spacing, quadrant: quadrant) });
        Assert.Throws<ArgumentException>(() => new NestJobRunner(_ => new Nester(One)).Solve(job));
    }

    [Fact]
    public void InvalidEdgesAndGeometryRejected()
    {
        var runner = new NestJobRunner(_ => new Nester(One));
        foreach (var edges in new[] { new Spacing(-1, 0, 0, 0), new Spacing(0, double.NaN, 0, 0), new Spacing(1000, 1000, 1000, 1000) })
            Assert.Throws<ArgumentException>(() => runner.Solve(new NestJob(Job().Parts,
                new[] { new NestPlateStock("s", new Size(100, 200), edgeSpacing: edges) })));
        var program = TestDrawingFactory.Rectangle();
        program.LineTo(double.NaN, 0);
        Assert.Throws<ArgumentException>(() => runner.Solve(new NestJob(new[]
            { new NestJobPart("p", PartGeometrySnapshot.FromProgram(program), 1) }, Job().Plates)));
    }

    [Fact]
    public void InvalidContractInputsAreRejected()
    {
        var job = Job();
        Assert.Throws<ArgumentNullException>(() => new NestJobRunner(_ => new Nester(One)).Solve(null!));
        Assert.Throws<ArgumentException>(() => new NestJob(new NestJobPart[] { null! }, job.Plates));
        Assert.Throws<ArgumentException>(() => new NestJob(job.Parts.Concat(job.Parts), job.Plates));
        Assert.Throws<ArgumentException>(() => new NestJob(job.Parts, job.Plates.Concat(job.Plates)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestJobPart("p", job.Parts[0].Geometry, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestPlateStock("s", new Size(1, 1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestJobOptions(maxPlates: 0));
    }

    [Fact]
    public void RunnerFactoriesAreInstanceScopedAndReceiveExactStrategyKeys()
    {
        var keys = new List<string>();
        var first = new NestJobRunner(key => { keys.Add(key); return new Nester(One); });
        var second = new NestJobRunner(key => { keys.Add(key); return new Nester(_ => new PlateCandidate(Array.Empty<NestJobPlacement>())); });
        Assert.Equal(NestJobStatus.Complete, first.Solve(Job(options: new NestJobOptions("custom-A"))).Status);
        Assert.Equal(NestJobStopReason.NoPlacementFound, second.Solve(Job(options: new NestJobOptions("custom-B"))).StopReason);
        Assert.Equal(NestJobStatus.Complete, first.Solve(Job(options: new NestJobOptions("custom-A"))).Status);
        Assert.Equal(new[] { "custom-A", "custom-B", "custom-A" }, keys);
    }

    [Fact]
    public void CancellationAfterAnEarlierCommitStillThrowsRatherThanReturningPartialResult()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var commits = new List<NestJobProgress>();
        var nester = new Nester(r => { if (++calls == 2) cts.Cancel(); return One(r); });
        Assert.Throws<OperationCanceledException>(() => new NestJobRunner(_ => nester)
            .Solve(Job(), new InlineProgress(commits.Add), cts.Token));
        Assert.Equal(1, Assert.Single(commits.Where(p => p.Stage == NestJobStage.PlateCommitted)).CommittedParts);
        Assert.Equal(2, calls);
    }

    private sealed class InlineProgress(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }
}

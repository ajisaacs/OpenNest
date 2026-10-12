using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>Physical accounting/continuation contract, run independently for each repeating engine.</summary>
public abstract class RepeatEngineSafetyTests
{
    private protected abstract INestingEngine Engine(Action? packed = null, Action<RepeatBatch>? batches = null);

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    public void MixedScarcityContinuesToDifferentResidualLayoutInOneSolve(int urgentCount)
    {
        var job = Mixed(urgentCount, 8);
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(urgentCount, batches[0].Copies);
        Assert.True(batches.Count > 1);
        Assert.Equal(urgentCount, result.Fulfillment.Single(f => f.PartId == "a").Placed);
        Assert.Equal(8, result.Fulfillment.Single(f => f.PartId == "b").Placed);
        Assert.All(result.Plates.Take(urgentCount), p => Assert.Equal(new[] { "a", "b" }, p.Placements.Select(v => v.PartId)));
        Assert.All(result.Plates.Skip(urgentCount), p => Assert.All(p.Placements, v => Assert.Equal("b", v.PartId)));
    }

    [Fact]
    public void SmallDemandThatFitsTheRemnantIsPlacedBeforeRepeating()
    {
        var job = Mixed(10, 10);
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(10, Assert.Single(batches).Copies);
        Assert.All(result.Plates, p => Assert.Equal(new[] { "a", "b" }, p.Placements.Select(v => v.PartId)));
    }

    [Theory]
    [InlineData(2, null, NestJobStopReason.StockExhausted)]
    [InlineData(null, 2, NestJobStopReason.PlateLimitReached)]
    [InlineData(3, 2, NestJobStopReason.PlateLimitReached)]
    public void FiniteStockAndMaxPlatesCountPhysicalCopies(int? stockCount, int? cap, NestJobStopReason reason)
    {
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2, quantity: stockCount)], new NestJobOptions(maxPlates: cap));
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(reason, result.StopReason);
        Assert.Equal(2, result.Plates.Count);
        Assert.Equal(8, Assert.Single(result.Fulfillment).Placed);
        Assert.Equal(2, Assert.Single(batches).Copies);
        Assert.Equal(stockCount - 2, Assert.Single(result.StockUsage).Remaining);
    }

    [Fact]
    public void CompetingSameSizeStockEntriesCommitSinglyUntilOneRemains()
    {
        var job = Job([Rectangle("a", 9, 9, 16)], [Stock("one", 20, 20, spacing: 0.2, quantity: 2), Stock("two", 20, 20, spacing: 0.2, quantity: 2)]);
        var batches = new List<RepeatBatch>();
        var calls = 0;
        var result = Engine(() => calls++, batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new[] { 1, 1, 2 }, batches.Select(b => b.Copies));
        Assert.Equal(new[] { 2, 2, 1 }, batches.Select(b => b.AvailableStockEntries));
        Assert.All(result.StockUsage, s => Assert.Equal(2, s.Used));
        Assert.True(calls >= 5); // Two competing trials twice, then the genuinely sole remaining stock.
    }

    [Fact]
    public void SameNamedDrawingsRetainExactDistinctRequirementIds()
    {
        var a = new Drawing("same", Shapes.Rectangle(6, 9));
        var b = new Drawing("same", Shapes.Rectangle(3, 9)) { Priority = 1 };
        var job = Job([DrawingJobMapper.FromDrawing("a", a, 5), DrawingJobMapper.FromDrawing("A", b, 8)], [Stock("s", 10, 10, spacing: 0.2)]);
        var result = Engine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(5, result.Fulfillment.Single(f => f.PartId == "a").Placed);
        Assert.Equal(8, result.Fulfillment.Single(f => f.PartId == "A").Placed);
        Assert.Equal(0, a.Quantity.Nested);
        Assert.Equal(0, b.Quantity.Nested);
    }

    [Fact]
    public void SalvageOffcutDoesNotPreventRepeatingCompletedJobSearch()
    {
        var stock = new NestPlateStock("s", new OpenNest.Geometry.Size(10, 10), null, 0.2, default, 1, cost: 20);
        var job = Job([Rectangle("a", 6, 9, 10)], [stock], new NestJobOptions(salvageRate: 0.5, minimumSalvageDimension: 1));
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(10, Assert.Single(batches).Copies);
        Assert.Equal(10, result.Plates.Count);
        Assert.Equal(162, NestJobCost.Evaluate(job, result), precision: 8);
        Assert.True(NestJobCost.NetSheetCost(job, result.Plates[0]) < stock.Cost);
    }

    [Fact]
    public void OversizedRequirementRemainsUnplacedWhileUsefulDemandRepeats()
    {
        var job = Job([Rectangle("huge", 100, 100, 4), Rectangle("small", 6, 9, 5)], [Stock("s", 10, 10, spacing: 0.2)]);
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(4, result.Fulfillment.Single(f => f.PartId == "huge").Unplaced);
        Assert.Equal(5, result.Fulfillment.Single(f => f.PartId == "small").Placed);
        Assert.Contains(batches, b => b.Copies == 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoFitOrUnavailableStockProducesNoEmptyRepeat(bool unavailable)
    {
        var job = Job([Rectangle("a", 100, 100, 2)], [Stock("s", 10, 10, quantity: unavailable ? 0 : null)]);
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Empty(result.Plates);
        Assert.Empty(batches);
        Assert.Equal(2, Assert.Single(result.Fulfillment).Unplaced);
    }

    [Fact]
    public void CancellationAtRepeatDecisionCannotReturnAProposal()
    {
        using var cancellation = new CancellationTokenSource();
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var observed = 0;
        var engine = Engine(batches: b => { Assert.Equal(10, b.Copies); observed++; cancellation.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => engine.Solve(job, token: cancellation.Token));
        Assert.Equal(1, observed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void CancellationDuringPhysicalExpansionIncludingLastCopyIsObserved(int cancelAfter)
    {
        using var cancellation = new CancellationTokenSource();
        var committed = new List<NestJobProgress>();
        var progress = new InlineProgress(p =>
        {
            if (p.Stage != NestJobStage.PlateCommitted) return;
            committed.Add(p);
            if (p.CommittedPlates == cancelAfter) cancellation.Cancel();
        });
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        Assert.Throws<OperationCanceledException>(() => Engine().Solve(job, progress, cancellation.Token));
        Assert.Equal(cancelAfter, committed.Count);
        Assert.Equal(cancelAfter * 4, committed.Last().CommittedParts);
    }

    [Fact]
    public void SuccessfulProgressRemainsPhysicalAndOrdered()
    {
        var committed = new List<NestJobProgress>();
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var result = Engine().Solve(job, new InlineProgress(p => { if (p.Stage == NestJobStage.PlateCommitted) committed.Add(p); }));
        LayoutAssert.Valid(job, result);
        Assert.Equal(Enumerable.Range(1, 10), committed.Select(p => p.CommittedPlates));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => i * 4), committed.Select(p => p.CommittedParts));
    }

    [Fact]
    public void UnreadableGeometryIsNotTurnedIntoAnEmptyRepeat()
    {
        var job = Job([Part("unsupported", new OpenNest.CNC.Program(), 12)], [Stock("s", 10, 10)]);
        var batches = new List<RepeatBatch>();
        var result = Engine(batches: batches.Add).Solve(job);
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(12, Assert.Single(result.Fulfillment).Unplaced);
        Assert.Empty(result.Plates);
        Assert.Empty(batches);
        Assert.Equal(0, Assert.Single(result.StockUsage).Used);
    }

    [Fact]
    public void CancellationInsideTheRealSheetSearchNeverCertifiesARepeat()
    {
        using var cancellation = new CancellationTokenSource();
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var batches = new List<RepeatBatch>();
        var packed = 0;
        var progress = new InlineProgress(p =>
        {
            if (p.Stage == NestJobStage.EvaluatingCandidate) cancellation.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() => Engine(() => packed++, batches.Add).Solve(job, progress, cancellation.Token));
        Assert.Equal(0, packed);
        Assert.Empty(batches);
    }

    private static NestJob Mixed(int a, int b) => Job(
        [Part("a", Shapes.Rectangle(6, 9), a, RotationPolicy.Fixed(0)), Part("b", Shapes.Rectangle(3, 9), b, RotationPolicy.Fixed(0), priority: 1)],
        [Stock("s", 10, 10, spacing: 0.2)]);

    private sealed class InlineProgress(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }
}

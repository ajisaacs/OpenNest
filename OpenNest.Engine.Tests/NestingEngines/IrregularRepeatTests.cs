using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularRepeatTests : RepeatEngineSafetyTests
{
    private protected override INestingEngine Engine(Action? packed = null, Action<RepeatBatch>? batches = null) =>
        new IrregularNestingEngine { WorkBudget = 0, SheetPacked = packed, BatchSelected = batches };

    [Fact]
    public void CompletedFullLayoutRemovesActualFrontierFillCallsIncludingTailTrials()
    {
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var calls = 0;
        var result = new IrregularNestingEngine { SheetPacked = () => calls++ }.Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(10, result.Plates.Count);
        Assert.Equal(24, calls); // Six main variants and six trials for each physical tail of 1..3 sheets.
        var first = result.Plates[0];
        foreach (var plate in result.Plates.Skip(1))
        {
            Assert.NotSame(first.Placements, plate.Placements);
            Assert.Equal(first.Placements.Select(Pose), plate.Placements.Select(Pose));
            Assert.NotSame(first.Placements[0], plate.Placements[0]);
        }
    }

    [Fact]
    public void SeededPhysicalTailReturnsFiniteStockAndReconsumesItWithoutDroppingRepeatedPrefix()
    {
        // A lifecycle test, not a claim that public Solve chooses this deliberately wasteful
        // incumbent. Four identical full copies precede two half-full physical tail copies.
        var job = Job([Rectangle("a", 9, 9, 20)], [Stock("s", 20, 20, spacing: 0.2, quantity: 6)]);
        var orientation = Assert.Single(Assert.Single(PartCatalog.Build(job)).Orientations);
        var full = new[] { new Placed(orientation, 0, 0), new Placed(orientation, 9.2, 0),
            new Placed(orientation, 0, 9.2), new Placed(orientation, 9.2, 9.2) };
        var seed = Enumerable.Range(0, 4).Select(_ => new SheetFill(job.Plates[0], full.ToArray(), 324))
            .Concat(Enumerable.Range(0, 2).Select(_ => new SheetFill(job.Plates[0], full.Take(2).ToArray(), 162))).ToArray();
        var incumbent = new NestJobResultBuilder(job);
        foreach (var sheet in seed)
            incumbent.AddSheet(sheet.Stock, sheet.Parts.Select(p => ("a", p.X, p.Y, p.Orientation.Rotation)));
        var before = incumbent.Build(NestJobStopReason.Completed);
        LayoutAssert.Valid(job, before);
        var replaced = new List<int>();
        var batches = new List<RepeatBatch>();
        var result = new IrregularNestingEngine { TailReplaced = replaced.Add, BatchSelected = batches.Add }
            .ImproveTailForTesting(job, seed);
        LayoutAssert.Valid(job, result);
        Assert.Equal(new[] { 2 }, replaced);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(5, result.Plates.Count);
        Assert.Equal(20, Assert.Single(result.Fulfillment).Placed);
        Assert.Equal((5, 1), (Assert.Single(result.StockUsage).Used, Assert.Single(result.StockUsage).Remaining));
        Assert.Equal(2000, NestJobCost.Evaluate(job, result), precision: 8);
        Assert.True(NestJobCost.Evaluate(job, result) < NestJobCost.Evaluate(job, before));
        for (var i = 0; i < 4; i++)
            Assert.Equal(before.Plates[i].Placements.Select(Pose), result.Plates[i].Placements.Select(Pose));
        Assert.All(result.Plates, p => Assert.Equal(4, p.Placements.Count));
        Assert.Equal(Enumerable.Range(0, 20), result.Plates.SelectMany(p => p.Placements).Select(p => p.InstanceIndex));
        Assert.All(seed.Take(4), s => Assert.Equal(full, s.Parts)); // The incumbent was not mutated.
        Assert.Contains(batches, b => b.ForcedFirst && b.Copies > 1); // The retained physical tail can still repeat.
    }

    [Fact]
    public void ForcedFirstStockCountsAllAvailableEntriesAndResumesAlternatives()
    {
        var job = Job([Rectangle("a", 9, 9, 16)],
            [Stock("one", 20, 20, spacing: 0.2, quantity: 10), Stock("two", 20, 20, spacing: 0.2, quantity: 10)]);
        var batches = new List<RepeatBatch>();
        var result = new IrregularNestingEngine { BatchSelected = batches.Add }.Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var forced = batches.Where(b => b.ForcedFirst).ToArray();
        Assert.NotEmpty(forced);
        Assert.All(forced, b =>
        {
            Assert.Equal(2, b.AvailableStockEntries);
            Assert.Equal(1, b.Copies);
        });
        // Within a multi-sheet Decode forced to start on "two", the next decision can
        // choose "one". A trial-subset count of one would incorrectly batch that first sheet.
        Assert.Contains(Enumerable.Range(0, batches.Count - 1), i =>
            batches[i] is { ForcedFirst: true, StockId: "two" }
            && batches[i + 1] is { ForcedFirst: false, StockId: "one", SheetsBefore: 1 });
    }

    private static (string, double, double, double) Pose(NestJobPlacement p) => (p.PartId, p.X, p.Y, p.Rotation);
}

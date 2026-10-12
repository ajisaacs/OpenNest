using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Engine.RectanglePacking;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

public class RectanglesRepeatTests : RepeatEngineSafetyTests
{
    private protected override INestingEngine Engine(Action? packed = null, Action<RepeatBatch>? batches = null) =>
        new RectanglesNestingEngine { SheetPacked = _ => packed?.Invoke(), BatchSelected = batches };

    [Fact]
    public void CompletedFullLayoutRemovesActualPackCallsInBothModes()
    {
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var calls = new List<PickMode>();
        var result = new RectanglesNestingEngine { SheetPacked = calls.Add }.Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(10, result.Plates.Count);
        Assert.Equal(6, calls.Count(m => m == PickMode.Global));
        Assert.Equal(6, calls.Count(m => m == PickMode.Ordered));
        var first = result.Plates[0];
        foreach (var plate in result.Plates.Skip(1))
        {
            Assert.NotSame(first.Placements, plate.Placements);
            Assert.Equal(first.Placements.Select(Pose), plate.Placements.Select(Pose));
            Assert.NotSame(first.Placements[0], plate.Placements[0]);
        }
    }

    [Fact]
    public void CandidateOutsideTheStricterCertificationBoundsRetainsOrdinaryAllocation()
    {
        // MaxRects/layout-check slack accepts this overhang; the candidate validator does not.
        // This real Solve must pack five times rather than certifying a repeat from that layout.
        var job = Job([Rectangle("a", 10.000006, 9, 5)], [Stock("s", 10, 10)]);
        var batches = new List<RepeatBatch>();
        var calls = 0;
        var result = new RectanglesNestingEngine { SheetPacked = _ => calls++, BatchSelected = batches.Add }.Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(5, result.Plates.Count);
        Assert.Equal(60, calls);
        Assert.Equal(5, batches.Count);
        Assert.All(batches, b => Assert.Equal(1, b.Copies));
        var candidate = new PlateCandidate(result.Plates[0].Placements);
        Assert.Throws<InvalidOperationException>(() => NestJobValidator.ValidateCandidate(candidate, job.Plates[0],
            new Dictionary<string, int> { ["a"] = 5 }, job.Parts.ToDictionary(p => p.Id)));
    }

    private static (string, double, double, double) Pose(NestJobPlacement p) => (p.PartId, p.X, p.Y, p.Rotation);
}

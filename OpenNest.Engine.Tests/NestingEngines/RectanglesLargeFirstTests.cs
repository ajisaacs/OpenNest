using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Rectangles;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// A job mixing large plates with small parts: cost alone picks the densest sheet first, a sheet of
/// small parts, and leaves the large plates' sheets nothing to fill their gaps with. The large-first
/// run nests the plates first and fills their gaps, so no sheet is spent on small parts alone.
/// </summary>
public class RectanglesLargeFirstTests
{
    private static NestJob BrazeCasing() => Job(
        new[]
        {
            Part("side-a", Rectangle(56.93, 88.99), 3),
            Part("side-b", Rectangle(58.7, 56.93), 2),
            Part("angle", Rectangle(5.3, 28.28), 30),
            Part("tab", Rectangle(9, 3.75), 40),
        },
        new[]
        {
            Stock("60x120", 60, 120, spacing: 0.25),
            Stock("72x144", 72, 144, spacing: 0.25),
        });

    private static readonly string[] Large = { "side-a", "side-b" };

    [Fact]
    public void CostAloneSpendsTheFirstSheetOnSmallParts()
    {
        var job = BrazeCasing();

        var result = new RectanglesNestingEngine { LargeFirstRuns = false }.Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.DoesNotContain(result.Plates[0].Placements, p => Large.Contains(p.PartId));
    }

    [Fact]
    public void SmallPartsFillTheLargePlatesSheets()
    {
        var job = BrazeCasing();

        var result = new RectanglesNestingEngine().Solve(job);
        var costFirst = new RectanglesNestingEngine { LargeFirstRuns = false }.Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.True(NestJobCost.Evaluate(job, result) <= NestJobCost.Evaluate(job, costFirst));
        // Only the last sheet may hold small parts alone: those its predecessors had no room for.
        Assert.All(result.Plates.Take(result.Plates.Count - 1),
            sheet => Assert.Contains(sheet.Placements, p => Large.Contains(p.PartId)));
        Assert.All(result.Plates.Take(result.Plates.Count - 1),
            sheet => Assert.Contains(sheet.Placements, p => !Large.Contains(p.PartId)));
    }
}

using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Irregular's large-first variants: each sheet packs the large types before any small one. On a
/// real job (long bars, near-rectangular strips and interlocking triangles on seven stock sizes)
/// sheet-by-sheet cost spent the first sheets on the small parts and left the bars six to a small
/// sheet, 12 sheets; large first took it to 8 sheets of 3% lower cost than the Rectangles plan.
/// That job needs about a minute, so here only the splits and the never-worse rule are tested.
/// </summary>
public class IrregularLargeFirstTests
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
    public void SplitsFallBelowEachWideDropInBoxArea()
    {
        var job = Job(
            new[]
            {
                Part("plate", Rectangle(40, 10), 2),
                Part("strip", Rectangle(30, 10), 2),
                Part("tab", Rectangle(5, 5), 2),
                Part("spent", Rectangle(80, 80), 1),
            },
            new[] { Stock("sheet", 100, 100) });
        var types = PartCatalog.Build(job);
        var demand = types.Select(t => t.Part.Id == "spent" ? 0 : t.Part.Quantity).ToArray();

        var splits = IrregularNestingEngine.LargeFirstRanks(types, demand).ToList();

        // 400 -> 300 is no wide drop; 300 -> 25 is. A type without demand left never sets a split.
        var split = Assert.Single(splits);
        Assert.Equal(new[] { 0, 0, 1, 0 }, types.Select(t => split[t.Index]));
    }

    [Fact]
    public void PriorityThatAlreadyPutsTheLargePartsFirstLeavesNoSplit()
    {
        var job = Job(
            new[] { Part("plate", Rectangle(40, 10), 2, priority: 1), Part("tab", Rectangle(5, 5), 2) },
            new[] { Stock("sheet", 100, 100) });
        var types = PartCatalog.Build(job);

        Assert.Empty(IrregularNestingEngine.LargeFirstRanks(types, types.Select(t => t.Part.Quantity).ToArray()));
    }

    [Fact]
    public void OneSizeJobsHaveNoSplit()
    {
        var job = Job(new[] { Part("a", Rectangle(10, 10), 4), Part("b", Rectangle(10, 9), 4) },
            new[] { Stock("sheet", 100, 100) });
        var types = PartCatalog.Build(job);

        Assert.Empty(IrregularNestingEngine.LargeFirstRanks(types, types.Select(t => t.Part.Quantity).ToArray()));
    }

    [Fact]
    public void LargeFirstNeverCostsMore()
    {
        var job = BrazeCasing();

        var result = new IrregularNestingEngine().Solve(job);
        var without = new IrregularNestingEngine { LargeFirst = false }.Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.True(NestJobCost.Evaluate(job, result) <= NestJobCost.Evaluate(job, without),
            $"{NestJobCost.Evaluate(job, result)} vs {NestJobCost.Evaluate(job, without)}");
        Assert.All(result.Plates.Take(result.Plates.Count - 1),
            sheet => Assert.Contains(sheet.Placements, p => Large.Contains(p.PartId)));
    }

    [Fact]
    public void PassesAfterItRunAsWithoutItWhenItDoesNotWin()
    {
        // The large-first plans tie the strategy variants' best here, so they never replace it,
        // and the passes after them, metered apart from them, make the same layout.
        var job = BrazeCasing();

        var result = new IrregularNestingEngine().Solve(job);
        var without = new IrregularNestingEngine { LargeFirst = false }.Solve(job);

        Assert.Equal(PlanAssert.Layout(without), PlanAssert.Layout(result));
    }
}

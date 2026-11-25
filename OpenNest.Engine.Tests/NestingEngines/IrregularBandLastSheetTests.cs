using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Irregular bands the last sheet of each plan it makes its best: the like parts that end a job
/// as their Lattice patterns side by side. On a real job (six 43 x 6 strips, 27 small plates and
/// 106 interlocking triangles ending a nine-sheet plan on 48 x 144) the frontier packer set the
/// triangles down one at a time and the sheet ended at 120.8; banded it ends at 114.6. That sheet
/// takes about twenty seconds to band, so smaller ones are tested here.
/// </summary>
public class IrregularBandLastSheetTests
{
    /// <summary>The search before the bands, without the variants that band whole jobs.</summary>
    private static IrregularNestingEngine Engine(bool bandLastSheet) => new()
    {
        GroupLikeParts = false,
        BandLikeParts = false,
        BandLastSheet = bandLastSheet,
    };

    private static NestJob BarsThenTriangles() => Job(
        new[]
        {
            Part("bar", Rectangle(43, 6), 3),
            Part("plate", Rectangle(12.5, 4.2), 9),
            Part("tri", Wedge(), 40),
        },
        new[] { Stock("sheet", 48, 96, spacing: 0.25, edge: new Spacing(0.25, 0.25)) });

    /// <summary>A tapered tab: two interlock head to tail, which the frontier packer does not find
    /// for every copy.</summary>
    private static OpenNest.CNC.Program Wedge() => Polyline((0, 0), (7.6, 0), (7.6, 1), (0.8, 3.8), (0, 3.8));

    [Fact]
    public void TheLastSheetEndsShorterThanWithoutTheBands()
    {
        var job = BarsThenTriangles();

        var plain = Engine(bandLastSheet: false).Solve(job);
        var banded = Engine(bandLastSheet: true).Solve(job);

        LayoutAssert.Valid(job, banded);
        Assert.Equal(NestJobStatus.Complete, banded.Status);
        Assert.Equal(plain.Plates.Count, banded.Plates.Count);
        Assert.True(NestJobCost.CompactnessOffcut(job, banded) > NestJobCost.CompactnessOffcut(job, plain),
            $"{NestJobCost.CompactnessOffcut(job, banded)} against {NestJobCost.CompactnessOffcut(job, plain)}");
    }

    [Fact]
    public void TheBandedPlanIsReportedAndReturned()
    {
        var plans = PlanAssert.ReportedAndEndWithTheResult(() => Engine(bandLastSheet: true), BarsThenTriangles());

        Assert.True(plans.Count > 1);
    }

    [Fact]
    public void TheSameJobBandsTheSameWay()
    {
        var first = Engine(bandLastSheet: true).Solve(BarsThenTriangles());
        var second = Engine(bandLastSheet: true).Solve(BarsThenTriangles());

        Assert.Equal(PlanAssert.Layout(first), PlanAssert.Layout(second));
    }

    [Fact]
    public void JobsOfSeveralPrioritiesAreNotBanded()
    {
        var job = Job(
            new[]
            {
                Part("bar", Rectangle(43, 6), 3, priority: 1),
                Part("plate", Rectangle(12.5, 4.2), 9),
                Part("tri", Wedge(), 40),
            },
            new[] { Stock("sheet", 48, 96, spacing: 0.25, edge: new Spacing(0.25, 0.25)) });

        var plain = Engine(bandLastSheet: false).Solve(job);
        var banded = Engine(bandLastSheet: true).Solve(job);

        Assert.Equal(PlanAssert.Layout(plain), PlanAssert.Layout(banded));
    }

    [Fact]
    public void ALastSheetWithoutThreeOfAKindIsLeftAlone()
    {
        var job = Job(
            new[]
            {
                Part("bar", Rectangle(40, 6), 2),
                Part("plate", Rectangle(12, 4), 2),
                Part("ell", LShape(12, 10, 4), 2),
            },
            new[] { Stock("sheet", 48, 96, spacing: 0.25, edge: new Spacing(0.25, 0.25)) });

        var plain = Engine(bandLastSheet: false).Solve(job);
        var banded = Engine(bandLastSheet: true).Solve(job);

        Assert.Equal(PlanAssert.Layout(plain), PlanAssert.Layout(banded));
    }
}

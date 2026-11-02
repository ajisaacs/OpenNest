using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularGroupedTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> NoPairs =
        new Dictionary<int, IReadOnlyList<PairPose>>();

    [Fact]
    public void GroupRanksPlaceEachTypeAsOneGroupInRankOrder()
    {
        var stock = Stock("sheet", 30, 60, spacing: 0.25);
        var job = Job(new[] { Part("box", Rectangle(4, 4), 8), Part("tri", Triangle(5, 5), 8) }, new[] { stock });
        var types = PartCatalog.Build(job);

        var fill = new FrontierPacker(types, new NoFitCache(0.25), NoPairs, stock, PackAxis.X, 1, new WorkCounter(),
            groupRanks: new[] { 1, 0 }).Fill(new[] { 8, 8 }, CancellationToken.None);

        var order = fill.Parts.Select(p => p.Orientation.TypeIndex).ToArray();
        Assert.Equal(Enumerable.Repeat(1, 8).Concat(Enumerable.Repeat(0, 8)), order);
    }

    [Fact]
    public void ReachEndsTheWorkAreaAlongTheFront()
    {
        var stock = Stock("sheet", 20, 60, spacing: 0.25);
        var job = Job(new[] { Part("box", Rectangle(4, 4), 40) }, new[] { stock });
        var types = PartCatalog.Build(job);

        var fill = new FrontierPacker(types, new NoFitCache(0.25), NoPairs, stock, PackAxis.X, 1, new WorkCounter(),
            reach: 13).Fill(new[] { 40 }, CancellationToken.None);

        Assert.Equal(12, fill.Parts.Count); // Three columns of four.
        Assert.All(fill.Parts, p => Assert.True(p.Right <= 13 + 1e-9, $"Right {p.Right}"));
    }

    [Fact]
    public void GroupedLayoutOfMixedRepeatedPartsIsValidAndComplete()
    {
        // The help sample's four parts: gussets, ells, plates and flanges in quantity.
        var job = Job(
            new[]
            {
                Part("gusset", Polyline((0, 0), (5, 0), (0, 5)), 30, RotationPolicy.Automatic),
                Part("ell", LShape(6, 6, 2), 20, RotationPolicy.Automatic),
                Part("plate", Rectangle(5, 8), 10, RotationPolicy.Automatic),
                Part("flange", Disc(3), 12, RotationPolicy.Automatic),
            },
            new[] { Stock("sheet", 60, 120, spacing: 0.3, edge: new Spacing(0.3, 0.75, 0.3, 0.25)) });

        var engine = new IrregularNestingEngine();
        var result = engine.Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(result.Plates.Single().Placements, engine.Solve(job).Plates.Single().Placements);
    }

    [Fact]
    public void GroupingNeedsTwoTypesAndOneOfThreeCopies()
    {
        var job = Job(new[] { Part("a", Rectangle(2, 2), 3), Part("b", Rectangle(3, 2), 2) },
            new[] { Stock("sheet", 20, 20) });
        var types = PartCatalog.Build(job);

        Assert.True(GroupedFill.Applies(types, new[] { 3, 2 }));
        Assert.False(GroupedFill.Applies(types, new[] { 2, 2 }));
        Assert.False(GroupedFill.Applies(types, new[] { 3, 0 }));
    }
}

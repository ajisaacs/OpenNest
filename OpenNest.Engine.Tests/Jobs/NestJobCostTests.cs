using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobCostTests
{
    public static IEnumerable<object[]> EdgeCases()
    {
        foreach (var rate in new[] { 0.0, 0.5, 1.0 })
        foreach (var edge in new[] { "bottom", "top", "left", "right" })
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
            yield return new object[] { rate, edge, quadrant };
    }

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void EveryEdgeMatchesFrozenScoringExactly(double rate, string edge, int quadrant)
    {
        var stock = new NestPlateStock("sheet", new Size(20, 20), partSpacing: 0.25,
            edgeSpacing: new Spacing(1, 1, 1, 1), quadrant: quadrant);
        var work = stock.WorkArea;
        var width = edge is "left" or "right" ? 4 : 18;
        var height = edge is "bottom" or "top" ? 4 : 18;
        var x = work.Left + (edge == "left" ? 14 : 0);
        var y = work.Bottom + (edge == "bottom" ? 14 : 0);
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(
            TestDrawingFactory.Rectangle(width, height)), 2);
        var job = new NestJob(new[] { part }, new[] { stock },
            new NestJobOptions(salvageRate: rate, minimumSalvageDimension: 2));
        var sheet = new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("part", 0, x, y, 0) });
        var expected = 400 - rate * (18 * 13.75);

        Assert.Equal(expected, LegacyNestJobCost.EstimateNetArea(job, sheet));
        Assert.Equal(expected, NestJobCost.NetSheetArea(job, sheet));
        Assert.Equal(expected, NestJobCost.NetSheetArea(job.Options, stock, new Box(x, y, width, height)));
#pragma warning disable CS0618 // Compatibility API must retain the old result.
        Assert.Equal(expected, StockLadderNestingEngine.EstimateNetArea(job, sheet));
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData(0, 5, 100)]
    [InlineData(0.5, 0, 100)]
    [InlineData(0.5, 7, 100)]
    [InlineData(0.5, 6, 70)]
    [InlineData(0.5, 5, 70)]
    public void StockLadderFixtureRetainsThresholds(double rate, double minimum, double expected)
    {
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 4)), 1);
        var stock = new NestPlateStock("sheet", new Size(10, 10));
        var job = new NestJob(new[] { part }, new[] { stock },
            new NestJobOptions(salvageRate: rate, minimumSalvageDimension: minimum));
        var sheet = new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("part", 0, 0, 0, 0) });

        Assert.Equal(expected, NestJobCost.NetSheetArea(job, sheet));
        Assert.Equal(LegacyNestJobCost.EstimateNetArea(job, sheet), NestJobCost.NetSheetArea(job, sheet));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public void RotatedMarksAndMultipleSheetsKeepExactLegacyCost(double rate)
    {
        var program = TestDrawingFactory.Rectangle(4, 3);
        program.MoveTo(2, 2);
        program.Codes.Add(new LinearMove(9, 2) { Layer = LayerType.Scribe });
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 5);
        var stock = new NestPlateStock("sheet", new Size(30, 40));
        var other = new NestPlateStock("large", new Size(50, 50));
        var job = new NestJob(new[] { part }, new[] { stock, other },
            new NestJobOptions(salvageRate: rate, minimumSalvageDimension: 2));
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(stock, new[] { ("part", 10.123, 8.456, 0.37), ("part", 25.789, 17.321, 1.12) });
        builder.AddSheet(other, new[] { ("part", 12.345, 19.876, 2.13) });
        var result = builder.Build(NestJobStopReason.NoPlacementFound);
        foreach (var sheet in result.Plates)
            Assert.Equal(LegacyNestJobCost.EstimateNetArea(job, sheet), NestJobCost.NetSheetArea(job, sheet));
        Assert.Equal(2500, NestJobCost.UnplacedPartPenalty(job));
        Assert.Equal(result.Plates.Sum(sheet => LegacyNestJobCost.EstimateNetArea(job, sheet)) + 5000,
            NestJobCost.Evaluate(job, result));
    }

    [Fact]
    public void ScoringKeepsEtchBoundsEvenThoughMaterialGeometryExcludesThem()
    {
        var program = TestDrawingFactory.Rectangle(4, 3);
        program.MoveTo(2, 2);
        program.Codes.Add(new LinearMove(15, 2) { Layer = LayerType.Scribe });
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1);
        var stock = new NestPlateStock("sheet", new Size(10, 20));
        var job = new NestJob(new[] { part }, new[] { stock },
            new NestJobOptions(salvageRate: 0.5, minimumSalvageDimension: 1));
        var sheet = new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("part", 0, 0, 0, 0) });

        Assert.Equal(130, LegacyNestJobCost.EstimateNetArea(job, sheet));
        Assert.Equal(130, NestJobCost.NetSheetArea(job, sheet));
        Assert.Equal(120, NestJobCost.NetSheetArea(job.Options, stock, JobPartGeometry.Read(part.Geometry).Bounds));
    }

    [Fact]
    public void EmptySheetsAndEmptyStockRetainBenchmarkSemantics()
    {
        var stock = new NestPlateStock("sheet", new Size(10, 10));
        var job = new NestJob(Array.Empty<NestJobPart>(), new[] { stock },
            new NestJobOptions(salvageRate: 1, minimumSalvageDimension: 1));
        var sheet = new NestJobPlateResult(0, stock, Array.Empty<NestJobPlacement>());
        Assert.Equal(100, NestJobCost.NetSheetArea(job, sheet));
        Assert.Equal(LegacyNestJobCost.EstimateNetArea(job, sheet), NestJobCost.NetSheetArea(job, sheet));
        var empty = new NestJob(Array.Empty<NestJobPart>(), Array.Empty<NestPlateStock>());
        Assert.Equal(0, NestJobCost.UnplacedPartPenalty(empty));
        Assert.Equal(0, NestJobCost.Evaluate(empty, new NestJobResultBuilder(empty).Build(NestJobStopReason.Completed)));
    }
}

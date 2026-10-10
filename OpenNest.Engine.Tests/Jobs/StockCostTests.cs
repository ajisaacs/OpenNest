using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class StockCostTests
{
    public static IEnumerable<object[]> Engines()
    {
        foreach (var engine in new[] { "Rectangles", "Irregular", "StockLadder", "Default", "Fill", "Strip", "Vertical Remnant", "Horizontal Remnant" })
            foreach (var factor in new[] { 1e-12, 1.0, 100.0 })
                yield return new object[] { engine, factor };
    }

    private static NestPlateStock Stock(string id, double cost, int quantity = 2, double size = 10) =>
        new(id, new Size(size, size), quantity, 0.1, default, 1, cost);

    private static NestJob Job(params NestPlateStock[] stocks) => new(
        new[] { new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 4)), 1) }, stocks);

    [Fact]
    public void ValidationRejectsForgedStockPrice()
    {
        var job = Job(Stock("offered", 10));
        var forged = Stock("offered", 1);
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, forged, new[] { new NestJobPlacement("part", 0, 0, 0, 0) }) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());
        Assert.NotEmpty(NestLayoutCheck.Violations(job, result));
    }

    [Fact]
    public void UnavailablePriceDoesNotChangeAreaModeOrLegacyPenalty()
    {
        var job = Job(new NestPlateStock("available", new Size(10, 10)), Stock("unavailable", 10000, 0));
        Assert.False(NestJobCost.UsesExplicitCosts(job));
        Assert.Equal(100, NestJobCost.UnplacedPartPenalty(job));
    }

    [Fact]
    public void FixedStrategyUsesSalvageOnlyForPricedStock()
    {
        var poses = new[] { new NestJobPlacement("part", 0, 0, 0, 0) };
        var candidate = new PlateCandidate(poses);
        var priced = Job(Stock("small", 10, size: 5), Stock("large", 12, size: 10));
        var options = new NestJobOptions(salvageRate: 1, minimumSalvageDimension: 1);
        var pricedJob = new NestJob(priced.Parts, priced.Plates, options);
        Assert.True(new NestJobCandidateComparer(pricedJob).Compare(candidate, priced.Plates[1], 1,
            candidate, priced.Plates[0], 0) > 0);
        var unpriced = new NestJob(priced.Parts, new[] { new NestPlateStock("small", new Size(5, 5)),
            new NestPlateStock("large", new Size(10, 10)) }, options);
        Assert.True(new NestJobCandidateComparer(unpriced).Compare(candidate, unpriced.Plates[0], 0,
            candidate, unpriced.Plates[1], 1) > 0);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(0.5, 14, 6)]
    public void SummarySeparatesGrossCreditAndNet(double rate, double net, double credit)
    {
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(4, 4));
        var stock = new NestPlateStock("priced", new Size(10, 10), 1, 0, default, 1, 20);
        var result = NestPipeline.Run(new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = drawing, Quantity = 1 } }, new[] { stock },
            new NestJobOptions(salvageRate: rate, minimumSalvageDimension: 6)));
        Assert.Empty(result.Violations);
        var summary = NestCostSummary.FromAccepted(result);
        Assert.Equal("supplied-cost", summary.Basis);
        Assert.Equal(20, summary.GrossTotal);
        Assert.Equal(net, summary.NetScore, 10);
        Assert.Equal(credit, summary.SalvageCredit, 10);
        Assert.Equal(1, Assert.Single(summary.Stock).Used);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void PricesReverseSelectionWithoutChangingFulfillment(string engine, double factor)
    {
        foreach (var cheap in new[] { "small", "large" })
        {
            var job = Job(Stock("small", (cheap == "small" ? 1 : 10) * factor),
                Stock("large", (cheap == "large" ? 1 : 10) * factor, size: 12));
            var result = NestingEngineRegistry.Create(engine).Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Assert.Empty(NestLayoutCheck.Violations(job, result));
            Assert.Equal(cheap, Assert.Single(result.Plates).StockId);
            Assert.Equal(factor, NestJobCost.Evaluate(job, result));
        }
    }

    [Theory]
    [InlineData("Rectangles")]
    [InlineData("Irregular")]
    [InlineData("StockLadder")]
    [InlineData("Default")]
    [InlineData("Fill")]
    public void SameSizeStockRetainsIndependentFiniteInventory(string engine)
    {
        var stocks = new[] { Stock("expensive", 10, 2, 5), Stock("cheap", 1, 1, 5) };
        var job = new NestJob(new[] { new NestJobPart("part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 4)), 3) }, stocks);
        var result = NestingEngineRegistry.Create(engine).Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Empty(NestLayoutCheck.Violations(job, result));
        Assert.Equal("cheap", result.Plates[0].StockId);
        Assert.Single(result.Plates.Where(p => p.StockId == "cheap"));
        Assert.Equal(21, NestJobCost.Evaluate(job, result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ExplicitCostsMustBeFiniteAndPositive(double cost) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Stock("bad", cost));

    [Fact]
    public void MixedModeRejectsOnlyAvailableStock()
    {
        Assert.Throws<ArgumentException>(() => Job(Stock("priced", 2), new NestPlateStock("missing", new Size(10, 10))));
        var job = Job(Stock("priced", 2), new NestPlateStock("unavailable", new Size(10, 10), 0));
        NestJobValidator.Validate(job);
        Assert.Equal(2, NestJobCost.UnplacedPartPenalty(job));
    }

    [Theory]
    [InlineData(0, 5, 20)]
    [InlineData(0.5, 0, 20)]
    [InlineData(0.5, 7, 20)]
    [InlineData(0.5, 6, 14)]
    public void PricesCreditSalvageProportionally(double rate, double minimum, double expected)
    {
        var stock = new NestPlateStock("priced", new Size(10, 10), 1, 0, default, 1, 20);
        var job = new NestJob(Job(stock).Parts, new[] { stock }, new NestJobOptions(salvageRate: rate, minimumSalvageDimension: minimum));
        var sheet = new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("part", 0, 0, 0, 0) });
        Assert.Equal(expected, NestJobCost.NetSheetCost(job, sheet), 10);
        Assert.Equal(expected, NestJobCost.NetSheetCost(job.Options, stock, new Box(0, 0, 4, 4)), 10);
        Assert.Equal(expected * 5, NestJobCost.NetSheetArea(job, sheet), 10);
    }

    [Fact]
    public void AggregateOverflowFailsClearly()
    {
        var stock = Stock("huge", double.MaxValue);
        var job = new NestJob(new[] { new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 4)), 2) }, new[] { stock });
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(stock, new[] { ("part", 0d, 0d, 0d) });
        builder.AddSheet(stock, new[] { ("part", 0d, 0d, 0d) });
        Assert.Throws<OverflowException>(() => NestJobCost.Evaluate(job, builder.Build(NestJobStopReason.Completed)));
    }

    [Fact]
    public void BuilderMapsLegacyZeroAndSnapshotsPositiveCosts()
    {
        var option = new PlateOption { Width = 10, Length = 10 };
        Assert.Null(Assert.Single(NestStockBuilder.FromTemplate(new Plate(10, 10), new[] { option })).Cost);
        option.Cost = 12;
        var stock = Assert.Single(NestStockBuilder.FromTemplate(new Plate(10, 10), new[] { option }));
        option.Cost = 50;
        Assert.Equal(12, stock.Cost);
        Assert.Throws<ArgumentException>(() => NestStockBuilder.FromTemplate(new Plate(10, 10),
            new[] { option, new PlateOption { Width = 12, Length = 12 } }));
    }
}

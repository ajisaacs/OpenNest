using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class StockLadderTests
{
    private static NestJobPart Rectangle(string id, int quantity, double x = 4, double y = 4,
        RotationPolicy? rotation = null) => new(id,
        PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(x, y)), quantity,
        rotation: rotation ?? RotationPolicy.Fixed(0));

    [Fact]
    public void MergesEquivalentDemandOntoLargerSheetAndReturnsFiniteStock()
    {
        var job = new NestJob(new[] { Rectangle("a", 5) }, new[]
        {
            new NestPlateStock("small", new Size(10, 10), 2),
            new NestPlateStock("large", new Size(10, 18), 1)
        });
        var result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("large", Assert.Single(result.Plates).StockId);
        Assert.Equal(5, Assert.Single(result.Fulfillment).Placed);
        Assert.Equal(0, result.StockUsage.Single(s => s.StockId == "small").Used);
        Assert.Equal(2, result.StockUsage.Single(s => s.StockId == "small").Remaining);
        Verify(job, result);
    }

    [Fact]
    public void FiniteStockAndPlateLimitDoNotOverproduce()
    {
        var parts = new[] { Rectangle("a", 9) };
        var stock = new[] { new NestPlateStock("only", new Size(10, 10), 1) };
        var job = new NestJob(parts, stock);
        var result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal(NestJobStopReason.StockExhausted, result.StopReason);
        Assert.Equal(4, result.Fulfillment[0].Placed);
        Verify(job, result);
        job = new NestJob(parts, new[] { new NestPlateStock("only", new Size(10, 10)) }, new NestJobOptions(maxPlates: 1));
        result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
        Assert.Single(result.Plates);
        Verify(job, result);
    }

    [Fact]
    public void ConstrainedLargeSinglePrecedesSmallFillers()
    {
        var job = new NestJob(new[] { Rectangle("small", 12, 2, 2), Rectangle("large", 1, 12, 6) }, new[]
        {
            new NestPlateStock("small-sheet", new Size(10, 10)),
            new NestPlateStock("large-sheet", new Size(10, 18))
        });
        var result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal("large", result.Plates[0].Placements[0].PartId);
        Assert.Contains(result.Plates[0].Placements, p => p.PartId == "small");
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Verify(job, result);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void GeometrySpacingRotationsAndQuadrantsAreValidated(int quadrant)
    {
        var job = new NestJob(new[] { Rectangle("a", 6, 3, 5, RotationPolicy.Fixed(System.Math.PI / 2)) },
            new[] { new NestPlateStock("sheet", new Size(12, 18), partSpacing: 0.25,
                edgeSpacing: new Spacing { Left = 0.5, Right = 0.5, Top = 0.5, Bottom = 0.5 }, quadrant: quadrant) });
        var result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Verify(job, result);
    }

    [Fact]
    public void ImpossibleDemandTerminatesWithoutUsingUnlimitedStock()
    {
        var job = new NestJob(new[] { Rectangle("a", 1, 100, 100) },
            new[] { new NestPlateStock("sheet", new Size(10, 10)) });
        var result = new StockLadderNestingEngine().Solve(job);
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
        Assert.Empty(result.Plates);
    }

    [Fact]
    public void CancellationBeforeAndDuringTrialNeverReturnsPartialSuccess()
    {
        var job = new NestJob(new[] { Rectangle("a", 1) }, new[] { new NestPlateStock("s", new Size(10, 10)) });
        using var cts = new CancellationTokenSource();
        var engine = new StockLadderNestingEngine(() => new CallbackNester(request =>
        {
            cts.Cancel();
            return new PlateCandidate(Array.Empty<NestJobPlacement>());
        }));
        Assert.Throws<OperationCanceledException>(() => engine.Solve(job, token: cts.Token));
        Assert.Throws<OperationCanceledException>(() => new StockLadderNestingEngine().Solve(job, token: cts.Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectsOverlappingOrOverproducingNester(bool overproduce)
    {
        var job = new NestJob(new[] { Rectangle("a", 2) }, new[] { new NestPlateStock("s", new Size(10, 10)) });
        var engine = new StockLadderNestingEngine(() => new CallbackNester(request =>
            new PlateCandidate(overproduce
                ? Enumerable.Repeat(new NestJobPlacement("a", 0, 0, 0, 0), 3)
                : new[] { new NestJobPlacement("a", 0, 50, 0, 0) })));
        Assert.Throws<InvalidOperationException>(() => engine.Solve(job));
        // Direct full-demand overlap check, not masked by the single-part feasibility probe limit.
        Assert.Throws<InvalidOperationException>(() => NestJobValidator.ValidateCandidate(
            new PlateCandidate(new[] { new NestJobPlacement("a", 0, 0, 0, 0), new NestJobPlacement("a", 1, 1, 1, 0) }),
            job.Plates[0], new Dictionary<string, int> { ["a"] = 2 }, job.Parts.ToDictionary(p => p.Id)));
    }

    [Fact]
    public void SalvageCreditsOnlyOneUsableEdgeRectangleAndDefaultsToZero()
    {
        var part = Rectangle("a", 1);
        var stock = new NestPlateStock("s", new Size(10, 10));
        var sheet = new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("a", 0, 0, 0, 0) });
        NestJob Job(double rate, double min) => new(new[] { part }, new[] { stock },
            new NestJobOptions(salvageRate: rate, minimumSalvageDimension: min));
        Assert.Equal(100, StockLadderNestingEngine.EstimateNetArea(Job(0.5, 0), sheet));
        Assert.Equal(100, StockLadderNestingEngine.EstimateNetArea(Job(0.5, 7), sheet));
        Assert.Equal(70, StockLadderNestingEngine.EstimateNetArea(Job(0.5, 5), sheet), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestJobOptions(salvageRate: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestJobOptions(salvageRate: 1.1));
    }

    [Fact]
    public void FailedRepackRetainsAllDemandAndFiniteStockAccounting()
    {
        var job = new NestJob(new[] { Rectangle("a", 5) }, new[]
        {
            new NestPlateStock("small", new Size(10, 10), 2),
            new NestPlateStock("large", new Size(10, 18), 1)
        });
        var fullDemandLargeTrials = 0;
        var engine = new StockLadderNestingEngine(() => new CallbackNester(request =>
        {
            var quantity = Assert.Single(request.Parts).Quantity;
            if (request.Stock.Id == "large" && quantity == 5) fullDemandLargeTrials++;
            // Deliberately fail to reproduce the fifth piece on the cheaper merged sheet.
            return new PlateCandidate(Enumerable.Range(0, System.Math.Min(quantity, 4))
                .Select(i => new NestJobPlacement("a", i, i % 2 * 4, i / 2 * 4, 0)));
        }));
        var result = engine.Solve(job);
        Assert.True(fullDemandLargeTrials >= 2); // Construction AND equivalent-demand repack ran.
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, result.Plates.Count);
        Assert.All(result.Plates, sheet => Assert.Equal("small", sheet.StockId));
        Assert.Equal(5, Assert.Single(result.Fulfillment).Placed);
        Assert.Equal(0, Assert.Single(result.Fulfillment).Unplaced);
        Assert.Equal(0, result.StockUsage.Single(s => s.StockId == "large").Used);
        Assert.Equal(1, result.StockUsage.Single(s => s.StockId == "large").Remaining);
        Verify(job, result);
    }

    [Theory]
    [InlineData(3.0, false)]
    [InlineData(4.0001, true)]
    public void OpenMarkMustRemainInsideClosedMaterial(double endX, bool reject)
    {
        var program = TestDrawingFactory.Rectangle(4, 4);
        program.MoveTo(2, 2);
        program.LineTo(endX, 2);
        var part = new NestJobPart("exterior-mark", PartGeometrySnapshot.FromProgram(program), 1);
        var job = new NestJob(new[] { part }, new[] { new NestPlateStock("s", new Size(10, 10)) });
        if (reject)
        {
            var error = Assert.Throws<ArgumentException>(() => new StockLadderNestingEngine().Solve(job));
            Assert.Contains("Open geometry leaves the closed material region", error.Message);
        }
        else
        {
            var result = new StockLadderNestingEngine().Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Verify(job, result);
        }
    }

    private static void Verify(NestJob job, NestJobResult result)
    {
        var parts = job.Parts.ToDictionary(p => p.Id);
        var remaining = job.Parts.ToDictionary(p => p.Id, p => p.Quantity);
        foreach (var sheet in result.Plates)
        {
            NestJobValidator.ValidateCandidate(new PlateCandidate(sheet.Placements), sheet.Stock, remaining, parts);
            foreach (var pose in sheet.Placements) remaining[pose.PartId]--;
        }
        foreach (var part in job.Parts)
        {
            var poses = result.Plates.SelectMany(p => p.Placements).Where(p => p.PartId == part.Id).ToList();
            Assert.Equal(Enumerable.Range(0, poses.Count), poses.Select(p => p.InstanceIndex));
            var fulfillment = result.Fulfillment.Single(p => p.PartId == part.Id);
            Assert.Equal(part.Quantity, fulfillment.Placed + fulfillment.Unplaced);
            Assert.Equal(poses.Count, fulfillment.Placed);
        }
        foreach (var stock in job.Plates)
        {
            var count = result.Plates.Count(p => p.StockId == stock.Id);
            var usage = result.StockUsage.Single(s => s.StockId == stock.Id);
            Assert.Equal(count, usage.Used);
            Assert.Equal(stock.Quantity - count, usage.Remaining);
            Assert.True(stock.Quantity == null || count <= stock.Quantity);
        }
    }

    private sealed class CallbackNester(Func<PlatePlacementRequest, PlateCandidate> callback) : IPlateNester
    {
        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => callback(request);
    }
}

using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobStockSelectionTests
{
    [Fact]
    public void LaterFittingStockWinsWhenFirstStockCannotPlace()
    {
        var result = Solve(new[] { Part("p", 1) }, new[] { Stock("small", 10, 10, 1), Stock("large", 20, 20, 1) },
            request => request.Stock.Id == "large" ? Candidate(request, "p") : Empty());

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("large", Assert.Single(result.Plates).StockId);
    }

    [Fact]
    public void ExhaustedLargeStockIsNotRecreatedWhileSmallerStockServesSmallParts()
    {
        var result = Solve(new[] { Part("large", 1, 0), Part("small", 2, 1) },
            new[] { Stock("large", 20, 20, 1), Stock("small", 10, 10, 2) }, request => request.Stock.Id switch
            {
                "large" when request.Parts.Any(part => part.Id == "large") => Candidate(request, "large"),
                "small" when request.Parts.Any(part => part.Id == "small") => Candidate(request, "small"),
                _ => Empty()
            });

        Assert.Equal(new[] { "large", "small", "small" }, result.Plates.Select(plate => plate.StockId));
        Assert.Collection(result.StockUsage,
            usage => Assert.Equal(new StockUsage("large", 1, 0), usage),
            usage => Assert.Equal(new StockUsage("small", 2, 0), usage));
    }

    [Fact]
    public void EqualDimensionsWithDifferentStockIdsRemainIndependent()
    {
        var result = Solve(new[] { Part("p", 2) }, new[] { Stock("first", 10, 10, 1), Stock("second", 10, 10, 1) },
            request => Candidate(request, "p"));

        Assert.Equal(new[] { "first", "second" }, result.Plates.Select(plate => plate.StockId));
        Assert.Equal(new[] { new StockUsage("first", 1, 0), new StockUsage("second", 1, 0) }, result.StockUsage);
    }

    [Fact]
    public void LosingTrialsDoNotConsumeStockPartsOrDrawingCounters()
    {
        var calls = new List<(string Stock, int Quantity)>();
        var result = Solve(new[] { Part("p", 2) }, new[] { Stock("wide", 20, 20, 2), Stock("narrow", 10, 10, 2) }, request =>
        {
            calls.Add((request.Stock.Id, request.Parts.Single().Quantity));
            return request.Stock.Id == "wide" ? Candidate(request, "p", 0, 100) : Candidate(request, "p", 0, 0);
        });

        Assert.Equal(new[] { "narrow", "narrow" }, result.Plates.Select(plate => plate.StockId));
        Assert.Equal(new[] { ("wide", 2), ("narrow", 2), ("wide", 1), ("narrow", 1) }, calls);
        Assert.Equal(new StockUsage("wide", 0, 2), result.StockUsage[0]);
        Assert.Equal(new StockUsage("narrow", 2, 0), result.StockUsage[1]);
        Assert.Equal(new[] { 0, 1 }, result.Plates.SelectMany(plate => plate.Placements).Select(placement => placement.InstanceIndex));
    }

    [Fact]
    public void CandidatePriorityAreaEnvelopeAndInputOrderAreComparedInDocumentedOrder()
    {
        var priority = Solve(new[] { Part("high", 1, 0), Part("low", 1, 1) }, new[] { Stock("a", 10, 10, 1), Stock("b", 10, 10, 1) },
            request => request.Stock.Id == "a" ? Candidate(request, "low") : Candidate(request, "high"));
        var area = Solve(new[] { Part("p", 1) }, new[] { Stock("large", 20, 20, 1), Stock("small", 10, 10, 1) },
            request => Candidate(request, "p"));
        var envelope = Solve(new[] { Part("p", 2) }, new[] { Stock("a", 10, 10, 1), Stock("b", 10, 10, 1) },
            request => request.Stock.Id == "a" ? CandidatePair("p", 4, 5) : CandidatePair("p", 4, 0));
        var inputOrder = Solve(new[] { Part("p", 1) }, new[] { Stock("first", 10, 10, 1), Stock("second", 10, 10, 1) },
            request => Candidate(request, "p"));

        Assert.Equal("b", priority.Plates[0].StockId);
        Assert.Equal("small", area.Plates[0].StockId);
        Assert.Equal("b", envelope.Plates[0].StockId);
        Assert.Equal("first", inputOrder.Plates[0].StockId);
    }

    [Fact]
    public void UnlimitedStockStopsWhenDemandIsFulfilledAndPlateLimitLeavesLeftovers()
    {
        var unlimited = Solve(new[] { Part("p", 2) }, new[] { Stock("u", 10, 10, null) }, request => Candidate(request, "p"));
        var limited = Solve(new[] { Part("p", 3) }, new[] { Stock("u", 10, 10, null) }, request => Candidate(request, "p"), new NestJobOptions(maxPlates: 2));

        Assert.Equal(NestJobStopReason.Completed, unlimited.StopReason);
        Assert.Equal(2, unlimited.Plates.Count);
        Assert.Equal(NestJobStopReason.PlateLimitReached, limited.StopReason);
        Assert.Equal(new PartFulfillment("p", 3, 2, 1), Assert.Single(limited.Fulfillment));
    }

    private static NestJobResult Solve(IEnumerable<NestJobPart> parts, IEnumerable<NestPlateStock> stock,
        Func<PlatePlacementRequest, PlateCandidate> place, NestJobOptions? options = null) =>
        new NestJobRunner(_ => new Nester(place)).Solve(new NestJob(parts, stock, options));

    private static NestJobPart Part(string id, int quantity, int priority = 0) =>
        new(id, PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 5)), quantity, priority);

    private static NestPlateStock Stock(string id, double width, double length, int? quantity) =>
        new(id, new Size(width, length), quantity);

    private static PlateCandidate Candidate(PlatePlacementRequest request, string id, double firstX = 0, double secondX = 0)
    {
        return new PlateCandidate(new[] { new NestJobPlacement(id, 0, firstX, 0, 0) });
    }

    private static PlateCandidate CandidatePair(string id, double secondX, double secondY) => new(new[]
    {
        new NestJobPlacement(id, 0, 0, 0, 0),
        new NestJobPlacement(id, 1, secondX, secondY, 0)
    });

    private static PlateCandidate Empty() => new(Array.Empty<NestJobPlacement>());

    private sealed class Nester(Func<PlatePlacementRequest, PlateCandidate> place) : IPlateNester
    {
        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => place(request);
    }
}

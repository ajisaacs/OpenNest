using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Clamp zones as engine input: immutable stock carry with the WorkArea quadrant
/// transform, validation of zone geometry, repeated-layout identity (C8) and the
/// layout-check violation against the D6-inflated rectangle (C1/C5).
/// </summary>
public class ClampZoneEngineTests
{
    private const double ZoneX = 5.25;
    private const double ZoneLength = 1.5;
    private const double ZoneWidth = 0.625;

    private static ClampZone Zone(string id = "clamp-0", double x = ZoneX) =>
        new(id, new Box(x, 0, ZoneLength, ZoneWidth));

    private static NestPlateStock Stock(
        int quadrant = 1,
        double bottomSpacing = 0.25,
        params ClampZone[] zones
    ) =>
        new("sheet", new Size(48, 96), null, 0.25,
            new Spacing(0.25, bottomSpacing, 0.25, 0.25), quadrant, null, zones);

    // ---- stock carry and quadrant transform ----------------------------------------

    [Fact]
    public void StockWithoutZonesExposesAnEmptyList()
    {
        var stock = new NestPlateStock("s", new Size(48, 96));
        Assert.Empty(stock.ClampZones);
    }

    [Theory]
    [InlineData(1, 0.0, 0.0)]
    [InlineData(2, -96.0, 0.0)]
    [InlineData(3, -96.0, -48.0)]
    [InlineData(4, 0.0, -48.0)]
    public void ZonesTransformWithTheWorkAreaIntoEveryQuadrant(int quadrant, double dx, double dy)
    {
        var stock = Stock(quadrant: quadrant, zones: Zone());

        var zone = Assert.Single(stock.ClampZones);
        Assert.Equal("clamp-0", zone.Id);
        Assert.Equal(ZoneX + dx, zone.Bounds.X); // exact bits: same translation WorkArea uses
        Assert.Equal(dy, zone.Bounds.Y);
        Assert.Equal(ZoneLength, zone.Bounds.Length);
        Assert.Equal(ZoneWidth, zone.Bounds.Width);

        // The keep-out stays inside the sheet box of this quadrant.
        var work = stock.WorkArea;
        Assert.True(zone.Bounds.Right <= work.Right + 0.25);
        Assert.True(zone.Bounds.Bottom >= work.Bottom - 0.25 - 0.25);
    }

    [Fact]
    public void StockDetachesZonesFromCallerBoxes()
    {
        var bounds = new Box(ZoneX, 0, ZoneLength, ZoneWidth);
        var zone = new ClampZone("clamp", bounds);
        var stock = Stock(zones: zone);
        bounds.Offset(500, 500);
        Assert.Equal(ZoneX, Assert.Single(stock.ClampZones).Bounds.X);
    }

    [Fact]
    public void MapperCarriesPlateZonesInBothDirections()
    {
        var plate = new Plate(48, 96) { Quadrant = 2 };
        plate.ClampZones.Add(Zone());

        var stock = DrawingJobMapper.FromPlate("s", plate, quantity: null);
        var zone = Assert.Single(stock.ClampZones);
        Assert.Equal(ZoneX - 96, zone.Bounds.X);
        Assert.Equal(0, zone.Bounds.Y);

        // Materializing the stock back keeps the placement frame: no second transform.
        var materialized = DrawingJobMapper.CreatePlate(stock);
        var copy = Assert.Single(materialized.ClampZones);
        Assert.Equal(zone.Bounds.X, copy.Bounds.X);
        Assert.Equal(0, copy.Bounds.Y); // quadrant 2 keeps Y at zero
        Assert.Equal("clamp-0", copy.Id);
    }

    [Fact]
    public void StockBuilderCarriesTemplateZonesForEveryOption()
    {
        var template = new Plate(48, 96);
        template.ClampZones.Add(Zone("a", 3));
        template.ClampZones.Add(Zone("b", 20));

        var single = Assert.Single(NestStockBuilder.SinglePlate(template));
        Assert.Equal(2, single.ClampZones.Count);

        var options = NestStockBuilder.FromTemplate(template,
            [new PlateOption { Width = 48, Length = 96 }, new PlateOption { Width = 40, Length = 80 }]);
        Assert.All(options, stock => Assert.Equal(2, stock.ClampZones.Count));
    }

    // ---- validator ------------------------------------------------------------------

    [Fact]
    public void ValidatorAcceptsEdgeFlushZones()
    {
        var job = new NestJob([], [Stock(zones: new[] { Zone(), Zone("flush", 94.5) })]);
        NestJobValidator.Validate(job); // y = 0 front edge and right-edge flush both legal
    }

    [Fact]
    public void ValidatorRejectsZonesOutsideTheSheet()
    {
        var beyond = new NestJob([], [Stock(zones: Zone(x: 95.5))]);
        Assert.Throws<ArgumentException>(() => NestJobValidator.Validate(beyond));

        var belowEdge = new NestJob([], [new NestPlateStock("s", new Size(48, 96),
            clampZones: [new ClampZone("under", new Box(5, -0.5, 1.5, 0.625))])]);
        Assert.Throws<ArgumentException>(() => NestJobValidator.Validate(belowEdge));
    }

    [Fact]
    public void ValidatorRejectsDuplicateZoneIds()
    {
        var job = new NestJob([], [Stock(zones: new[] { Zone("dup", 3), Zone("dup", 20) })]);
        Assert.Throws<ArgumentException>(() => NestJobValidator.Validate(job));
    }

    // ---- repeated-layout identity (C8) ----------------------------------------------

    [Fact]
    public void DifferentClampSetsAreDifferentStock()
    {
        var plain = new NestPlateStock("sheet", new Size(20, 30), 10, 0.25);
        var clamped = new NestPlateStock("sheet", new Size(20, 30), 10, 0.25,
            clampZones: [Zone()]);
        var sameAsPlain = new NestPlateStock("sheet", new Size(20, 30), 10, 0.25,
            clampZones: [Zone()]);

        Assert.False(RepeatedPlateGrouping.SameStock(plain, clamped));
        Assert.True(RepeatedPlateGrouping.SameStock(clamped, sameAsPlain));

        var pose = new NestJobPlacement("p", 0, 1, 1, 0);
        var groups = RepeatedPlateGrouping.Create(
        [
            new NestJobPlateResult(0, clamped, [pose]),
            new NestJobPlateResult(1, plain, [pose]),
            new NestJobPlateResult(2, sameAsPlain, [pose]),
        ], consolidate: true);
        // sheet2 repeats sheet0's exact layout and an identical clamp set, so only the
        // plain-clamp sheet stays a separate group; the clamped layout is never reused
        // for the plain sheet (C8).
        Assert.Equal(new[] { 2, 1 }, groups.Select(g => g.Quantity).ToArray());
    }

    // ---- layout check (C1/C5) ---------------------------------------------------------

    private static NestJobPart RectPart(string id = "p", double w = 2, double h = 2) =>
        new(id, PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(w, h)), 1);

    private static IReadOnlyList<string> Check(NestPlateStock stock, double x, double y)
    {
        var job = new NestJob([RectPart()], [stock]);
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, stock, [new NestJobPlacement("p", 0, x, y, 0)])],
            [], []);
        return NestLayoutCheck.Violations(job, result);
    }

    [Fact]
    public void CheckerFlagsPartsInsideTheInflatedClampRectangle()
    {
        // keep-out = zone inflated by bottom EdgeSpacing (0.25): x [~5.0, ~7.0], y up to ~0.875
        var violations = Check(Stock(zones: Zone()), x: 5.3, y: 0.4);
        Assert.Contains(violations, v => v.Contains("clamp-0") && v.Contains("clamp zone"));
    }

    [Fact]
    public void CheckerAcceptsPartsClearOfTheInflatedClampRectangle()
    {
        // Above the inflated depth (0.625 + 0.25) and clear in X beyond the inflated width.
        Assert.DoesNotContain(Check(Stock(zones: Zone()), x: 5.3, y: 1.0),
            v => v.Contains("clamp zone"));
        Assert.DoesNotContain(Check(Stock(zones: Zone()), x: 7.2, y: 0.4),
            v => v.Contains("clamp zone"));
    }

    [Fact]
    public void CheckerEnforcesTransformedZonesInOtherQuadrants()
    {
        // Quadrant 3 moves the front-edge zone to (5.25-96, -48); a part inside it must red.
        var stock = Stock(quadrant: 3, zones: Zone());
        var violations = Check(stock, x: 5.25 - 96 + 0.1, y: -48 + 0.4);
        Assert.Contains(violations, v => v.Contains("clamp zone"));

        // The same sheet a clear distance away from the transformed zone passes.
        Assert.DoesNotContain(Check(stock, x: -50, y: -40), v => v.Contains("clamp zone"));
    }

    [Fact]
    public void EmptyClampListAddsNoViolations()
    {
        Assert.DoesNotContain(Check(Stock(), x: 10, y: 10), v => v.Contains("clamp"));
    }
}

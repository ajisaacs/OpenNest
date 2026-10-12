using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// The fixed machine clamp rail: X centers measured from the plate origin along the
/// origin-side edge, resolved onto every sheet size without re-scaling (D2/D3).
/// </summary>
public class ClampRailTests
{
    // The user's machine: ~8 clamps, 1.5" along the edge x 0.625" into the sheet.
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private static ClampRail Machine() => new(1.5, 0.625, MachineCenters);

    // Plate(width, length): Width is the Y extent, Length the X extent along the clamp edge.
    private static Plate Sheet(double width, double length, int quadrant = 1) =>
        new(width, length) { Quadrant = quadrant };

    [Theory]
    [InlineData(0, 0.625)]
    [InlineData(-1, 0.625)]
    [InlineData(double.NaN, 0.625)]
    [InlineData(double.PositiveInfinity, 0.625)]
    [InlineData(1.5, 0)]
    [InlineData(1.5, -0.5)]
    [InlineData(1.5, double.NaN)]
    public void NonPositiveOrNonFiniteSizesAreRejected(double width, double depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClampRail(width, depth, new[] { 6.0 }));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CentersMustBeFiniteAndFromTheOrigin(double center)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClampRail(1.5, 0.625, new[] { 6, center }));
        Assert.Throws<ArgumentNullException>(() => new ClampRail(1.5, 0.625, null!));
    }

    [Fact]
    public void CentersAreSortedAndDistinct()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 36.0, 6, 21, 6 });
        Assert.Equal(new[] { 6.0, 21, 36 }, rail.Centers);
    }

    [Fact]
    public void FullLengthSheetGetsEveryClampOnTheFrontEdge()
    {
        var zones = Machine().ResolveFor(new Size(60, 120), quadrant: 1);

        Assert.Equal(8, zones.Count);
        for (var i = 0; i < zones.Count; i++)
        {
            Assert.Equal($"clamp-{i + 1}", zones[i].Id);
            Assert.Equal(MachineCenters[i] - 0.75, zones[i].Bounds.X);
            Assert.Equal(0, zones[i].Bounds.Y);
            Assert.Equal(1.5, zones[i].Bounds.Length);
            Assert.Equal(0.625, zones[i].Bounds.Width);
        }
    }

    [Fact]
    public void ShorterSheetSkipsClampsBeyondItWithoutRescaling()
    {
        // D2: a 60" sheet keeps the clamps at their table positions and drops the rest.
        var zones = Machine().ResolveFor(new Size(48, 60), quadrant: 1);

        Assert.Equal(new[] { "clamp-1", "clamp-2", "clamp-3", "clamp-4" }, zones.Select(z => z.Id));
        Assert.Equal(new[] { 5.25, 20.25, 35.25, 50.25 }, zones.Select(z => z.Bounds.X));
    }

    [Fact]
    public void ClampStraddlingTheSheetEndIsClippedNotDropped()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 59.5, 60.5, 60.75, 61 });

        var zones = rail.ResolveFor(new Size(48, 60), quadrant: 1);

        // 59.5 covers [58.75, 60]; 60.5 still reaches onto the sheet [59.75, 60];
        // 60.75 only touches the end and 61 lies wholly beyond it.
        Assert.Equal(new[] { "clamp-1", "clamp-2" }, zones.Select(z => z.Id));
        Assert.Equal(58.75, zones[0].Bounds.X);
        Assert.Equal(1.25, zones[0].Bounds.Length);
        Assert.Equal(59.75, zones[1].Bounds.X);
        Assert.Equal(0.25, zones[1].Bounds.Length);
    }

    [Fact]
    public void ClampAtTheOriginAndDeepClampsAreClippedToTheSheet()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 0.25 });
        var origin = Assert.Single(rail.ResolveFor(new Size(48, 96), quadrant: 1));
        Assert.Equal(0, origin.Bounds.X);
        Assert.Equal(1.0, origin.Bounds.Length);

        var deep = Assert.Single(new ClampRail(1.5, 10, new[] { 6.0 }).ResolveFor(new Size(4, 96), 1));
        Assert.Equal(4, deep.Bounds.Width);
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(48, 0)]
    [InlineData(double.NaN, 96)]
    public void DegenerateSheetsGetNoZones(double width, double length)
    {
        Assert.Empty(Machine().ResolveFor(new Size(width, length), quadrant: 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ZonesSitOnTheOriginEdgeMeasuredFromTheOriginInEveryQuadrant(int quadrant)
    {
        var plate = Sheet(48, 96, quadrant);
        plate.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0 }));
        var zone = Assert.Single(plate.ClampZones);

        // Plate coordinates start at the sheet's lower-left corner; translating by the
        // sheet's placement corner gives machine coordinates around the origin.
        var sheet = plate.BoundingBox(includeParts: false);
        var machine = zone.Bounds.Translate(sheet.X, sheet.Y);
        var awayFromOrigin = quadrant is 1 or 4 ? 1 : -1;
        var intoSheet = quadrant is 1 or 2 ? 1 : -1;

        Assert.Equal(6.0, System.Math.Abs(machine.Center.X), 12);
        Assert.Equal(awayFromOrigin, System.Math.Sign(machine.Center.X));
        Assert.Equal(0.0, intoSheet > 0 ? machine.Bottom : machine.Top, 12);
        Assert.Equal(0.625, machine.Width);
        Assert.Equal(1.5, machine.Length, 12);
        Assert.True(zone.Bounds.Left >= 0 && zone.Bounds.Right <= 96 + 1e-9);
        Assert.True(zone.Bounds.Bottom >= 0 && zone.Bounds.Top <= 48 + 1e-9);
    }

    [Fact]
    public void PlateFollowsItsRailWhenResizedOrRotated()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());
        Assert.Equal(8, plate.ClampZones.Count);
        Assert.True(plate.ClampRail.Matches(plate));

        plate.Size = new Size(48, 60);
        Assert.Equal(4, plate.ClampZones.Count);

        // Rotating swaps the sheet's extents; the machine's clamps stay where they are.
        plate.Rotate90(RotationType.CW);
        Assert.Equal(new Size(60, 48), plate.Size);
        Assert.Equal(new[] { 5.25, 20.25, 35.25 }, plate.ClampZones.Select(z => z.Bounds.X));

        plate.Quadrant = 2;
        Assert.True(plate.ClampRail.Matches(plate));
        Assert.Equal(48 - 6.75, plate.ClampZones[0].Bounds.X);
    }

    [Fact]
    public void UnchangedResolutionRaisesNoEvents()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());
        var original = plate.ClampZones.ToList();
        var changes = 0;
        plate.ClampZonesChanged += (_, _) => changes++;

        plate.Size = new Size(48, 120);
        plate.Quadrant = 1;
        plate.SetClampRail(Machine());

        Assert.Equal(0, changes);
        Assert.Equal(original, plate.ClampZones); // same zone instances
    }

    [Fact]
    public void DetachingTheRailRemovesItsZones()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());

        plate.SetClampRail(null!);

        Assert.Null(plate.ClampRail);
        Assert.Empty(plate.ClampZones);
        plate.Size = new Size(48, 96);
        Assert.Empty(plate.ClampZones);
    }

    [Fact]
    public void PlatesWithoutARailKeepExplicitZonesThroughResizes()
    {
        var plate = Sheet(48, 96);
        plate.ClampZones.Add(new ClampZone("manual", new Box(5.25, 0, 1.5, 0.625)));

        plate.Size = new Size(48, 120);
        plate.Quadrant = 3;

        Assert.Equal("manual", Assert.Single(plate.ClampZones).Id);
        Assert.False(Machine().Matches(plate));
    }

    [Fact]
    public void NewPlatesFollowTheNestRailAtTheirOwnSize()
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 60);
        nest.SetClampRail(Machine());

        var plate = nest.CreatePlate();

        Assert.Same(nest.PlateDefaults.ClampRail, plate.ClampRail);
        Assert.Equal(4, plate.ClampZones.Count);
        plate.Size = new Size(60, 120);
        Assert.Equal(8, plate.ClampZones.Count);
    }

    [Fact]
    public void NestRailReplacesEveryPlatesZones()
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 120);
        var a = nest.CreatePlate();
        var b = nest.CreatePlate();
        b.Size = new Size(48, 60);
        b.ClampZones.Add(new ClampZone("manual", new Box(1, 0, 1, 1)));

        nest.SetClampRail(Machine());
        Assert.Equal(8, a.ClampZones.Count);
        Assert.Equal(4, b.ClampZones.Count);
        Assert.DoesNotContain(b.ClampZones, z => z.Id == "manual");

        // A rail without clamps is no rail: zones and the template are cleared everywhere.
        nest.SetClampRail(new ClampRail(1.5, 0.625, Array.Empty<double>()));
        Assert.Null(nest.PlateDefaults.ClampRail);
        Assert.All(nest.Plates, p => Assert.Empty(p.ClampZones));
        Assert.Empty(nest.CreatePlate().ClampZones);
    }

    [Fact]
    public void RailZonesStayOutOfQuantityAndUtilization()
    {
        var drawing = new Drawing("part", new OpenNest.CNC.Program());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();

        Assert.Equal(8, plate.ClampZones.Count);
        Assert.Empty(plate.Parts);
        Assert.Equal(0, plate.Utilization());
        nest.UpdateDrawingQuantities();
        Assert.Equal(0, drawing.Quantity.Nested);
    }

    [Fact]
    public void RailsCompareByValue()
    {
        Assert.Equal(Machine(), new ClampRail(1.5, 0.625, MachineCenters.Reverse()));
        Assert.Equal(Machine().GetHashCode(), new ClampRail(1.5, 0.625, MachineCenters).GetHashCode());
        Assert.NotEqual(Machine(), new ClampRail(1.5, 0.5, MachineCenters));
        Assert.NotEqual(Machine(), new ClampRail(1.5, 0.625, MachineCenters.Take(7)));
    }
}

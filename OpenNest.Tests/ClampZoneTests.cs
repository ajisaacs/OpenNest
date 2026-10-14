using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// Rectangular machine-clamp keep-out zones: a first-class rectangle on the plate,
/// never a part. Zones are exclusion and display only - quantity, utilization and
/// cut accounting must never see them.
/// </summary>
public class ClampZoneTests
{
    private static ClampZone Valid(string id = "clamp-1") =>
        new(id, new Box(5.25, 0, 1.5, 0.625));

    [Fact]
    public void ZoneCarriesIdAndBounds()
    {
        var zone = Valid();
        Assert.Equal("clamp-1", zone.Id);
        Assert.Equal(5.25, zone.Bounds.X);
        Assert.Equal(0, zone.Bounds.Y);
        Assert.Equal(1.5, zone.Bounds.Length);
        Assert.Equal(0.625, zone.Bounds.Width);
    }

    [Fact]
    public void ZoneOwnsADetachedCopyOfItsBounds()
    {
        var bounds = new Box(5.25, 0, 1.5, 0.625);
        var zone = new ClampZone("clamp", bounds);
        bounds.Offset(100, 100);
        Assert.Equal(5.25, zone.Bounds.X);
        Assert.Equal(0, zone.Bounds.Y);
    }

    [Theory]
    [InlineData(double.NaN, 0, 1.5, 0.625)]
    [InlineData(0, double.PositiveInfinity, 1.5, 0.625)]
    [InlineData(0, 0, double.NaN, 0.625)]
    [InlineData(0, 0, 1.5, 0)]          // zero-size zone keeps out nothing
    [InlineData(0, 0, -1.5, 0.625)]     // negative size is not a rectangle
    [InlineData(0, 0, 1.5, -0.625)]
    public void MalformedBoundsAreRejected(double x, double y, double length, double width)
    {
        Assert.Throws<ArgumentException>(() => new ClampZone("clamp", new Box(x, y, length, width)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingIdIsRejected(string? id)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new ClampZone(id!, new Box(0, 0, 1.5, 0.625))
        );
    }

    [Fact]
    public void NullBoundsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ClampZone("clamp", null!));
    }

    [Fact]
    public void PlateStartsEmptyAndKeepsIndependentLists()
    {
        var a = new Plate(48, 96);
        var b = new Plate(48, 96);
        Assert.Empty(a.ClampZones);
        a.ClampZones.Add(Valid());
        Assert.Single(a.ClampZones);
        Assert.Empty(b.ClampZones);
    }

    [Fact]
    public void ZoneChangesRaiseClampZonesChanged()
    {
        var plate = new Plate(48, 96);
        var raised = 0;
        plate.ClampZonesChanged += (_, _) => raised++;

        var zone = Valid();
        plate.ClampZones.Add(zone);
        Assert.Equal(1, raised);

        plate.ClampZones.Remove(zone);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ZonesAreInvisibleToQuantityAndUtilization()
    {
        var plate = new Plate(48, 96) { Quantity = 1 };
        for (var i = 0; i < 8; i++)
            plate.ClampZones.Add(new ClampZone($"clamp-{i}", new Box(i * 2.0, 0, 1.5, 0.625)));

        var drawing = MakeRectDrawing(10, 5);
        drawing.Quantity.Required = 4;
        var part = new Part(drawing);
        plate.Parts.Add(part);

        // The single placed part accounts exactly as it does on a plate without clamps.
        Assert.Equal(1, drawing.Quantity.Nested);
        Assert.Single(plate.Parts); // clamp zones never enter Parts
        Assert.Equal(drawing.Area / plate.Area(), plate.Utilization(), 9);

        plate.Parts.Remove(part);
        Assert.Equal(0, drawing.Quantity.Nested);
    }

    [Fact]
    public void ZonesNeverEnterCutSequenceOrPartList()
    {
        var plate = new Plate(48, 96);
        plate.ClampZones.Add(Valid());
        Assert.All(plate.Parts, p => Assert.False(p.BaseDrawing.IsCutOff));
        Assert.Empty(plate.Parts);
        Assert.DoesNotContain(plate.ClampZones, z => ReferenceEquals(z, null));
    }

    private static Drawing MakeRectDrawing(double length, double width)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }
}

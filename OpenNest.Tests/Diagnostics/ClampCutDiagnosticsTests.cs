using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class ClampCutDiagnosticsTests
{
    [Theory]
    [InlineData(CutOffAxis.Vertical, 10.5, 0, 1, true)]
    [InlineData(CutOffAxis.Vertical, 10.5, 2, 4, false)]
    [InlineData(CutOffAxis.Vertical, 14, 0, 2, false)]
    [InlineData(CutOffAxis.Horizontal, 0.25, 9, 13, true)]
    [InlineData(CutOffAxis.Horizontal, 2, 9, 13, false)]
    [InlineData(CutOffAxis.Horizontal, 0.25, 12, 13, false)]
    public void NominalLimitsAndBothAxesDetermineContact(CutOffAxis axis, double coordinate,
        double start, double end, bool crosses)
    {
        var plate = new Plate(24, 48);
        plate.ClampZones.Add(new ClampZone("front", new Box(10, 0, 1.5, 0.625)));
        plate.CutOffs.Add(new CutOff(axis == CutOffAxis.Vertical
            ? new Vector(coordinate, 0) : new Vector(0, coordinate), axis)
        { StartLimit = start, EndLimit = end });
        var findings = ClampCutDiagnostics.Find(plate);
        Assert.Equal(crosses, findings.Count != 0);
        if (crosses)
            Assert.Equal("front", Assert.Single(findings).ClampId);
    }

    [Fact]
    public void SegmentChecksPhysicalFootprintNotInfiniteLineOrPartSpacing()
    {
        var zone = new Box(10, 0, 1.5, 0.625);
        Assert.True(ClampCutDiagnostics.Intersects(new Vector(9, 0.3), new Vector(12, 0.3),
            zone, out var contact));
        Assert.InRange(contact.X, 10, 11.5);
        Assert.False(ClampCutDiagnostics.Intersects(new Vector(9, 3), new Vector(12, 3),
            zone, out _));
        Assert.False(ClampCutDiagnostics.Intersects(new Vector(9, 0.3), new Vector(9.9, 0.3),
            zone, out _));
        Assert.Throws<ArgumentException>(() => ClampCutDiagnostics.Intersects(
            new Vector(double.NaN, 0), new Vector(12, 0), zone, out _));
    }

    [Fact]
    public void OnlyActiveClampsAreReported()
    {
        var plate = new Plate(24, 48);
        plate.ClampZones.Add(new ClampZone("front", new Box(10, 0, 1.5, 0.625)));
        plate.CutOffs.Add(new CutOff(new Vector(10.5, 0), CutOffAxis.Vertical));
        Assert.Single(ClampCutDiagnostics.Find(plate));
        plate.ClampZones.Clear(); // Removed on this sheet; no longer an obstacle.
        Assert.Empty(ClampCutDiagnostics.Find(plate));
    }
}

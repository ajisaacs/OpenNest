using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Engine;

public class CanonicalAngleTests
{
    private const double AngleTol = 0.002; // ~0.11°

    private static Drawing MakeRect(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    private static Drawing RotateCopy(Drawing src, double angle)
    {
        var pgm = src.Program.Clone() as OpenNest.CNC.Program;
        pgm.Rotate(angle, pgm.BoundingBox().Center);
        return new Drawing("rotated", pgm);
    }

    [Fact]
    public void AxisAlignedRectangle_ReturnsZero()
    {
        var d = MakeRect(100, 50);
        Assert.Equal(0.0, CanonicalAngle.Compute(d), precision: 6);
    }

    // Program.BoundingBox() has a pre-existing bug where minX/minY initialize to 0 and can
    // only decrease, so programs whose extents stay in the positive half-plane report a
    // too-large AABB. To validate MBR-axis-alignment without tripping that bug, extract the
    // outer perimeter polygon and compute its true AABB from vertices.
    private static (double length, double width) TrueAabb(OpenNest.CNC.Program pgm)
    {
        var entities = ConvertProgram.ToGeometry(pgm).Where(e => e.Layer != SpecialLayers.Rapid);
        var shapes = ShapeBuilder.GetShapes(entities);
        var outer = shapes.OrderByDescending(s => s.Area()).First();
        var poly = outer.ToPolygonWithTolerance(0.1);
        var minX = poly.Vertices.Min(v => v.X);
        var maxX = poly.Vertices.Max(v => v.X);
        var minY = poly.Vertices.Min(v => v.Y);
        var maxY = poly.Vertices.Max(v => v.Y);
        return (maxX - minX, maxY - minY);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.2)]
    public void Rectangle_ReturnsNegatedRotation_Modulo90(double theta)
    {
        var rotated = RotateCopy(MakeRect(100, 50), theta);
        var angle = CanonicalAngle.Compute(rotated);

        // Applying the returned angle should leave MBR axis-aligned.
        var canonical = rotated.Program.Clone() as OpenNest.CNC.Program;
        canonical.Rotate(angle, canonical.BoundingBox().Center);

        var (length, width) = TrueAabb(canonical);
        var longer = System.Math.Max(length, width);
        var shorter = System.Math.Min(length, width);
        Assert.InRange(longer, 100 - 0.1, 100 + 0.1);
        Assert.InRange(shorter, 50 - 0.1, 50 + 0.1);
    }

    [Fact]
    public void NearZeroInput_SnapsToZero()
    {
        var rotated = RotateCopy(MakeRect(100, 50), 0.0005);
        Assert.Equal(0.0, CanonicalAngle.Compute(rotated), precision: 6);
    }

    [Fact]
    public void DegeneratePolygon_ReturnsZero()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        var d = new Drawing("line", pgm);
        Assert.Equal(0.0, CanonicalAngle.Compute(d), precision: 6);
    }

    [Fact]
    public void EmptyProgram_ReturnsZero()
    {
        var d = new Drawing("empty", new OpenNest.CNC.Program());
        Assert.Equal(0.0, CanonicalAngle.Compute(d), precision: 6);
    }
}

using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Engine;

public class CanonicalFrameTests
{
    private static Drawing MakeRect(double w, double h, double rotation)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        if (!Tolerance.IsEqualTo(rotation, 0))
            pgm.Rotate(rotation, pgm.BoundingBox().Center);
        return new Drawing("rect", pgm) { Source = new SourceInfo { Angle = -rotation } };
    }

    [Fact]
    public void AsCanonicalCopy_AxisAlignsMbr()
    {
        var d = MakeRect(100, 50, 0.6);
        var canonical = CanonicalFrame.AsCanonicalCopy(d);

        var bb = canonical.Program.BoundingBox();
        var longer = System.Math.Max(bb.Length, bb.Width);
        var shorter = System.Math.Min(bb.Length, bb.Width);
        Assert.InRange(longer, 100 - 0.1, 100 + 0.1);
        Assert.InRange(shorter, 50 - 0.1, 50 + 0.1);
        Assert.Equal(0.0, canonical.Source.Angle, precision: 6);
    }

    [Fact]
    public void AsCanonicalCopy_DoesNotMutateSource()
    {
        var d = MakeRect(100, 50, 0.6);
        var originalBbox = d.Program.BoundingBox();
        var originalAngle = d.Source.Angle;

        CanonicalFrame.AsCanonicalCopy(d);

        var afterBbox = d.Program.BoundingBox();
        Assert.Equal(originalBbox.Width, afterBbox.Width, precision: 6);
        Assert.Equal(originalBbox.Length, afterBbox.Length, precision: 6);
        Assert.Equal(originalAngle, d.Source.Angle, precision: 6);
    }

    [Fact]
    public void FromCanonical_ComposesSourceAngleOntoRotation()
    {
        var d = MakeRect(100, 50, 0.0);
        var part = new Part(d);
        part.Rotate(0.2); // engine returned a canonical-frame part at R = 0.2

        var placed = CanonicalFrame.FromCanonical(new List<Part> { part }, sourceAngle: -0.5);

        // R' = R + sourceAngle = 0.2 + (-0.5) = -0.3
        // Part.Rotation comes from Program.Rotation which is normalized to [0, 2PI),
        // so compare after normalizing the expected value as well.
        Assert.Single(placed);
        Assert.Equal(Angle.NormalizeRad(-0.3), placed[0].Rotation, precision: 4);
    }

    [Fact]
    public void RoundTrip_RestoresGeometry()
    {
        var d = MakeRect(100, 50, 0.4);
        var canonical = CanonicalFrame.AsCanonicalCopy(d);

        // Place a part at origin in the canonical frame.
        var part = Part.CreateAtOrigin(canonical);
        var canonicalBbox = part.BoundingBox;

        var placed = CanonicalFrame.FromCanonical(new List<Part> { part }, d.Source.Angle);

        var originalBbox = d.Program.BoundingBox();
        Assert.Equal(originalBbox.Width, placed[0].BoundingBox.Width, precision: 2);
        Assert.Equal(originalBbox.Length, placed[0].BoundingBox.Length, precision: 2);
    }
}

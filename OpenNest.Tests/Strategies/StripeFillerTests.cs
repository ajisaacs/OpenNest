using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Tests.Strategies;

public class StripeFillerTests
{
    private static Drawing MakeRectDrawing(double w, double h, string name = "rect")
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing(name, pgm);
    }

    private static Pattern MakeRectPattern(double w, double h)
    {
        var drawing = MakeRectDrawing(w, h);
        var part = Part.CreateAtOrigin(drawing);
        var pattern = new Pattern();
        pattern.Parts.Add(part);
        pattern.UpdateBounds();
        return pattern;
    }

    [Fact]
    public void FindAngleForTargetSpan_ZeroAngle_WhenAlreadyMatches()
    {
        var pattern = MakeRectPattern(20, 10);
        var angle = StripeFiller.FindAngleForTargetSpan(
            pattern.Parts, 20.0, NestDirection.Horizontal);

        Assert.True(System.Math.Abs(angle) < 0.05,
            $"Expected angle near 0, got {OpenNest.Math.Angle.ToDegrees(angle):F1}°");
    }

    [Fact]
    public void FindAngleForTargetSpan_FindsLargerSpan()
    {
        var pattern = MakeRectPattern(20, 10);
        var angle = StripeFiller.FindAngleForTargetSpan(
            pattern.Parts, 22.0, NestDirection.Horizontal);

        var rotated = FillHelpers.BuildRotatedPattern(pattern.Parts, angle);
        var span = rotated.BoundingBox.Width;
        Assert.True(System.Math.Abs(span - 22.0) < 0.5,
            $"Expected span ~22, got {span:F2} at {OpenNest.Math.Angle.ToDegrees(angle):F1}°");
    }

    [Fact]
    public void FindAngleForTargetSpan_ReturnsClosest_WhenUnreachable()
    {
        var pattern = MakeRectPattern(20, 10);
        var angle = StripeFiller.FindAngleForTargetSpan(
            pattern.Parts, 30.0, NestDirection.Horizontal);

        Assert.True(angle >= 0 && angle <= System.Math.PI / 2);
    }
}

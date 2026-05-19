using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

public class BestFitResultFrameTests
{
    [Fact]
    public void BuildCanonicalParts_NonAxisAlignedPairNormalizesActualBounds()
    {
        var drawing = new TShape { Width = 10, Height = 8 }.GetDrawing();
        var canonical = CanonicalFrame.AsCanonicalCopy(drawing);

        var result = EvaluateOffsetPair(canonical, new Vector(40, 30));

        Assert.True(IsNonAxisAligned(result.OptimalRotation),
            $"Expected a non-axis-aligned result, got {Angle.ToDegrees(result.OptimalRotation):F2} degrees.");

        var parts = result.BuildCanonicalParts();
        var bounds = result.GetCutBounds(parts);

        Assert.Equal(0, bounds.Left, 3);
        Assert.Equal(0, bounds.Bottom, 3);
        Assert.Equal(result.BoundingWidth, bounds.Length, 2);
        Assert.Equal(result.BoundingHeight, bounds.Width, 2);
    }

    [Fact]
    public void BuildSourceParts_RebindsCanonicalResultToRotatedSourceDrawing()
    {
        var drawing = new TShape { Width = 10, Height = 8 }.GetDrawing();
        drawing.Program.Rotate(Angle.ToRadians(30), drawing.Program.BoundingBox().Center);
        drawing.RecomputeCanonicalAngle();

        var canonical = CanonicalFrame.AsCanonicalCopy(drawing);
        var result = EvaluateOffsetPair(canonical, new Vector(40, 30));

        var parts = result.BuildSourceParts(drawing);
        var bounds = result.GetCutBounds(parts);

        Assert.All(parts, p => Assert.Same(drawing, p.BaseDrawing));
        Assert.Equal(0, bounds.Left, 3);
        Assert.Equal(0, bounds.Bottom, 3);
        Assert.False(parts[0].Intersects(parts[1], out _));
    }

    private static BestFitResult EvaluateOffsetPair(Drawing drawing, Vector offset)
    {
        var candidate = new PairCandidate
        {
            Drawing = drawing,
            Part1Rotation = 0,
            Part2Rotation = System.Math.PI,
            Part2Offset = offset,
            Spacing = 0.25
        };

        return new PairEvaluator().Evaluate(candidate);
    }

    private static bool IsNonAxisAligned(double angle)
    {
        var normalized = Angle.NormalizeRad(angle);
        var nearestQuadrant = Angle.HalfPI * System.Math.Round(normalized / Angle.HalfPI);
        var delta = System.Math.Abs(normalized - nearestQuadrant);
        delta = System.Math.Min(delta, Angle.HalfPI - delta);
        return delta > Angle.ToRadians(1);
    }
}

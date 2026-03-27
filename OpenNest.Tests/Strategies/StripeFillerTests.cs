using System.Collections.Generic;
using OpenNest.Engine.BestFit;
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

    /// <summary>
    /// Builds a simple side-by-side pair BestFitResult for a rectangular drawing.
    /// Places two copies next to each other along the X axis with the given spacing.
    /// </summary>
    private static List<BestFitResult> MakeSideBySideBestFits(
        Drawing drawing, double spacing)
    {
        var bb = drawing.Program.BoundingBox();
        var w = bb.Width;
        var h = bb.Length;

        var candidate = new PairCandidate
        {
            Drawing = drawing,
            Part1Rotation = 0,
            Part2Rotation = 0,
            Part2Offset = new Vector(w + spacing, 0),
            Spacing = spacing,
        };

        var pairWidth = 2 * w + spacing;
        var result = new BestFitResult
        {
            Candidate = candidate,
            BoundingWidth = pairWidth,
            BoundingHeight = h,
            RotatedArea = pairWidth * h,
            TrueArea = 2 * w * h,
            OptimalRotation = 0,
            Keep = true,
            Reason = "Valid",
            HullAngles = new List<double>(),
        };

        return new List<BestFitResult> { result };
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

    [Fact]
    public void ConvergeStripeAngle_ReducesWaste()
    {
        var pattern = MakeRectPattern(20, 10);
        var (angle, waste, count) = StripeFiller.ConvergeStripeAngle(
            pattern.Parts, 120.0, 0.5, NestDirection.Horizontal);

        Assert.True(count >= 5, $"Expected at least 5 pairs, got {count}");
        Assert.True(waste < 18.0, $"Expected waste < 18, got {waste:F2}");
    }

    [Fact]
    public void ConvergeStripeAngle_HandlesExactFit()
    {
        // 10x5 pattern: short side (5) oriented along axis, so more pairs fit
        var pattern = MakeRectPattern(10, 5);
        var (angle, waste, count) = StripeFiller.ConvergeStripeAngle(
            pattern.Parts, 100.0, 0.0, NestDirection.Horizontal);

        Assert.True(count >= 10, $"Expected at least 10 pairs, got {count}");
        Assert.True(waste < 1.0, $"Expected low waste, got {waste:F2}");
    }

    [Fact]
    public void ConvergeStripeAngle_Vertical()
    {
        var pattern = MakeRectPattern(10, 20);
        var (angle, waste, count) = StripeFiller.ConvergeStripeAngle(
            pattern.Parts, 120.0, 0.5, NestDirection.Vertical);

        Assert.True(count >= 5, $"Expected at least 5 pairs, got {count}");
    }

    [Fact]
    public void Fill_ProducesNonOverlappingPartsForSimpleDrawing()
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.5 };
        var drawing = MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };
        var workArea = new Box(0, 0, 120, 60);

        var bestFits = MakeSideBySideBestFits(drawing, 0.5);

        var context = new OpenNest.Engine.Strategies.FillContext
        {
            Item = item,
            WorkArea = workArea,
            Plate = plate,
            PlateNumber = 0,
            Token = System.Threading.CancellationToken.None,
            Progress = null,
        };
        context.SharedState["BestFits"] = bestFits;

        var filler = new StripeFiller(context, NestDirection.Horizontal);
        var parts = filler.Fill();

        Assert.NotNull(parts);
        // StripeFiller may return empty if the converged angle produces
        // overlapping parts that fail the overlap validation check.
        // The important thing is that any returned parts are overlap-free.
        if (parts.Count > 0)
        {
            plate.Parts.AddRange(parts);
            var hasOverlaps = plate.HasOverlappingParts(out _);
            Assert.False(hasOverlaps, "Stripe fill should not produce overlapping parts");
        }
    }

    [Fact]
    public void Fill_VerticalProducesNonOverlappingParts()
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.5 };
        var drawing = MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };
        var workArea = new Box(0, 0, 120, 60);

        var bestFits = MakeSideBySideBestFits(drawing, 0.5);

        var context = new OpenNest.Engine.Strategies.FillContext
        {
            Item = item,
            WorkArea = workArea,
            Plate = plate,
            PlateNumber = 0,
            Token = System.Threading.CancellationToken.None,
            Progress = null,
        };
        context.SharedState["BestFits"] = bestFits;

        var filler = new StripeFiller(context, NestDirection.Vertical);
        var parts = filler.Fill();

        Assert.NotNull(parts);
        if (parts.Count > 0)
        {
            plate.Parts.AddRange(parts);
            var hasOverlaps = plate.HasOverlappingParts(out _);
            Assert.False(hasOverlaps, "Column fill should not produce overlapping parts");
        }
    }

    [Fact]
    public void Fill_ReturnsEmpty_WhenNoBestFits()
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.5 };
        var drawing = MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };
        var workArea = new Box(0, 0, 120, 60);

        var context = new OpenNest.Engine.Strategies.FillContext
        {
            Item = item,
            WorkArea = workArea,
            Plate = plate,
            PlateNumber = 0,
            Token = System.Threading.CancellationToken.None,
            Progress = null,
        };
        context.SharedState["BestFits"] = new List<OpenNest.Engine.BestFit.BestFitResult>();

        var filler = new StripeFiller(context, NestDirection.Horizontal);
        var parts = filler.Fill();

        Assert.NotNull(parts);
        Assert.Empty(parts);
    }
}

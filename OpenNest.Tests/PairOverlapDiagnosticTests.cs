using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit.Abstractions;

namespace OpenNest.Tests;

public class PairOverlapDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public PairOverlapDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Creates a 5x3.31 rectangle with rounded corners on the top-right and bottom-right
    /// (radius 0.5), similar to "4526 A14 PT13".
    /// </summary>
    private static Drawing MakeRoundedRect(double w = 5.0, double h = 3.31, double r = 0.5)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        // Bottom edge
        pgm.Codes.Add(new LinearMove(new Vector(w - r, 0)));
        // Bottom-right rounded corner
        pgm.Codes.Add(new ArcMove(new Vector(w, r), new Vector(w - r, r), RotationType.CW));
        // Right edge
        pgm.Codes.Add(new LinearMove(new Vector(w, h - r)));
        // Top-right rounded corner
        pgm.Codes.Add(new ArcMove(new Vector(w - r, h), new Vector(w - r, h - r), RotationType.CW));
        // Top edge
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        // Left edge back to start
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rounded-rect", pgm);
    }

    private static Drawing MakeSimpleRect(double w = 5.0, double h = 3.31)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    [Theory]
    [InlineData(0)]       // 0 degrees
    [InlineData(90)]      // 90 degrees
    [InlineData(180)]     // 180 degrees
    [InlineData(270)]     // 270 degrees
    public void PartBoundary_HasEdgesAtAllRotations_RoundedRect(double angleDeg)
    {
        var drawing = MakeRoundedRect();
        var part = new Part(drawing);
        if (angleDeg != 0)
            part.Rotate(Angle.ToRadians(angleDeg));

        var boundary = new PartBoundary(part, 0.125);

        var left = boundary.GetEdges(PushDirection.Left);
        var right = boundary.GetEdges(PushDirection.Right);
        var up = boundary.GetEdges(PushDirection.Up);
        var down = boundary.GetEdges(PushDirection.Down);

        _output.WriteLine($"Rotation: {angleDeg}°");
        _output.WriteLine($"  Left edges:  {left.Length}");
        _output.WriteLine($"  Right edges: {right.Length}");
        _output.WriteLine($"  Up edges:    {up.Length}");
        _output.WriteLine($"  Down edges:  {down.Length}");

        Assert.True(left.Length > 0, $"No left edges at {angleDeg}°");
        Assert.True(right.Length > 0, $"No right edges at {angleDeg}°");
        Assert.True(up.Length > 0, $"No up edges at {angleDeg}°");
        Assert.True(down.Length > 0, $"No down edges at {angleDeg}°");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void PartBoundary_HasEdgesAtAllRotations_SimpleRect(double angleDeg)
    {
        var drawing = MakeSimpleRect();
        var part = new Part(drawing);
        if (angleDeg != 0)
            part.Rotate(Angle.ToRadians(angleDeg));

        var boundary = new PartBoundary(part, 0.125);

        var left = boundary.GetEdges(PushDirection.Left);
        var right = boundary.GetEdges(PushDirection.Right);
        var up = boundary.GetEdges(PushDirection.Up);
        var down = boundary.GetEdges(PushDirection.Down);

        _output.WriteLine($"Rotation: {angleDeg}°");
        _output.WriteLine($"  Left edges:  {left.Length}");
        _output.WriteLine($"  Right edges: {right.Length}");
        _output.WriteLine($"  Up edges:    {up.Length}");
        _output.WriteLine($"  Down edges:  {down.Length}");

        Assert.True(left.Length > 0, $"No left edges at {angleDeg}°");
        Assert.True(right.Length > 0, $"No right edges at {angleDeg}°");
        Assert.True(up.Length > 0, $"No up edges at {angleDeg}°");
        Assert.True(down.Length > 0, $"No down edges at {angleDeg}°");
    }

    [Theory]
    [InlineData(false)]  // simple rect
    [InlineData(true)]   // rounded rect
    public void FillExtents_NoPairOverlap_At90Degrees(bool rounded)
    {
        var drawing = rounded ? MakeRoundedRect() : MakeSimpleRect();
        var workArea = new Box(0, 0, 20, 20);
        var partSpacing = 0.25;

        var filler = new FillExtents(workArea, partSpacing);
        var parts = filler.Fill(drawing, Angle.ToRadians(90));

        _output.WriteLine($"Shape: {(rounded ? "rounded rect" : "simple rect")}");
        _output.WriteLine($"Parts: {parts.Count}");

        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            _output.WriteLine($"  [{i}] rot={Angle.ToDegrees(p.Rotation):F1}° " +
                $"bbox=({p.BoundingBox.Left:F2},{p.BoundingBox.Bottom:F2})-({p.BoundingBox.Right:F2},{p.BoundingBox.Top:F2})");
        }

        // Check for overlapping bounding boxes
        for (var i = 0; i < parts.Count; i++)
        {
            var b1 = parts[i].BoundingBox;
            for (var j = i + 1; j < parts.Count; j++)
            {
                var b2 = parts[j].BoundingBox;
                var overlapX = System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                var overlapY = System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);

                if (overlapX > 0.01 && overlapY > 0.01)
                    _output.WriteLine($"  OVERLAP: [{i}] and [{j}] overlap by ({overlapX:F3}, {overlapY:F3})");

                Assert.False(overlapX > 0.01 && overlapY > 0.01,
                    $"Parts [{i}] and [{j}] have overlapping bounding boxes " +
                    $"({overlapX:F3} x {overlapY:F3})");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillLinear_PairPattern_NoPairOverlap_At90Degrees(bool rounded)
    {
        var drawing = rounded ? MakeRoundedRect() : MakeSimpleRect();
        var workArea = new Box(0, 0, 20, 20);
        var partSpacing = 0.25;

        // Build a pair at 90°/270°
        var part1 = Part.CreateAtOrigin(drawing, Angle.ToRadians(90));
        var part2 = Part.CreateAtOrigin(drawing, Angle.ToRadians(270));

        // Slide part2 right of part1
        var offset = part1.BoundingBox.Width + part2.BoundingBox.Width + partSpacing;
        part2.Offset(offset, 0);
        part2.UpdateBounds();

        // Slide part2 left toward part1 using geometry
        var b1 = new PartBoundary(part1, partSpacing / 2);
        var b2 = new PartBoundary(part2, partSpacing / 2);

        _output.WriteLine($"Part1 (90°) boundary edges: L={b1.GetEdges(PushDirection.Left).Length} R={b1.GetEdges(PushDirection.Right).Length}");
        _output.WriteLine($"Part2 (270°) boundary edges: L={b2.GetEdges(PushDirection.Left).Length} R={b2.GetEdges(PushDirection.Right).Length}");

        var movingLines = b2.GetLines(part2.Location, PushDirection.Left);
        var stationaryLines = b1.GetLines(part1.Location, PushDirection.Right);

        _output.WriteLine($"Part1 loc: ({part1.Location.X:F4},{part1.Location.Y:F4})");
        _output.WriteLine($"Part2 loc: ({part2.Location.X:F4},{part2.Location.Y:F4})");

        _output.WriteLine($"Moving lines (part2 left): {movingLines.Count}");
        foreach (var l in movingLines)
            _output.WriteLine($"  ({l.pt1.X:F4},{l.pt1.Y:F4})->({l.pt2.X:F4},{l.pt2.Y:F4})");

        _output.WriteLine($"Stationary lines (part1 right): {stationaryLines.Count}");
        foreach (var l in stationaryLines)
            _output.WriteLine($"  ({l.pt1.X:F4},{l.pt1.Y:F4})->({l.pt2.X:F4},{l.pt2.Y:F4})");

        var slideDist = SpatialQuery.DirectionalDistance(movingLines, stationaryLines, PushDirection.Left);
        _output.WriteLine($"Slide distance: {slideDist:F4}");

        if (slideDist < double.MaxValue && slideDist > 0)
        {
            part2.Offset(-slideDist, 0);
            part2.UpdateBounds();
        }

        _output.WriteLine($"Part1 bbox: ({part1.BoundingBox.Left:F2},{part1.BoundingBox.Bottom:F2})-({part1.BoundingBox.Right:F2},{part1.BoundingBox.Top:F2})");
        _output.WriteLine($"Part2 bbox: ({part2.BoundingBox.Left:F2},{part2.BoundingBox.Bottom:F2})-({part2.BoundingBox.Right:F2},{part2.BoundingBox.Top:F2})");

        // Now tile this pair pattern
        var pattern = new Pattern();
        pattern.Parts.Add(part1);
        pattern.Parts.Add(part2);
        pattern.UpdateBounds();

        _output.WriteLine($"Pattern bbox width: {pattern.BoundingBox.Width:F2}");

        var engine = new FillLinear(workArea, partSpacing);
        var parts = engine.Fill(pattern, NestDirection.Horizontal);

        _output.WriteLine($"Total parts: {parts.Count}");
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            _output.WriteLine($"  [{i}] rot={Angle.ToDegrees(p.Rotation):F1}° " +
                $"bbox=({p.BoundingBox.Left:F2},{p.BoundingBox.Bottom:F2})-({p.BoundingBox.Right:F2},{p.BoundingBox.Top:F2})");
        }

        // Check for overlaps
        for (var i = 0; i < parts.Count; i++)
        {
            var bi = parts[i].BoundingBox;
            for (var j = i + 1; j < parts.Count; j++)
            {
                var bj = parts[j].BoundingBox;
                var ox = System.Math.Min(bi.Right, bj.Right) - System.Math.Max(bi.Left, bj.Left);
                var oy = System.Math.Min(bi.Top, bj.Top) - System.Math.Max(bi.Bottom, bj.Bottom);

                Assert.False(ox > 0.01 && oy > 0.01,
                    $"Parts [{i}] and [{j}] overlap ({ox:F3} x {oy:F3})");
            }
        }
    }
}

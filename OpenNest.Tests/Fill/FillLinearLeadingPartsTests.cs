using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Fill;

/// <summary>
/// A tiling starts with its pattern on the work area's leading edge, so the copy before it
/// never fits whole; once the tiling moves into the room left past its far end, some of
/// that copy's parts can.
/// </summary>
public class FillLinearLeadingPartsTests
{
    private static Drawing Rectangle(double length, double width)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    /// <summary>
    /// A 10 x 8.5 arch: a band between a 5 radius and a 4.28 radius arc, its legs ending in
    /// 0.125 radius rounds. Turned 180 degrees it is a cup, and a cup row drops 2.1 below
    /// an arch row, each arch's legs inside two cups.
    /// </summary>
    private static Drawing Arch()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new ArcMove(new Vector(-0.1894982776, 0.1624029578),
            new Vector(-0.0899707487, 0.0867770959), RotationType.CW));
        pgm.Codes.Add(new ArcMove(new Vector(-7.0081616203, 0.1624029578),
            new Vector(-3.5988299489, 2.7529791426), RotationType.CCW));
        pgm.Codes.Add(new ArcMove(new Vector(-7.1976598979, 0),
            new Vector(-7.1076891492, 0.0867770959), RotationType.CW));
        pgm.Codes.Add(new ArcMove(new Vector(0, 0),
            new Vector(-3.5988299489, 3.4710838363), RotationType.CW));
        return new Drawing("arch", pgm);
    }

    private static Pattern PatternOf(params Part[] parts)
    {
        var pattern = new Pattern();
        foreach (var part in parts)
        {
            part.UpdateBounds();
            pattern.Parts.Add(part);
        }
        pattern.UpdateBounds();
        return pattern;
    }

    private static void AssertInside(Box area, List<Part> parts)
    {
        foreach (var part in parts)
        {
            var box = part.BoundingBox;
            Assert.True(box.Left >= area.Left - Tolerance.Epsilon && box.Right <= area.Right + Tolerance.Epsilon
                && box.Bottom >= area.Bottom - Tolerance.Epsilon && box.Top <= area.Top + Tolerance.Epsilon,
                $"part at ({box.Left:F4},{box.Bottom:F4})-({box.Right:F4},{box.Top:F4}) is outside the area");
        }
    }

    [Fact]
    public void Tiling_MovesForwardToFitAPartOfTheCopyBeforeIt()
    {
        // Two 4 x 4 squares side by side, the right one 3 higher: copies stack 4.5 apart.
        // In 20, three whole copies and the next copy's left square fit, leaving 2.5 at
        // the top; raising the tiling 1.5 fits the previous copy's right square below.
        var square = Rectangle(4, 4);
        var pattern = PatternOf(new Part(square, new Vector(0, 0)), new Part(square, new Vector(5, 3)));
        var area = new Box(0, 0, 9, 20);

        var parts = new FillLinear(area, 0.5).Fill(pattern, NestDirection.Vertical);

        Assert.Equal(8, parts.Count);
        AssertInside(area, parts);
        Assert.Equal(new[] { 1.5, 6, 10.5, 15 }, Bottoms(parts, left: true));
        Assert.Equal(new[] { 0, 4.5, 9, 13.5 }, Bottoms(parts, left: false));
    }

    [Fact]
    public void Tiling_WithoutRoomForThePreviousCopy_StaysOnTheLeadingEdge()
    {
        // The same squares in 18.5: no room past the far end, so nothing moves.
        var square = Rectangle(4, 4);
        var pattern = PatternOf(new Part(square, new Vector(0, 0)), new Part(square, new Vector(5, 3)));
        var area = new Box(0, 0, 9, 17.5);

        var parts = new FillLinear(area, 0.5).Fill(pattern, NestDirection.Vertical);

        Assert.Equal(7, parts.Count);
        Assert.Equal(new[] { 0, 4.5, 9, 13.5 }, Bottoms(parts, left: true));
        Assert.Equal(new[] { 3, 7.5, 12 }, Bottoms(parts, left: false));
    }

    [Fact]
    public void ArchesAndCups_FitTheCupRowBelowTheFirstArchRow()
    {
        // A cup resting on a row of arches tiles 10.3 along the row and 9.53 across it, but
        // the pair starts with an arch row: in 58.375 it holds five pairs and a last arch
        // row (121). The cup row of the pair before fits under it with the tiling raised
        // 2.1, making twelve rows.
        var arch = Arch();
        var cup = new Part(arch);
        cup.Rotate(System.Math.PI);
        cup.Location = new Vector(-2.047654897931384, 15.862226444770164);
        var pattern = PatternOf(new Part(arch, new Vector(0, 0)), cup);
        var area = new Box(0.3, 1.375, 119.4, 58.375);

        var parts = new FillLinear(area, 0.3).Fill(pattern, NestDirection.Horizontal);

        Assert.Equal(132, parts.Count);
        AssertInside(area, parts);
        Assert.Empty(PartOverlapChecker.FindOverlappingPairs(parts));
    }

    private static double[] Bottoms(List<Part> parts, bool left) =>
        parts.Where(p => p.BoundingBox.Left < 1 == left)
            .Select(p => System.Math.Round(p.BoundingBox.Bottom, 6))
            .OrderBy(y => y)
            .ToArray();
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// PlateView spacing expander: grows part-to-part spacing with the work area
/// and non-selected parts as hard boundaries.
/// </summary>
public class ExpanderTests
{
    private static Program Rectangle(double width = 4, double length = 4)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, length);
        program.LineTo(0, length);
        program.LineTo(0, 0);
        return program;
    }

    private static Part AddSquare(Plate plate, double x, double y, double size = 4)
    {
        var part = new Part(new Drawing($"sq{plate.Parts.Count}", Rectangle(size, size)), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Plate MakePlate(double lengthX, double widthY, double edge = 0.5)
    {
        var plate = new Plate(new Size(widthY, lengthX));
        plate.EdgeSpacing = new Spacing(edge, edge);
        return plate;
    }

    /// <summary>Independent clearance oracle: naive vertex/segment min distance over raw part lines.</summary>
    private static double BruteClearance(Part a, Part b)
    {
        var linesA = PartGeometry.GetPartLines(a);
        var linesB = PartGeometry.GetPartLines(b);

        double min = double.MaxValue;
        foreach (var la in linesA)
            foreach (var lb in linesB)
            {
                min = System.Math.Min(min, PointSegment(a, la.StartPoint, lb));
                min = System.Math.Min(min, PointSegment(a, la.EndPoint, lb));
                min = System.Math.Min(min, PointSegment(b, lb.StartPoint, la));
                min = System.Math.Min(min, PointSegment(b, lb.EndPoint, la));
            }
        return min;
    }

    private static double PointSegment(Part owner, Vector pt, Line seg)
    {
        var d = seg.EndPoint - seg.StartPoint;
        var len2 = d.DotProduct(d);
        var t = len2 <= 1e-12 ? 0 : System.Math.Clamp((pt - seg.StartPoint).DotProduct(d) / len2, 0, 1);
        return pt.DistanceTo(seg.StartPoint + d * t);
    }

    private static void AssertNoOverlaps(Plate plate)
    {
        for (var i = 0; i < plate.Parts.Count; i++)
            for (var j = i + 1; j < plate.Parts.Count; j++)
                Assert.False(
                    plate.Parts[i].Intersects(plate.Parts[j], out _),
                    $"{plate.Parts[i].BaseDrawing.Name} overlaps {plate.Parts[j].BaseDrawing.Name}"
                );
    }

    [Fact]
    public void Expand_TwoSquares_GrowUntilEdgeFloor_AndAnchorStaysPut()
    {
        var plate = MakePlate(24, 24);
        var a = AddSquare(plate, 6, 10);
        var b = AddSquare(plate, 14, 10);

        var result = Expander.Expand(new List<Part> { a, b }, plate);

        // Max gap: B flush against the right edge floor (23.5): 23.5 - 14 - 4 + gap base...
        // A stays (anchor); B slides to x=19.5 -> gap 9.5.
        Assert.Equal(6, a.Location.X, 6);
        Assert.Equal(10, a.Location.Y, 6);
        Assert.Equal(9.5, b.Location.X - (a.Location.X + 4), 1);
        Assert.True(result.AchievedSpacing >= 9.4, $"achieved {result.AchievedSpacing}");
        Assert.True(result.AchievedSpacing <= 9.6, $"achieved {result.AchievedSpacing}");
        AssertNoOverlaps(plate);
        Assert.True(b.BoundingBox.Right <= 23.5 + 1e-6);
    }

    [Fact]
    public void Expand_SandwichedBetweenWalls_ConvergesOnlyToInitialGaps_AndKeepsWalls()
    {
        var plate = MakePlate(26, 10, edge: 0.0);
        var wallL = AddSquare(plate, 0, 3);
        var wallR = AddSquare(plate, 18, 3);
        var a = AddSquare(plate, 6, 3);
        var b = AddSquare(plate, 12, 3);

        var result = Expander.Expand(new List<Part> { a, b }, plate);

        // Every gap starts at exactly 2.0; straight separation moves cannot open
        // the row (opening one gap costs another), so the run stays at ~2.0.
        Assert.Equal(0, wallL.Location.X, 6);
        Assert.Equal(18, wallR.Location.X, 6);
        Assert.True(
            result.AchievedSpacing >= 1.9 && result.AchievedSpacing <= 2.05,
            $"achieved {result.AchievedSpacing}"
        );
        AssertNoOverlaps(plate);
    }

    [Fact]
    public void Expand_OverlappingPair_SeparatesAndClearsOverlap()
    {
        var plate = MakePlate(30, 12);
        var a = AddSquare(plate, 5, 4);
        var b = AddSquare(plate, 7, 4); // 2.0 overlap in X

        var result = Expander.Expand(new List<Part> { a, b }, plate, new Expander.Options
        {
            InitialStep = 0.5,
            MaxSpacing = 3,
        });

        Assert.False(a.Intersects(b, out _));
        var gap = b.Location.X - (a.Location.X + 4);
        Assert.True(gap >= 2.99, $"gap {gap}");
        Assert.True(result.AchievedSpacing >= 2.9);
        AssertNoOverlaps(plate);
    }

    [Fact]
    public void Separate_PinnedPart_ReportsViolationsWithoutOverlap()
    {
        var plate = MakePlate(20, 20, edge: 0.0);
        var pinned = AddSquare(plate, 8, 8);
        // Walls 0.2 clear on all four sides.
        var left = AddSquare(plate, 3.8, 8);
        var right = AddSquare(plate, 12.2, 8);
        var bottom = AddSquare(plate, 8, 3.8);
        var top = AddSquare(plate, 8, 12.2);

        var (converged, positions, violations) = Expander.Separate(
            new List<Part> { pinned },
            plate,
            spacing: 1.0
        );

        Assert.False(converged);
        Assert.NotEmpty(violations);
        // The pinned part may slide into the walls but never through them.
        AssertNoOverlaps(plate);
        Assert.Equal(3.8, left.Location.X, 6);
        Assert.Equal(12.2, right.Location.X, 6);
        Assert.Equal(3.8, bottom.Location.Y, 6);
        Assert.Equal(12.2, top.Location.Y, 6);
    }

    [Fact]
    public void Expand_CancelledBeforeRun_LeavesEverythingInPlace()
    {
        var plate = MakePlate(24, 24);
        var a = AddSquare(plate, 6, 10);
        var b = AddSquare(plate, 14, 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = Expander.Expand(
            new List<Part> { a, b },
            plate,
            token: cts.Token
        );

        Assert.True(result.Cancelled);
        Assert.Equal(6, a.Location.X, 6);
        Assert.Equal(14, b.Location.X, 6);
    }

    [Fact]
    public void Expand_ThreeInRow_FirstSelectedNeverMoves_AndOracleConfirmsSpacing()
    {
        var plate = MakePlate(60, 14);
        var a = AddSquare(plate, 5, 5);
        var b = AddSquare(plate, 10, 5);
        var c = AddSquare(plate, 15, 5);

        var result = Expander.Expand(
            new List<Part> { a, b, c },
            plate,
            new Expander.Options { MaxSpacing = 8 }
        );

        Assert.Equal(5, a.Location.X, 6); // anchor: never the later index of any pair
        Assert.True(result.AchievedSpacing >= 7.9);

        // Independent oracle: every pair clears the reported spacing.
        var parts = new List<Part> { a, b, c };
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var clearance = BruteClearance(parts[i], parts[j]);
                Assert.True(
                    clearance >= result.AchievedSpacing - 0.01,
                    $"{parts[i].BaseDrawing.Name}/{parts[j].BaseDrawing.Name}: oracle {clearance} < reported {result.AchievedSpacing}"
                );
            }
        AssertNoOverlaps(plate);
    }

    private static Program RectangleWithHole(
        double width,
        double length,
        double hx,
        double hy,
        double hw,
        double hh
    )
    {
        var program = Rectangle(width, length);
        program.MoveTo(hx, hy);
        program.LineTo(hx + hw, hy);
        program.LineTo(hx + hw, hy + hh);
        program.LineTo(hx, hy + hh);
        program.LineTo(hx, hy);
        return program;
    }

    /// <summary>
    /// Hole-subtracting overlap check matching NestValidator semantics (a part in
    /// a cutout is legal). Part.Intersects is perimeter-only, so it cannot
    /// certify part-in-cutout layouts.
    /// </summary>
    private static bool MateriallyOverlaps(Part a, Part b)
    {
        var (outerA, holesA) = Rings(a);
        var (outerB, holesB) = Rings(b);
        return Collision.HasOverlap(outerA, outerB, holesA, holesB);
    }

    private static (Polygon Outer, List<Polygon> Holes) Rings(Part part)
    {
        var entities = OpenNest.Converters.ConvertProgram
            .ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();
        var profile = new ShapeProfile(entities);

        var outer = profile.Perimeter.ToPolygonWithTolerance(0.001);
        outer.Offset(part.Location);

        var holes = new List<Polygon>();
        foreach (var cutout in profile.Cutouts)
        {
            var hole = cutout.ToPolygonWithTolerance(0.001);
            hole.Offset(part.Location);
            holes.Add(hole);
        }

        return (outer, holes.Count == 0 ? null : holes);
    }

    [Fact]
    public void Expand_PartInsideCutout_KeepsLegalAndClearsHoleWalls()
    {
        var plate = MakePlate(40, 24);

        // Wall part with a 10x10 cutout; a small selected part sits inside it.
        var wall = new Part(
            new Drawing(
                "wall",
                RectangleWithHole(20, 20, 5, 5, 10, 10)
            ),
            new Vector(0, 0)
        );
        plate.Parts.Add(wall);

        var inside = new Part(new Drawing("inside", Rectangle(2, 2)), new Vector(9, 9));
        var other = new Part(new Drawing("other", Rectangle(2, 2)), new Vector(30, 9));
        plate.Parts.Add(inside);
        plate.Parts.Add(other);

        var result = Expander.Expand(
            new List<Part> { inside, other },
            plate,
            new Expander.Options { MaxSpacing = 2 }
        );

        // Part-in-cutout is legal, never a material overlap.
        Assert.False(MateriallyOverlaps(inside, wall));
        Assert.False(MateriallyOverlaps(other, wall));
        Assert.False(MateriallyOverlaps(inside, other));
        Assert.True(result.AchievedSpacing >= 1.9);

        // The part that started in the cutout must clear the hole walls too.
        var holeLeft = 5;
        var holeRight = 15;
        var gapLeft = inside.Location.X - holeLeft;
        var gapRight = holeRight - (inside.Location.X + 2);
        var gapBottom = inside.Location.Y - holeLeft;
        var gapTop = holeRight - (inside.Location.Y + 2);
        var minGap = System.Math.Min(
            System.Math.Min(gapLeft, gapRight),
            System.Math.Min(gapBottom, gapTop)
        );
        Assert.True(minGap >= 1.9, $"closest hole-wall gap {minGap}");
    }

    [Fact]
    public void Expand_DegenerateInputs_Throw()
    {
        var plate = MakePlate(10, 10);
        var a = AddSquare(plate, 1, 1);
        var stranger = new Part(new Drawing("stranger", Rectangle()), new Vector(50, 50));

        Assert.Throws<ArgumentException>(() => Expander.Expand(new List<Part>(), plate));
        Assert.Throws<ArgumentException>(() => Expander.Expand(new List<Part> { a }, plate));
        Assert.Throws<ArgumentException>(() => Expander.Expand(new List<Part> { a, stranger }, plate));
        Assert.Throws<ArgumentNullException>(() => Expander.Expand(new List<Part> { a, a }, null));
    }

    [Fact]
    public void Expand_WallsAndUnselectedPairs_StayExactlyAtClearance()
    {
        // Selection must not be pushed to open gaps between parts it excludes.
        var plate = MakePlate(40, 12);
        var w1 = AddSquare(plate, 2, 4);
        var w2 = AddSquare(plate, 6.5, 4); // 0.5 apart from w1, both unselected
        var a = AddSquare(plate, 14, 4);
        var b = AddSquare(plate, 20, 4);

        Expander.Expand(new List<Part> { a, b }, plate);

        Assert.Equal(2, w1.Location.X, 6);
        Assert.Equal(6.5, w2.Location.X, 6);
        Assert.True(a.Intersects(w1, out _) == false);
        Assert.True(b.Intersects(w2, out _) == false);
    }
}

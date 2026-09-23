using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class CollisionTests
{
    /// Two unit squares overlapping by 0.5 in X.
    /// Square A: (0,0)-(1,1), Square B: (0.5,0)-(1.5,1)
    /// Expected overlap: (0.5,0)-(1,1), area = 0.5
    [Fact]
    public void Check_OverlappingSquares_ReturnsOverlapRegion()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(0.5, 0, 1.5, 1);

        var result = Collision.Check(a, b);

        Assert.True(result.Overlaps);
        Assert.True(result.OverlapArea > 0.49 && result.OverlapArea < 0.51);
        Assert.NotEmpty(result.OverlapRegions);
    }

    /// Two squares that don't touch at all.
    [Fact]
    public void Check_NonOverlappingSquares_ReturnsNone()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(5, 5, 6, 6);

        var result = Collision.Check(a, b);

        Assert.False(result.Overlaps);
        Assert.Empty(result.OverlapRegions);
        Assert.Equal(0, result.OverlapArea);
    }

    /// Two squares sharing an edge (touching but not overlapping).
    [Fact]
    public void Check_EdgeTouchingSquares_ReturnsNone()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(1, 0, 2, 1);

        var result = Collision.Check(a, b);

        Assert.False(result.Overlaps);
    }

    /// One square fully inside another. Inner: (0.25,0.25)-(0.75,0.75), area = 0.25
    [Fact]
    public void Check_ContainedSquare_ReturnsInnerArea()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(0.25, 0.25, 0.75, 0.75);

        var result = Collision.Check(a, b);

        Assert.True(result.Overlaps);
        Assert.True(result.OverlapArea > 0.24 && result.OverlapArea < 0.26);
    }

    /// L-shaped concave polygon overlapping a square.
    [Fact]
    public void Check_ConcavePolygonOverlap_ReturnsOverlap()
    {
        // L-shape: 2x2 with a 1x1 notch cut from top-right
        var lShape = new Polygon();
        lShape.Vertices.Add(new Vector(0, 0));
        lShape.Vertices.Add(new Vector(2, 0));
        lShape.Vertices.Add(new Vector(2, 1));
        lShape.Vertices.Add(new Vector(1, 1));
        lShape.Vertices.Add(new Vector(1, 2));
        lShape.Vertices.Add(new Vector(0, 2));
        lShape.Close();
        lShape.UpdateBounds();

        // Square overlapping the notch area and bottom-right
        var square = MakeSquare(1.5, 0, 2.5, 1.5);

        var result = Collision.Check(lShape, square);

        Assert.True(result.Overlaps);
        // Overlap is 0.5 x 1.0 = 0.5 (the part of the square inside the L bottom-right)
        Assert.True(result.OverlapArea > 0.49 && result.OverlapArea < 0.51);
    }

    /// <summary>
    /// Square A has a hole. Square B overlaps only the hole area.
    /// This should NOT be a collision — B fits inside A's cutout.
    /// </summary>
    [Fact]
    public void Check_OverlapInsideHole_ReturnsNone()
    {
        var a = MakeSquare(0, 0, 4, 4);
        var holeA = new List<Polygon> { MakeSquare(1, 1, 3, 3) };

        // B fits entirely inside the hole
        var b = MakeSquare(1.5, 1.5, 2.5, 2.5);

        var result = Collision.Check(a, b, holesA: holeA);

        Assert.False(result.Overlaps);
    }

    /// <summary>
    /// Square A has a hole. Square B partially overlaps the hole and
    /// partially overlaps solid material. Should still be a collision.
    /// </summary>
    [Fact]
    public void Check_PartialOverlapWithHole_StillOverlaps()
    {
        var a = MakeSquare(0, 0, 4, 4);
        var holeA = new List<Polygon> { MakeSquare(1, 1, 3, 3) };

        // B extends beyond the hole into solid material
        var b = MakeSquare(2, 2, 5, 5);

        var result = Collision.Check(a, b, holesA: holeA);

        // Hole subtraction uses a conservative approach (keeps partial overlaps),
        // so we only verify that a collision is still detected for solid material.
        Assert.True(result.Overlaps);
    }

    /// <summary>
    /// HasOverlap with holes returns false when overlap is inside cutout.
    /// </summary>
    [Fact]
    public void HasOverlap_InsideHole_ReturnsFalse()
    {
        var a = MakeSquare(0, 0, 4, 4);
        var holeA = new List<Polygon> { MakeSquare(1, 1, 3, 3) };
        var b = MakeSquare(1.5, 1.5, 2.5, 2.5);

        Assert.False(Collision.HasOverlap(a, b, holesA: holeA));
    }

    [Fact]
    public void CheckAll_MultiplePolygons_FindsAllOverlaps()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(0.5, 0, 1.5, 1); // overlaps A
        var c = MakeSquare(5, 5, 6, 6); // overlaps nobody

        var results = Collision.CheckAll(new List<Polygon> { a, b, c });

        Assert.Single(results);
        Assert.True(results[0].Overlaps);
    }

    [Fact]
    public void CheckAll_NoOverlaps_ReturnsEmpty()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(3, 3, 4, 4);

        var results = Collision.CheckAll(new List<Polygon> { a, b });

        Assert.Empty(results);
    }

    [Fact]
    public void HasAnyOverlap_WithOverlap_ReturnsTrue()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(0.5, 0, 1.5, 1);

        Assert.True(Collision.HasAnyOverlap(new List<Polygon> { a, b }));
    }

    [Fact]
    public void HasAnyOverlap_NoOverlap_ReturnsFalse()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(3, 3, 4, 4);

        Assert.False(Collision.HasAnyOverlap(new List<Polygon> { a, b }));
    }

    [Fact]
    public void Check_IdenticalSquares_FullOverlap()
    {
        var a = MakeSquare(0, 0, 1, 1);
        var b = MakeSquare(0, 0, 1, 1);

        var result = Collision.Check(a, b);

        Assert.True(result.Overlaps);
        Assert.True(result.OverlapArea > 0.99 && result.OverlapArea < 1.01);
    }

    [Fact]
    public void HasAnyOverlap_SinglePolygon_ReturnsFalse()
    {
        var a = MakeSquare(0, 0, 1, 1);
        Assert.False(Collision.HasAnyOverlap(new List<Polygon> { a }));
    }

    [Fact]
    public void HasAnyOverlap_EmptyList_ReturnsFalse()
    {
        Assert.False(Collision.HasAnyOverlap(new List<Polygon>()));
    }

    // The cases below feed Collision with ClipperBridge offsets, the way the spacing
    // checks prepare their inputs: lines only, round joins, 1e-4 precision.

    [Theory]
    [InlineData(4.9, 5.1, true)] // Inside the collapsed slot: 0.05 from its walls.
    [InlineData(10.3, 12, false)] // Beside the part, 0.3 away.
    public void HasOverlap_NeighborOfPartWithCollapsedSlot(
        double left,
        double right,
        bool expected
    )
    {
        // 10x10 part with a 0.3-wide slot down from the top, inflated by 0.25.
        var part = MakeProfile(
            MakePolygon((0, 0), (10, 0), (10, 10), (5.15, 10), (5.15, 7), (4.85, 7), (4.85, 10), (0, 10))
        );
        var inflated = ClipperBridge.Offset(part, 0.25, 0.001);
        var neighbor = MakeSquare(left, 8, right, 10);

        Assert.Equal(
            expected,
            Collision.HasOverlap(inflated.LargestOuter(), neighbor, inflated.Holes)
        );
    }

    [Theory]
    [InlineData(5.5, 14.5, false)] // 0.5 from the hole's edges.
    [InlineData(5.1, 14.9, true)] // 0.1 from the hole's edges.
    public void HasOverlap_PartInsideHoleShrunkBySpacing(double min, double max, bool expected)
    {
        var part = MakeProfile(
            MakePolygon((0, 0), (20, 0), (20, 20), (0, 20)),
            MakePolygon((5, 5), (15, 5), (15, 15), (5, 15))
        );
        var inflated = ClipperBridge.Offset(part, 0.25, 0.001);
        var inner = MakeSquare(min, min, max, max);

        Assert.Single(inflated.Holes);
        Assert.Equal(
            expected,
            Collision.HasOverlap(inflated.LargestOuter(), inner, inflated.Holes)
        );
    }

    [Fact]
    public void HasOverlap_ZeroSpacingEdgeContact_ReturnsFalse()
    {
        var a = ClipperBridge.Offset(
            MakeProfile(MakePolygon((0, 0), (10, 0), (10, 10), (0, 10))),
            0,
            0.001
        );
        var b = ClipperBridge.Offset(
            MakeProfile(MakePolygon((10, 0), (20, 0), (20, 10), (10, 10))),
            0,
            0.001
        );

        Assert.False(Collision.HasOverlap(a.LargestOuter(), b.LargestOuter()));
    }

    private static Shape MakePolygon(params (double X, double Y)[] pts)
    {
        var shape = new Shape();

        for (var i = 0; i < pts.Length; i++)
        {
            var from = pts[i];
            var to = pts[(i + 1) % pts.Length];
            shape.Entities.Add(new Line(from.X, from.Y, to.X, to.Y));
        }

        return shape;
    }

    private static ShapeProfile MakeProfile(params Shape[] shapes) =>
        new(shapes.SelectMany(s => s.Entities).ToList());

    private static Polygon MakeSquare(double left, double bottom, double right, double top)
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(left, bottom));
        p.Vertices.Add(new Vector(right, bottom));
        p.Vertices.Add(new Vector(right, top));
        p.Vertices.Add(new Vector(left, top));
        p.Close();
        p.UpdateBounds();
        return p;
    }
}

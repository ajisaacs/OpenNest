using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Tests;

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
        var b = MakeSquare(0.5, 0, 1.5, 1);  // overlaps A
        var c = MakeSquare(5, 5, 6, 6);      // overlaps nobody

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

using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class ClearanceTests
{
    private const double Tol = 1e-9;

    private static Polygon Square(double x, double y, double w, double h)
    {
        var p = new Polygon
        {
            Vertices = new List<Vector>
            {
                new Vector(x, y),
                new Vector(x + w, y),
                new Vector(x + w, y + h),
                new Vector(x, y + h),
            }
        };
        p.Close();
        p.UpdateBounds();
        return p;
    }

    private static Polygon Triangle(params double[] xy)
    {
        var p = new Polygon();
        for (var i = 0; i + 1 < xy.Length; i += 2)
            p.Vertices.Add(new Vector(xy[i], xy[i + 1]));
        p.Close();
        p.UpdateBounds();
        return p;
    }

    // ---- Separation ----

    [Fact]
    public void Between_SeparatedHorizontally_DistanceAndDirection()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(3, 0, 1, 1);

        var r = Clearance.Between(a, b);

        Assert.Equal(2.0, r.Distance, 6);
        // Pushing a away from b means moving left.
        Assert.Equal(-1.0, r.Direction.X, 6);
        Assert.Equal(0.0, r.Direction.Y, 6);
    }

    [Fact]
    public void Between_SeparatedDiagonally_CornerDistance()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(2, 2, 1, 1);

        var r = Clearance.Between(a, b);

        Assert.Equal(System.Math.Sqrt(2.0), r.Distance, 6);
        Assert.Equal(-1 / System.Math.Sqrt(2), r.Direction.X, 6);
        Assert.Equal(-1 / System.Math.Sqrt(2), r.Direction.Y, 6);
    }

    [Fact]
    public void Between_Touching_ZeroDistance()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(1, 0, 2, 1);

        var r = Clearance.Between(a, b);

        Assert.True(System.Math.Abs(r.Distance) < 1e-6, $"expected ~0, got {r.Distance}");
        var mag = System.Math.Sqrt(
            r.Direction.X * r.Direction.X + r.Direction.Y * r.Direction.Y
        );
        Assert.Equal(1.0, mag, 6);
    }

    [Fact]
    public void Between_VertexToEdge_DistanceIsPerpendicular()
    {
        // Triangle above a wide square; the base sits 3 above the square's top edge.
        var a = Triangle(1, 3, 3, 3, 2, 4);
        var b = Square(0, -4, 10, 4); // top edge at y = 0

        var r = Clearance.Between(a, b);

        Assert.Equal(3.0, r.Distance, 6); // base y=3 to y=0
        Assert.Equal(0.0, r.Direction.X, 6);
        Assert.Equal(1.0, r.Direction.Y, 6);
    }

    [Fact]
    public void Between_ParallelStaggeredEdges_MinimumAcrossAllPairs()
    {
        // Two L-ish shapes (as simple polys) offset so the true minimum is
        // between mid-edges, not vertices.
        var a = Square(0, 0, 4, 1);
        var b = Square(1, 2, 1, 3);

        var r = Clearance.Between(a, b);

        Assert.Equal(1.0, r.Distance, 6);
        Assert.Equal(-1.0, r.Direction.Y, 6);
    }

    // ---- Penetration ----

    [Fact]
    public void Between_OverlappingSquares_MinimumTranslationAxis()
    {
        // Overlap 0.5 in X, 1.0 in Y -> cheapest exit is X.
        var a = Square(0, 0, 1, 1);
        var b = Square(0.5, 0, 1.5, 1);

        var r = Clearance.Between(a, b);

        Assert.Equal(-0.5, r.Distance, 6);
        Assert.Equal(-1.0, r.Direction.X, 6); // push a left, out of b
        Assert.Equal(0.0, r.Direction.Y, 6);
    }

    [Fact]
    public void Between_OverlappingVerticallyCheaper_ExitsInY()
    {
        // Overlap 0.8 in X, 0.2 in Y -> cheapest exit is Y.
        var a = Square(0, 0, 1, 1);
        var b = Square(0.2, 0.8, 1.2, 1.8);

        var r = Clearance.Between(a, b);

        Assert.Equal(-0.2, r.Distance, 6);
        Assert.Equal(0.0, r.Direction.X, 6);
        Assert.Equal(-1.0, r.Direction.Y, 6);
    }

    [Fact]
    public void Between_ContainedSquare_ExitsThroughNearestWall()
    {
        // Inner square near the left wall: the translation that ENDS the overlap
        // carries its right edge (x=1.2) past the outer's left edge (x=0).
        var outer = Square(0, 0, 10, 10);
        var inner = Square(0.2, 4, 1, 1);

        var r = Clearance.Between(inner, outer);

        Assert.Equal(-1.2, r.Distance, 6);
        Assert.Equal(-1.0, r.Direction.X, 6);
    }

    [Fact]
    public void Between_ConcentricSquares_DepthIsExitTranslation()
    {
        var outer = Square(0, 0, 10, 10);
        var inner = Square(2, 2, 4, 4); // spans [2,6]; leftmost exit carries 6 to 0

        var r = Clearance.Between(inner, outer);

        Assert.Equal(-6.0, r.Distance, 6);
        Assert.Equal(0.0, r.Direction.X, 6);
        Assert.Equal(-1.0, r.Direction.Y, 6);
    }

    [Fact]
    public void Between_TrianglesPenetrating_ReportsNegativeDepth()
    {
        var a = Triangle(0, 0, 4, 0, 2, 3);
        var b = Triangle(1, 0, 5, 0, 3, 3);

        var r = Clearance.Between(a, b);

        Assert.True(r.Distance < 0, $"expected penetration, got {r.Distance}");
    }

    // ---- Direction is actionable: moving a by -Distance * dir clears contact ----

    [Fact]
    public void Between_PenetrationApplyingDirection_EndsContact()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(0.3, 0, 1.6, 2);

        var r = Clearance.Between(a, b);

        var moved = (Polygon)a.Clone();
        moved.Offset(r.Direction * (-r.Distance + 0.001));
        moved.UpdateBounds();

        Assert.False(Collision.HasOverlap(moved, b));
    }

    [Fact]
    public void Between_SeparationApplyingDirection_NeverReducesDistance()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(4, 1, 2, 2);

        var r = Clearance.Between(a, b);
        Assert.True(r.Distance > 0);

        // A tiny step along the reported direction must not move closer.
        var moved = (Polygon)a.Clone();
        moved.Offset(r.Direction * (r.Distance / 2));
        moved.UpdateBounds();

        var r2 = Clearance.Between(moved, b);
        Assert.True(
            r2.Distance >= r.Distance - Tol,
            $"moving along dir reduced clearance {r.Distance} -> {r2.Distance}"
        );
    }

    // ---- Determinism ----

    [Fact]
    public void Between_RepeatedCalls_IdenticalResult()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(0.5, 0.25, 2, 1.5);

        var r1 = Clearance.Between(a, b);
        var r2 = Clearance.Between(a, b);

        Assert.Equal(r1.Distance, r2.Distance);
        Assert.Equal(r1.Direction.X, r2.Direction.X);
        Assert.Equal(r1.Direction.Y, r2.Direction.Y);
    }

    [Fact]
    public void Between_SymmetricSwap_MirrorsDirection()
    {
        var a = Square(0, 0, 1, 1);
        var b = Square(0.5, 0, 1.5, 1);

        var ab = Clearance.Between(a, b);
        var ba = Clearance.Between(b, a);

        Assert.Equal(ab.Distance, ba.Distance, 6);
        Assert.Equal(-ab.Direction.X, ba.Direction.X, 6);
        Assert.Equal(-ab.Direction.Y, ba.Direction.Y, 6);
    }

    // ---- Agreement with the Collision oracle ----

    [Fact]
    public void Between_SignMatchesCollisionVerdict()
    {
        var polygons = new List<Polygon>
        {
            Square(0, 0, 1, 1),
            Square(1, 0, 2, 1),
            Square(0.5, 0, 1.5, 1),
            Square(0.25, 0.25, 0.75, 0.75),
            Square(5, 5, 6, 6),
            Triangle(0, 0, 2, 0, 1, 2),
            Triangle(0.5, -1, 2.5, -1, 1.5, 1),
        };

        for (var i = 0; i < polygons.Count; i++)
        {
            for (var j = i + 1; j < polygons.Count; j++)
            {
                var overlaps = Collision.HasOverlap(polygons[i], polygons[j]);
                var r = Clearance.Between(polygons[i], polygons[j]);

                if (overlaps)
                {
                    Assert.True(
                        r.Distance <= Tol,
                        $"pair {i},{j}: Collision overlaps but clearance {r.Distance}"
                    );
                }
                else
                {
                    Assert.True(
                        r.Distance >= -Tol,
                        $"pair {i},{j}: Collision clear but clearance {r.Distance}"
                    );
                }
            }
        }
    }
}

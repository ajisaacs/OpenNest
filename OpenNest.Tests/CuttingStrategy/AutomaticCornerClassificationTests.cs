using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>
/// The shared classification query for automatic start-point planning reuses the emitter's
/// own corner geometry and winding derivation: convex, reflex, smooth and cusp vertices
/// classify identically from either adjacent entity, in both windings, and rotations of the
/// shape do not change the kind. Midpoints are not corners.
/// </summary>
public class AutomaticCornerClassificationTests
{
    private const double ToleranceDegrees = 1e-6;

    // --- fixtures -------------------------------------------------------------

    private static Shape ClosedShape(params Entity[] entities)
    {
        var shape = new Shape();
        shape.Entities.AddRange(entities);
        Assert.True(shape.IsClosed());
        return shape;
    }

    private static Shape Square(Vector[] corners)
    {
        var e = new Entity[corners.Length];
        for (var i = 0; i < corners.Length; i++)
            e[i] = new Line(corners[i], corners[(i + 1) % corners.Length]);
        return ClosedShape(e);
    }

    /// <summary>CCW square; the shape's own winding derivation says so.</summary>
    private static Shape CcwSquare(double size = 10) =>
        Square(new[]
        {
            new Vector(0, 0), new Vector(size, 0), new Vector(size, size), new Vector(0, size),
        });

    /// <summary>Same square traversed CW.</summary>
    private static Shape CwSquare(double size = 10)
    {
        var s = CcwSquare(size);
        s.Reverse();
        return s;
    }

    /// <summary>CCW L-shape: the concave vertex (5,5) turns right — reflex.</summary>
    private static Shape Notched()
    {
        return Square(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(5, 10),
            new Vector(5, 5), new Vector(0, 5),
        });
    }

    /// <summary>CCW square whose top side is a half-circle bump; the tangent joints are at (0,10) and (10,10).</summary>
    private static Shape ArcBumpSquare()
    {
        return ClosedShape(
            new Line(new Vector(0, 0), new Vector(10, 0)),
            new Line(new Vector(10, 0), new Vector(10, 10)),
            new Arc(new Vector(5, 10), 5, 0, System.Math.PI),
            new Line(new Vector(0, 10), new Vector(0, 0)));
    }

    // --- positive cases --------------------------------------------------------

    public static IEnumerable<object[]> CcwSquareCorners()
    {
        var corners = new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10),
        };
        for (var i = 0; i < corners.Length; i++)
        {
            yield return new object[] { corners[i], i };
            yield return new object[] { corners[i], (i + corners.Length - 1) % corners.Length };
        }
    }

    [Theory]
    [MemberData(nameof(CcwSquareCorners))]
    public void SquareCorners_AreConvexFromEitherEdge(Vector corner, int entityIndex)
    {
        var shape = CcwSquare();

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            shape, corner, shape.Entities[entityIndex], out var found));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Convex, found.Kind);
    }

    [Theory]
    [MemberData(nameof(CcwSquareCorners))]
    public void TraversedBackwards_SquareCornersStayConvex(Vector corner, int entityIndex)
    {
        var shape = CwSquare();
        // Same geometric corners; entity order is reversed, so look the vertex up by position
        // and alternate between the two entities adjacent to it.
        var (entity, point) = AdjacentToVertex(shape, corner, entityIndex);

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            shape, point, entity, out var found));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Convex, found.Kind);
    }

    [Fact]
    public void NotchVertex_IsReflexFromEitherEdge()
    {
        var shape = Notched();
        var notch = new Vector(5, 5);
        var incoming = shape.Entities.Single(e => EndOf(e) == notch);
        var outgoing = shape.Entities.Single(e => StartOf(e) == notch);

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, notch, incoming, out var a));
        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, notch, outgoing, out var b));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Reflex, a.Kind);
        Assert.Equal(a.Kind, b.Kind);
        Assert.Equal(ContourCuttingStrategy.CornerKind.Reflex,
            ClassifyAtVertex(CwShaped(Notched()), notch));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 10)]
    public void TangentLineArcJoints_AreSmoothNotCorners(double x, double y)
    {
        var shape = ArcBumpSquare();
        var point = new Vector(x, y);
        var incoming = shape.Entities.Single(e => EndOf(e) == point);
        var outgoing = shape.Entities.Single(e => StartOf(e) == point);

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, point, incoming, out var a));
        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, point, outgoing, out var b));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Smooth, a.Kind);
        Assert.Equal(b.Kind, a.Kind);
    }

    [Fact]
    public void ReversalVertex_IsCusp()
    {
        // The emitter's own rule: equal-and-opposite travel tangents (turn ≈ 0, dot < 0).
        var shape = Square(new[]
        {
            new Vector(0, 0), new Vector(5, 0), new Vector(0, 0), new Vector(0, 10), new Vector(10, 10),
            new Vector(10, 0),
        });
        var cusp = new Vector(0, 0);
        var entity = shape.Entities.First(e => StartOf(e) == cusp);

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, cusp, entity, out var found));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Cusp, found.Kind);
    }

    // --- invariants -------------------------------------------------------------

    [Theory]
    [InlineData(0.37)]
    [InlineData(1.9)]
    [InlineData(4.71)]
    public void RotatingTheShape_DoesNotChangeTheKind(double angle)
    {
        var shape = Notched();
        var notch = new Vector(5, 5);
        var entity = shape.Entities.Single(e => StartOf(e) == notch);
        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, notch, entity, out var before));

        shape.Rotate(angle);
        var (rotatedEntity, rotatedPoint) = NearestVertex(shape, notch.Rotate(angle));

        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            shape, rotatedPoint, rotatedEntity, out var after));
        Assert.Equal(before.Kind, after.Kind);
    }

    [Fact]
    public void Midpoint_IsNotACorner()
    {
        var shape = CcwSquare();
        var edge = Assert.IsType<Line>(shape.Entities[1]);

        Assert.False(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            shape, edge.MidPoint, edge, out _));
    }

    [Fact]
    public void OpenContour_HasNoClassifiableCorners()
    {
        var shape = CcwSquare();
        shape.Entities.RemoveAt(2);
        Assert.False(shape.IsClosed());

        Assert.False(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            shape, new Vector(10, 10), shape.Entities[1], out _));
    }

    [Fact]
    public void KindMatchesTheExistingEmitterClassification()
    {
        // Cross-check against the lead-in path actually used by emission: an outside
        // square corner is where ResolveLeadIn extends the outgoing edge (convex), and
        // the notch is where the internal-style lead bisects (reflex). The wrapper must
        // agree with what EmitContour/ResolveLeadIn already do, not invent a third rule.
        var square = CcwSquare();
        var corner = new Vector(10, 0);
        var outgoing = square.Entities.Single(e => StartOf(e) == corner);
        Assert.True(ContourCuttingStrategy.TryClassifyAutomaticStartCorner(
            square, corner, outgoing, out var kind));
        Assert.Equal(ContourCuttingStrategy.CornerKind.Convex, kind.Kind);

        // Tangents are travel directions of the two joined edges.
        Assert.Equal(1.0, kind.TangentIn.X, 9);
        Assert.Equal(0.0, kind.TangentIn.Y, 9);
        Assert.Equal(0.0, kind.TangentOut.X, 9);
        Assert.Equal(1.0, kind.TangentOut.Y, 9);
    }

    // --- helpers ----------------------------------------------------------------

    private static Shape CwShaped(Shape shape)
    {
        shape.Reverse();
        return shape;
    }

    private static ContourCuttingStrategy.CornerKind? ClassifyAtVertex(Shape shape, Vector vertex)
    {
        var (entity, point) = NearestVertex(shape, vertex);
        return ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, point, entity, out var found)
            ? found.Kind
            : null;
    }

    private static (Entity Entity, Vector Point) AdjacentToVertex(Shape shape, Vector vertex, int entityIndex)
    {
        // The two entities of the reversed contour that touch this geometric vertex;
        // entityIndex alternates between them so both edge selections are covered.
        var adjacent = shape.Entities
            .Where(e => StartOf(e).DistanceTo(vertex) <= 1e-9 || EndOf(e).DistanceTo(vertex) <= 1e-9)
            .ToList();
        Assert.Equal(2, adjacent.Count);
        return (adjacent[entityIndex % 2], vertex);
    }

    private static (Entity Entity, Vector Point) NearestVertex(Shape shape, Vector approximate)
    {
        Entity? best = null;
        var bestPoint = Vector.Zero;
        var bestDistance = double.MaxValue;
        foreach (var entity in shape.Entities)
        {
            foreach (var point in new[] { StartOf(entity), EndOf(entity) })
            {
                var d = point.DistanceTo(approximate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = entity;
                    bestPoint = point;
                }
            }
        }
        Assert.NotNull(best);
        return (best!, bestPoint);
    }

    private static Vector StartOf(Entity entity) => entity switch
    {
        Line line => line.StartPoint,
        Arc arc => arc.StartPoint(),
        _ => throw new NotSupportedException(),
    };

    private static Vector EndOf(Entity entity) => entity switch
    {
        Line line => line.EndPoint,
        Arc arc => arc.EndPoint(),
        _ => throw new NotSupportedException(),
    };
}

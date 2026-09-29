using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Diagnostics;

/// <summary>Request-local, validated single-outer material. Never exposed to report consumers.</summary>
internal sealed record OverlapMaterial(Polygon Outer, List<Polygon> Holes)
{
    internal static OverlapMaterial Read(List<Entity> entities, CancellationToken cancellationToken)
    {
        // ShapeBuilder can reverse entities while chaining. Own a fresh copy for each analysis.
        var shapes = ShapeBuilder.GetShapes(entities.Select(entity => entity.Clone()));
        if (shapes.Count == 0)
            throw new ArgumentException("Drawing has no closed material contour.");
        var polygons = new List<Polygon>();
        foreach (var shape in shapes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateChain(shape);
            var polygon = shape.ToPolygonWithTolerance(PlateOverlapAnalyzer.ChordTolerance);
            // The analytic chain was validated above. Normalize its sampled seam (e.g.
            // sin(2*pi) is not exactly zero), rather than adding a spurious microscopic edge.
            if (polygon.IsClosed())
                polygon.Vertices[^1] = polygon.Vertices[0];
            ValidatePolygon(polygon, cancellationToken);
            polygons.Add(polygon);
        }
        // Sampling can hide a crossing or tangency between curves. Reject native
        // contour contact before asking the polygon approximation about containment.
        for (var i = 0; i < shapes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j < i; j++)
            {
                shapes[i].Intersects(shapes[j], out var intersections);
                if (intersections.Count > 0)
                    throw new ArgumentException("Native material contours cross or touch.");
            }
        }
        var ordered = polygons.OrderByDescending(polygon => Area(polygon.Vertices)).ToList();
        var outer = ordered[0];
        var holes = ordered.Skip(1).ToList();
        for (var i = 0; i < holes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BoundariesTouch(outer, holes[i], cancellationToken)
                || !Inside(outer, holes[i].Vertices[0]))
                throw new ArgumentException("Contours must have one outer with strictly internal holes.");
            for (var j = 0; j < i; j++)
            {
                if (BoundariesTouch(holes[i], holes[j], cancellationToken)
                    || Inside(holes[i], holes[j].Vertices[0])
                    || Inside(holes[j], holes[i].Vertices[0]))
                    throw new ArgumentException("Intersecting holes and nested material islands are unsupported.");
            }
        }
        return new OverlapMaterial(outer, holes);
    }

    internal OverlapMaterial Transform(double rotation, Vector offset) =>
        new(TransformPolygon(Outer, rotation, offset),
            Holes.Select(hole => TransformPolygon(hole, rotation, offset)).ToList());

    private static Polygon TransformPolygon(Polygon polygon, double rotation, Vector offset)
    {
        var transformed = new Polygon();
        transformed.Vertices.AddRange(polygon.Vertices.Select(point =>
            (rotation == 0 ? point : point.Rotate(rotation)) + offset));
        if (transformed.Vertices.Any(point => !IsFinite(point)))
            throw new ArithmeticException("Transformed contour has nonfinite coordinates.");
        transformed.UpdateBounds();
        if (!double.IsFinite(transformed.BoundingBox.Length)
            || !double.IsFinite(transformed.BoundingBox.Width))
            throw new ArithmeticException("Transformed contour bounds overflowed.");
        var sourceArea = Area(polygon.Vertices);
        var transformedArea = Area(transformed.Vertices);
        if (!double.IsFinite(transformedArea) || transformedArea <= Tolerance.Epsilon
            || System.Math.Abs(sourceArea - transformedArea)
                > System.Math.Max(Tolerance.Epsilon, sourceArea * 1e-8))
            throw new ArithmeticException("Coordinate precision cannot preserve the contour area at this pose.");
        for (var i = 0; i + 1 < transformed.Vertices.Count; i++)
        {
            var a = transformed.Vertices[i];
            var b = transformed.Vertices[i + 1];
            if (a.X == b.X && a.Y == b.Y)
                throw new ArithmeticException("Coordinate precision collapsed a contour edge at this pose.");
        }
        return transformed;
    }

    internal static bool IsFinite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    /// <summary>Translation-stable unsigned shoelace area; accepts an explicit closing vertex.</summary>
    internal static double Area(IReadOnlyList<Vector> vertices)
    {
        var twiceArea = 0.0;
        for (var i = 1; i + 1 < vertices.Count; i++)
            twiceArea += Cross(vertices[0], vertices[i], vertices[i + 1]);
        return System.Math.Abs(twiceArea) * 0.5;
    }

    private static void ValidateChain(Shape shape)
    {
        if (!shape.IsClosed())
            throw new ArgumentException("Material contour is open.");
        foreach (var entity in shape.Entities)
        {
            if (!double.IsFinite(entity.Length) || entity.Length <= 0)
                throw new ArgumentException("Material contour has a nonfinite or zero-length edge.");
        }
        if (shape.Entities.Count == 1 && shape.Entities[0] is Circle circle)
        {
            if (!IsFinite(circle.Center) || !double.IsFinite(circle.Radius) || circle.Radius <= 0)
                throw new ArgumentException("Material circle is invalid.");
            return;
        }
        for (var i = 0; i < shape.Entities.Count; i++)
        {
            var end = Endpoints(shape.Entities[i]).End;
            var start = Endpoints(shape.Entities[(i + 1) % shape.Entities.Count]).Start;
            // Do not let ShapeBuilder's larger chain tolerance silently repair a broken cut.
            if (!IsFinite(start) || !IsFinite(end) || end.DistanceTo(start) > Tolerance.Epsilon)
                throw new ArgumentException("Material contour has a gap or invalid endpoint.");
        }
    }

    private static (Vector Start, Vector End) Endpoints(Entity entity) => entity switch
    {
        Line line => (line.StartPoint, line.EndPoint),
        Arc arc => (arc.StartPoint(), arc.EndPoint()),
        _ => throw new ArgumentException("Unsupported material entity."),
    };

    private static void ValidatePolygon(Polygon polygon, CancellationToken cancellationToken)
    {
        var vertices = polygon.Vertices;
        var area = Area(vertices);
        if (vertices.Count < 4 || vertices.Any(point => !IsFinite(point))
            || !double.IsFinite(area) || area <= Tolerance.Epsilon)
            throw new ArgumentException("Material contour is degenerate or nonfinite.");
        var count = vertices.Count - 1;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = vertices[(i + count - 1) % count];
            var current = vertices[i];
            var next = vertices[i + 1];
            if (current.X == next.X && current.Y == next.Y)
                throw new ArgumentException("Material polygon has a zero-length edge.");
            if (Cross(previous, current, next) == 0
                && (previous.X - current.X) * (next.X - current.X)
                    + (previous.Y - current.Y) * (next.Y - current.Y) > 0)
                throw new ArgumentException("Material polygon has a retraced edge.");
            for (var j = i + 2; j < count; j++)
            {
                if (i == 0 && j == count - 1)
                    continue;
                if (SegmentsTouch(vertices[i], vertices[i + 1], vertices[j], vertices[j + 1]))
                    throw new ArgumentException("Material contour self-intersects or touches itself.");
            }
        }
        // Ear clipping can stop early on unusable geometry. Do not certify that as clear.
        var local = TransformPolygon(polygon, 0, vertices[0] * -1);
        var triangulatedArea = Collision.Triangulate(local).Sum(triangle => Area(triangle.Vertices));
        if (!double.IsFinite(triangulatedArea)
            || System.Math.Abs(triangulatedArea - area) > System.Math.Max(Tolerance.Epsilon, area * 1e-9))
            throw new ArgumentException("Material contour could not be completely triangulated.");
    }

    private static bool BoundariesTouch(Polygon a, Polygon b, CancellationToken cancellationToken)
    {
        for (var i = 0; i + 1 < a.Vertices.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j + 1 < b.Vertices.Count; j++)
                if (SegmentsTouch(a.Vertices[i], a.Vertices[i + 1], b.Vertices[j], b.Vertices[j + 1]))
                    return true;
        }
        return false;
    }

    private static bool SegmentsTouch(Vector a, Vector b, Vector c, Vector d)
    {
        var ac = Cross(a, b, c);
        var ad = Cross(a, b, d);
        var ca = Cross(c, d, a);
        var cb = Cross(c, d, b);
        return ac == 0 && OnSegment(a, b, c) || ad == 0 && OnSegment(a, b, d)
            || ca == 0 && OnSegment(c, d, a) || cb == 0 && OnSegment(c, d, b)
            || (ac < 0 && ad > 0 || ac > 0 && ad < 0)
                && (ca < 0 && cb > 0 || ca > 0 && cb < 0);
    }

    private static bool OnSegment(Vector a, Vector b, Vector point) =>
        point.X >= System.Math.Min(a.X, b.X) && point.X <= System.Math.Max(a.X, b.X)
        && point.Y >= System.Math.Min(a.Y, b.Y) && point.Y <= System.Math.Max(a.Y, b.Y);

    // Boundary contact is rejected before this winding-number test is used for topology.
    private static bool Inside(Polygon polygon, Vector point)
    {
        var winding = 0;
        for (var i = 0; i + 1 < polygon.Vertices.Count; i++)
        {
            var a = polygon.Vertices[i];
            var b = polygon.Vertices[i + 1];
            if (a.Y <= point.Y && b.Y > point.Y && Cross(a, b, point) > 0)
                winding++;
            else if (a.Y > point.Y && b.Y <= point.Y && Cross(a, b, point) < 0)
                winding--;
        }
        return winding != 0;
    }

    private static double Cross(Vector a, Vector b, Vector point) =>
        (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
}

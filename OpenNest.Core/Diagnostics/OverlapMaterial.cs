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
        // A contact lies within both entities' bounds, so shapes whose bounds are clear by more
        // than any native query's allowance cannot touch: a part with thousands of holes does
        // not query every pair.
        var boxes = shapes.Select(shape => shape.BoundingBox).ToList();
        for (var i = 0; i < shapes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j < i; j++)
            {
                if (ClearOfNativeQueries(boxes[i], boxes[j]))
                    continue;
                shapes[i].Intersects(shapes[j], out var intersections);
                if (intersections.Count > 0)
                    throw new ArgumentException("Native material contours cross or touch.");
            }
        }
        var ordered = polygons.OrderByDescending(polygon => Area(polygon.Vertices)).ToList();
        var outer = ordered[0];
        var holes = ordered.Skip(1).ToList();
        var outerBounds = BoundsOf(outer);
        var holeBounds = holes.Select(BoundsOf).ToList();
        for (var i = 0; i < holes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BoundariesTouch(outer, outerBounds, holes[i], holeBounds[i], cancellationToken)
                || !Inside(outer, outerBounds, holes[i].Vertices[0]))
                throw new ArgumentException("Contours must have one outer with strictly internal holes.");
            for (var j = 0; j < i; j++)
            {
                // Exact polygon tests: holes whose vertex bounds are apart can neither touch
                // nor contain each other's vertices.
                if (!Overlap(holeBounds[i], holeBounds[j]))
                    continue;
                if (BoundariesTouch(holes[i], holeBounds[i], holes[j], holeBounds[j], cancellationToken)
                    || Inside(holes[i], holeBounds[i], holes[j].Vertices[0])
                    || Inside(holes[j], holeBounds[j], holes[i].Vertices[0]))
                    throw new ArgumentException("Intersecting holes and nested material islands are unsupported.");
            }
        }
        return new OverlapMaterial(outer, holes);
    }

    /// <summary>
    /// The gap beyond which no native intersection query counts two entities as touching: ten
    /// times the 0.00001 bounding-box allowance of native intersections, as the planner's
    /// <c>PostVerificationGeometry.Extent.ClearMargin</c>.
    /// </summary>
    private const double NativeClearMargin = 1e-3;

    // True only when both boxes are finite and apart by more than the native allowance on some axis.
    private static bool ClearOfNativeQueries(Box a, Box b) =>
        IsFinite(a) && IsFinite(b)
        && (a.Right + NativeClearMargin < b.Left || b.Right + NativeClearMargin < a.Left
            || a.Top + NativeClearMargin < b.Bottom || b.Top + NativeClearMargin < a.Bottom);

    private static bool IsFinite(Box box) => double.IsFinite(box.Left) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && double.IsFinite(box.Top);

    /// <summary>Exact vertex bounds of a sampled polygon, for skipping segment and winding tests.</summary>
    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Contains(Vector point) =>
            point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
    }

    private static Bounds BoundsOf(Polygon polygon)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var vertex in polygon.Vertices)
        {
            minX = System.Math.Min(minX, vertex.X);
            minY = System.Math.Min(minY, vertex.Y);
            maxX = System.Math.Max(maxX, vertex.X);
            maxY = System.Math.Max(maxY, vertex.Y);
        }
        return new(minX, minY, maxX, maxY);
    }

    // Closed-interval overlap: two segments that touch share a point inside both bounds.
    private static bool Overlap(Bounds a, Bounds b) =>
        a.MaxX >= b.MinX && b.MaxX >= a.MinX && a.MaxY >= b.MinY && b.MaxY >= a.MinY;

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

    // Exact: a segment of a whose bounds are clear of b's bounds touches no segment of b.
    private static bool BoundariesTouch(Polygon a, Bounds aBounds, Polygon b, Bounds bBounds,
        CancellationToken cancellationToken)
    {
        if (!Overlap(aBounds, bBounds))
            return false;
        for (var i = 0; i + 1 < a.Vertices.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = a.Vertices[i];
            var end = a.Vertices[i + 1];
            var segment = new Bounds(System.Math.Min(start.X, end.X), System.Math.Min(start.Y, end.Y),
                System.Math.Max(start.X, end.X), System.Math.Max(start.Y, end.Y));
            if (!Overlap(segment, bBounds))
                continue;
            for (var j = 0; j + 1 < b.Vertices.Count; j++)
                if (SegmentsTouch(start, end, b.Vertices[j], b.Vertices[j + 1]))
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
    // A point outside the polygon's vertex bounds has winding zero.
    private static bool Inside(Polygon polygon, Bounds bounds, Vector point) =>
        bounds.Contains(point) && Inside(polygon, point);

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

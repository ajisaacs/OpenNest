using System;
using System.Collections.Generic;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest;

/// <summary>Validates a trial against immutable job geometry before the runner commits accounting.</summary>
internal static class NestJobPlacementValidator
{
    private const double Epsilon = 0.0000001;

    internal static void ValidateCandidate(PlateCandidate candidate, NestPlateStock stock,
        IReadOnlyDictionary<string, int> remaining, IReadOnlyDictionary<string, NestJobPart> parts)
    {
        if (candidate == null) throw new InvalidOperationException("The plate nester returned a null candidate.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var placed = new List<ShapeTopology>();
        foreach (var placement in candidate.Placements)
        {
            if (placement.PartId == null || !remaining.TryGetValue(placement.PartId, out var available) ||
                !parts.TryGetValue(placement.PartId, out var part))
                throw new InvalidOperationException("Candidate references an unknown requirement ID.");
            if (!double.IsFinite(placement.X) || !double.IsFinite(placement.Y) || !double.IsFinite(placement.Rotation))
                throw new InvalidOperationException("Candidate poses must be finite.");
            counts.TryGetValue(placement.PartId, out var count);
            if (count >= available) throw new InvalidOperationException("Candidate overproduces a requirement.");
            if (!RotationIsAllowed(part.Rotation, placement.Rotation))
                throw new InvalidOperationException("Candidate rotation is not allowed for the requirement.");

            var shape = Transform(CreateShape(part.Geometry), placement);
            if (!FitsWorkArea(shape, stock))
                throw new InvalidOperationException("Candidate placement falls outside the usable stock area.");
            foreach (var other in placed)
            {
                if (Overlaps(shape, other))
                    throw new InvalidOperationException("Candidate placements overlap.");
                if (stock.PartSpacing > 0 && Distance(shape, other) < stock.PartSpacing - Epsilon)
                    throw new InvalidOperationException("Candidate placements violate required part spacing.");
            }

            placed.Add(shape);
            counts[placement.PartId] = count + 1;
        }
    }

    internal static void ValidateGeometry(PartGeometrySnapshot geometry)
    {
        _ = CreateShape(geometry);
    }

    private static bool RotationIsAllowed(RotationPolicy policy, double rotation)
    {
        if (policy.Kind == RotationPolicyKind.Automatic) return true;
        if (policy.Kind == RotationPolicyKind.Fixed)
            return AnglesEqual(rotation, policy.Start);
        if (rotation < policy.Start - Epsilon || rotation > policy.End + Epsilon) return false;
        var steps = (rotation - policy.Start) / policy.Step;
        return System.Math.Abs(steps - System.Math.Round(steps)) <= Epsilon;
    }

    private static bool AnglesEqual(double left, double right)
    {
        var delta = (left - right) % (System.Math.PI * 2);
        return System.Math.Abs(delta) <= Epsilon || System.Math.Abs(System.Math.Abs(delta) - System.Math.PI * 2) <= Epsilon;
    }

    private static ShapeTopology CreateShape(PartGeometrySnapshot geometry)
    {
        var entities = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(geometry));
        var cutEntities = new List<Entity>();
        foreach (var entity in entities)
            if (!ReferenceEquals(entity.Layer, SpecialLayers.Rapid))
                cutEntities.Add(entity);

        var contours = ShapeBuilder.GetShapes(cutEntities);
        if (contours.Count == 0) throw new ArgumentException("Geometry must contain a closed contour.");
        foreach (var contour in contours)
            ValidateContour(contour);

        var profile = new ShapeProfile(cutEntities);
        profile.NormalizeWinding();
        return new ShapeTopology(profile.Perimeter, profile.Cutouts);
    }

    private static void ValidateContour(Shape contour)
    {
        if (!contour.IsClosed())
            throw new ArgumentException("Geometry must contain closed contours with usable edges.");
        foreach (var entity in contour.Entities)
            if (entity.Length <= Epsilon)
                throw new ArgumentException("Geometry contains a zero-length edge.");
        if (contour.Area() <= Epsilon)
            throw new ArgumentException("Geometry must contain non-degenerate contours.");
    }

    private static ShapeTopology Transform(ShapeTopology source, NestJobPlacement placement)
    {
        var perimeter = TransformContour(source.Perimeter, placement);
        var cutouts = new List<Shape>(source.Cutouts.Count);
        foreach (var cutout in source.Cutouts)
            cutouts.Add(TransformContour(cutout, placement));
        return new ShapeTopology(perimeter, cutouts);
    }

    private static Shape TransformContour(Shape source, NestJobPlacement placement)
    {
        var contour = (Shape)source.Clone();
        contour.Rotate(placement.Rotation);
        contour.Offset(placement.X, placement.Y);
        return contour;
    }

    private static bool FitsWorkArea(ShapeTopology shape, NestPlateStock stock)
    {
        var workArea = WorkArea(stock);
        if (!FitsWorkArea(shape.Perimeter, workArea)) return false;
        foreach (var cutout in shape.Cutouts)
            if (!FitsWorkArea(cutout, workArea)) return false;
        return true;
    }

    private static Box WorkArea(NestPlateStock stock)
    {
        var left = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var bottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        return new Box(left + stock.EdgeSpacing.Left, bottom + stock.EdgeSpacing.Bottom,
            stock.Size.Length - stock.EdgeSpacing.Left - stock.EdgeSpacing.Right,
            stock.Size.Width - stock.EdgeSpacing.Bottom - stock.EdgeSpacing.Top);
    }

    private static bool FitsWorkArea(Shape contour, Box workArea)
    {
        var bounds = contour.BoundingBox;
        return bounds.Left >= workArea.Left - Epsilon && bounds.Right <= workArea.Right + Epsilon &&
            bounds.Bottom >= workArea.Bottom - Epsilon && bounds.Top <= workArea.Top + Epsilon;
    }

    private static bool Overlaps(ShapeTopology left, ShapeTopology right)
    {
        var leftPoly = ToPolygon(left.Perimeter);
        var rightPoly = ToPolygon(right.Perimeter);
        if (!leftPoly.BoundingBox.Intersects(rightPoly.BoundingBox))
            return false;
        // True material overlap requires shared interior area, not boundary touching.
        // Edge/corner contact (zero clearance) is a valid placement when part spacing is zero.
        return InteriorOverlap(leftPoly, left, rightPoly, right);
    }

    private static bool InteriorOverlap(Polygon leftPoly, ShapeTopology left, Polygon rightPoly, ShapeTopology right)
    {
        // The intersection of two polygons is either empty, a region of positive area (true overlap),
        // or a zero-area line/point (boundary contact). Test the interior of the intersection region:
        // a point strictly inside BOTH perimeters and outside both parts' holes proves shared material.
        foreach (var point in InteriorWitnessPoints(leftPoly, rightPoly))
        {
            if (StrictlyInside(leftPoly, point) && !InAnyHole(left, point) &&
                StrictlyInside(rightPoly, point) && !InAnyHole(right, point))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Points that lie in the interior of the perimeter-perimeter intersection when one exists.
    /// For each pair of crossing edges, the two interior-side vertices (one from each polygon)
    /// have their midpoint inside both perimeters; that midpoint is a witness of positive-area
    /// overlap. For containment, an interior vertex of the inner perimeter witnesses it.
    /// </summary>
    private static IEnumerable<Vector> InteriorWitnessPoints(Polygon left, Polygon right)
    {
        foreach (var l in left.ToLines())
            foreach (var r in right.ToLines())
                if (l.Intersects(r, out var pt) && pt.IsValid())
                {
                    yield return Midpoint(l, pt);
                    yield return Midpoint(r, pt);
                }
        // Containment: an interior point of one polygon inside the other. Use a point pulled
        // toward the centroid of each polygon from a vertex (guaranteed interior for simple shapes).
        foreach (var poly in new[] { left, right })
        {
            foreach (var vertex in poly.Vertices)
            {
                var centroid = Centroid(poly);
                yield return (vertex + centroid) * 0.5;
            }
        }
    }

    private static Vector Midpoint(Line line, Vector point)
    {
        var other = line.StartPoint.DistanceTo(point) <= line.EndPoint.DistanceTo(point)
            ? line.EndPoint
            : line.StartPoint;
        return (other + point) * 0.5;
    }

    private static Vector Centroid(Polygon polygon)
    {
        var n = polygon.IsClosed() ? polygon.Vertices.Count - 1 : polygon.Vertices.Count;
        var sum = Vector.Zero;
        for (var i = 0; i < n; i++)
            sum += polygon.Vertices[i];
        return sum / n;
    }

    /// <summary>
    /// Winding-number point-in-polygon. Returns false for points on an edge or vertex.
    /// </summary>
    private static bool StrictlyInside(Polygon polygon, Vector point)
    {
        var n = polygon.IsClosed() ? polygon.Vertices.Count - 1 : polygon.Vertices.Count;
        if (n < 3) return false;
        var winding = 0;
        for (var i = 0; i < n; i++)
        {
            var p1 = polygon.Vertices[i];
            var p2 = polygon.Vertices[(i + 1) % n];
            if (OnSegment(p1, p2, point)) return false;
            if (p1.Y <= point.Y)
            {
                if (p2.Y > point.Y && IsLeft(p1, p2, point) > 0)
                    winding++;
            }
            else if (p2.Y <= point.Y && IsLeft(p1, p2, point) < 0)
            {
                winding--;
            }
        }
        return winding != 0;
    }

    private static bool OnSegment(Vector a, Vector b, Vector p)
    {
        var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        if (!cross.IsEqualTo(0.0)) return false;
        return System.Math.Min(a.X, b.X) - Epsilon <= p.X && p.X <= System.Math.Max(a.X, b.X) + Epsilon &&
               System.Math.Min(a.Y, b.Y) - Epsilon <= p.Y && p.Y <= System.Math.Max(a.Y, b.Y) + Epsilon;
    }

    private static double IsLeft(Vector p1, Vector p2, Vector p) =>
        (p2.X - p1.X) * (p.Y - p1.Y) - (p2.Y - p1.Y) * (p.X - p1.X);

    private static bool InAnyHole(ShapeTopology topology, Vector point)
    {
        foreach (var cutout in topology.Cutouts)
            if (ToPolygon(cutout).ContainsPoint(point))
                return true;
        return false;
    }

    private static double Distance(ShapeTopology left, ShapeTopology right)
    {
        var result = double.PositiveInfinity;
        foreach (var leftContour in AllContours(left))
            foreach (var rightContour in AllContours(right))
                result = System.Math.Min(result, BoundaryDistance(ToPolygon(leftContour), ToPolygon(rightContour)));
        return result;
    }

    private static IEnumerable<Shape> AllContours(ShapeTopology shape)
    {
        yield return shape.Perimeter;
        foreach (var cutout in shape.Cutouts)
            yield return cutout;
    }

    private static List<Polygon> ToPolygons(List<Shape> contours)
    {
        var polygons = new List<Polygon>(contours.Count);
        foreach (var contour in contours)
            polygons.Add(ToPolygon(contour));
        return polygons;
    }

    private static Polygon ToPolygon(Shape contour)
    {
        var polygon = contour.ToPolygon();
        polygon.UpdateBounds();
        return polygon;
    }

    private static double BoundaryDistance(Polygon left, Polygon right)
    {
        var result = double.PositiveInfinity;
        foreach (var leftLine in left.ToLines())
        {
            foreach (var rightLine in right.ToLines())
            {
                if (leftLine.Intersects(rightLine)) return 0;
                result = System.Math.Min(result, leftLine.ClosestPointTo(rightLine.StartPoint).DistanceTo(rightLine.StartPoint));
                result = System.Math.Min(result, leftLine.ClosestPointTo(rightLine.EndPoint).DistanceTo(rightLine.EndPoint));
                result = System.Math.Min(result, rightLine.ClosestPointTo(leftLine.StartPoint).DistanceTo(leftLine.StartPoint));
                result = System.Math.Min(result, rightLine.ClosestPointTo(leftLine.EndPoint).DistanceTo(leftLine.EndPoint));
            }
        }
        return result;
    }

    private sealed class ShapeTopology(Shape perimeter, List<Shape> cutouts)
    {
        internal Shape Perimeter { get; } = perimeter;
        internal List<Shape> Cutouts { get; } = cutouts;
    }
}

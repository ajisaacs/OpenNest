#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// Material topology read once from a snapshot using the host validation rules.
/// The analytic shapes are owned by this instance and are mutable; clone them before transforming.
/// </summary>
public sealed class JobPartGeometry
{
    private const double Epsilon = 0.0000001;

    private JobPartGeometry(ShapeProfile profile)
    {
        Profile = profile;
        MaterialArea = System.Math.Abs(Perimeter.Area())
            - Cutouts.Sum(shape => System.Math.Abs(shape.Area()));
    }

    /// <summary>Analytic outer contour, clockwise to match the host CNC convention.</summary>
    public Shape Perimeter => Profile.Perimeter;

    /// <summary>Closed cutouts, counterclockwise to match the host CNC convention.</summary>
    public IReadOnlyList<Shape> Cutouts => Profile.Cutouts;

    /// <summary>Material profile suitable for Clipper preparation; arcs are preserved.</summary>
    public ShapeProfile Profile { get; }

    /// <summary>Absolute perimeter area less the absolute cutout areas at read time.</summary>
    public double MaterialArea { get; }

    /// <summary>Unrotated material bounds; rapid and scribe/etch moves are excluded.</summary>
    public Box Bounds => Perimeter.BoundingBox;

    /// <summary>Reads usable material, or returns null for an unreadable snapshot.</summary>
    public static JobPartGeometry? TryRead(PartGeometrySnapshot geometry)
    {
        try
        {
            return Read(geometry);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException or NotSupportedException or ArithmeticException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and validates closed material contours. Scribe/etch moves are ignored;
    /// open cut marks must stay inside material and do not contribute to its area.
    /// </summary>
    /// <exception cref="ArgumentException">Geometry has no usable closed material or invalid marks.</exception>
    public static JobPartGeometry Read(PartGeometrySnapshot geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Motions.Count == 0 || geometry.Motions.Any(m =>
            !double.IsFinite(m.X) || !double.IsFinite(m.Y)
            || !double.IsFinite(m.CenterX) || !double.IsFinite(m.CenterY)))
            throw new ArgumentException("Geometry must contain finite motions.", nameof(geometry));
        var entities = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(geometry));
        var cutEntities = new List<Entity>();
        foreach (var entity in entities)
            if (SpecialLayers.IsMaterial(entity.Layer))
                cutEntities.Add(entity);

        var contours = ShapeBuilder.GetShapes(cutEntities);
        if (contours.Count == 0)
            throw new ArgumentException("Geometry must contain a closed contour.");
        var closedEntities = new List<Entity>();
        var marks = new List<Shape>();
        foreach (var contour in contours)
        {
            if (contour.IsClosed())
            {
                ValidateContour(contour);
                closedEntities.AddRange(contour.Entities);
            }
            else
                marks.Add(contour);
        }
        if (closedEntities.Count == 0)
            throw new ArgumentException("Geometry must contain a closed outer contour.");

        // ShapeProfile selects the outer profile, but does not validate containment and
        // treats open chains as cutouts. Only validated closed contours may define material.
        var profile = new ShapeProfile(closedEntities);
        foreach (var cutout in profile.Cutouts)
            ValidateInternalChain(cutout, profile.Perimeter, new List<Shape>());
        foreach (var mark in marks)
            ValidateMark(mark, profile.Perimeter, profile.Cutouts);
        profile.NormalizeWinding();
        return new JobPartGeometry(profile);
    }

    private static void ValidateMark(Shape mark, Shape perimeter, List<Shape> holes)
    {
        const double chordTolerance = 0.00001;
        var boundaries = new List<Shape> { perimeter };
        boundaries.AddRange(holes);
        var polygons = boundaries.ConvertAll(s => s.ToPolygonWithTolerance(chordTolerance));
        foreach (var entity in mark.Entities)
        {
            if (entity.Length <= Epsilon || entity is not (Line or Arc))
                throw new ArgumentException("Unsupported or degenerate internal mark.");
            var parameters = new List<double> { 0, 1 };
            foreach (var boundary in boundaries)
            {
                entity.Intersects(boundary, out var intersections);
                foreach (var point in intersections)
                    AddParameter(point);
                // Include endpoints of coincident edges (parallel intersections may be empty).
                foreach (var point in boundary.Entities.CollectPoints())
                    if (entity.ClosestPointTo(point).DistanceTo(point) <= Epsilon)
                        AddParameter(point);
            }
            parameters.Sort();
            for (var index = 0; index < parameters.Count; index++)
            {
                Check(PointAt(parameters[index]));
                if (index > 0)
                    Check(PointAt((parameters[index - 1] + parameters[index]) / 2));
            }

            void AddParameter(Vector point)
            {
                if (!point.IsValid())
                    throw new ArgumentException("Indeterminate mark intersection.");
                var value = entity is Line line
                    ? line.StartPoint.DistanceTo(point) / line.Length
                    : Angle.NormalizeRad(
                        ((Arc)entity).IsReversed
                            ? ((Arc)entity).StartAngle - ((Arc)entity).Center.AngleTo(point)
                            : ((Arc)entity).Center.AngleTo(point) - ((Arc)entity).StartAngle
                    ) / ((Arc)entity).SweepAngle();
                if (value >= 0 && value <= 1)
                    parameters.Add(value);
            }
            Vector PointAt(double value)
            {
                if (entity is Line line)
                    return line.StartPoint + (line.EndPoint - line.StartPoint) * value;
                var arc = (Arc)entity;
                var angle = arc.StartAngle + (arc.IsReversed ? -1 : 1) * arc.SweepAngle() * value;
                return arc.Center
                    + new Vector(System.Math.Cos(angle), System.Math.Sin(angle)) * arc.Radius;
            }
            void Check(Vector point)
            {
                for (var index = 0; index < boundaries.Count; index++)
                {
                    // Exact analytic boundary contact is allowed; near-boundary uncertainty is not.
                    var onBoundary = false;
                    foreach (var edge in boundaries[index].Entities)
                        if (edge.ClosestPointTo(point).DistanceTo(point) <= Epsilon)
                            onBoundary = true;
                    if (onBoundary)
                        continue;
                    foreach (var edge in polygons[index].ToLines())
                        if (edge.ClosestPointTo(point).DistanceTo(point) <= 2 * chordTolerance)
                            throw new ArgumentException(
                                "Internal mark is too close to a material boundary."
                            );
                    var inside = StrictlyInside(polygons[index], point);
                    if (index == 0 ? !inside : inside)
                        throw new ArgumentException(
                            "Open geometry leaves the closed material region."
                        );
                }
            }
        }
    }

    private static void ValidateInternalChain(Shape chain, Shape perimeter, List<Shape> holes)
    {
        // A connected analytic entity cannot leave material without crossing its boundary.
        // Reject contact too: conservative, rather than guessing at tangent/collinear cuts.
        // The witness point is farther than the polygonization error from every boundary.
        const double chordTolerance = 0.00001;
        var boundaries = new List<Shape> { perimeter };
        boundaries.AddRange(holes);
        var polygons = boundaries.ConvertAll(s => s.ToPolygonWithTolerance(chordTolerance));
        foreach (var entity in chain.Entities)
        {
            if (entity.Length <= Epsilon)
                throw new ArgumentException("Geometry contains a zero-length internal edge.");
            var point = entity switch
            {
                Line line => line.StartPoint,
                Arc arc => arc.StartPoint(),
                Circle circle => circle.Center.Offset(circle.Radius, 0),
                _ => throw new ArgumentException("Unsupported internal geometry."),
            };
            if (!StrictlyInside(polygons[0], point))
                throw new ArgumentException(
                    "Open or disconnected geometry lies outside the closed perimeter."
                );
            for (var index = 0; index < boundaries.Count; index++)
            {
                if (index > 0 && polygons[index].ContainsPoint(point))
                    throw new ArgumentException("Internal geometry lies in a cutout.");
                foreach (var edge in polygons[index].ToLines())
                    if (edge.ClosestPointTo(point).DistanceTo(point) <= 2 * chordTolerance)
                        throw new ArgumentException(
                            "Internal geometry is too close to a material boundary."
                        );
                if (entity.Intersects(boundaries[index]))
                    throw new ArgumentException(
                        "Internal geometry crosses or touches a material boundary."
                    );
            }
        }
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

    /// <summary>
    /// Winding-number point-in-polygon. Returns false for points on an edge or vertex.
    /// </summary>
    private static bool StrictlyInside(Polygon polygon, Vector point)
    {
        var n = polygon.IsClosed() ? polygon.Vertices.Count - 1 : polygon.Vertices.Count;
        if (n < 3)
            return false;
        var winding = 0;
        for (var i = 0; i < n; i++)
        {
            var p1 = polygon.Vertices[i];
            var p2 = polygon.Vertices[(i + 1) % n];
            if (OnSegment(p1, p2, point))
                return false;
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
        if (!cross.IsEqualTo(0.0))
            return false;
        return System.Math.Min(a.X, b.X) - Epsilon <= p.X
            && p.X <= System.Math.Max(a.X, b.X) + Epsilon
            && System.Math.Min(a.Y, b.Y) - Epsilon <= p.Y
            && p.Y <= System.Math.Max(a.Y, b.Y) + Epsilon;
    }

    private static double IsLeft(Vector p1, Vector p2, Vector p) =>
        (p2.X - p1.X) * (p.Y - p1.Y) - (p2.Y - p1.Y) * (p.X - p1.X);

}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Deterministic rotation candidates and optional perimeter symmetry reduction.</summary>
public static class RotationCandidates
{
    /// <summary>
    /// Returns policy radians first, followed, for Automatic only, by the rotation aligning
    /// the minimum bounding rectangle and its three right-angle turns. Uses the same 0.1
    /// chord tolerance and polygon rotating-calipers implementation as Fill's rotation analysis.
    /// Results satisfy <see cref="RotationPolicy.Allows"/>, are normalized to [0, 2π), and
    /// deduplicated with a circular tolerance of 1e-7 radians, preserving first occurrence.
    /// Empty, open, non-finite or degenerate perimeters fall back to policy angles.
    /// The default policy sweep cap is 720 base samples; limit truncates the combined list,
    /// keeping policy angles (the four right angles for Automatic) first. Zero returns empty.
    /// The perimeter is not modified.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is negative.</exception>
    public static IReadOnlyList<double> ForShape(
        RotationPolicy policy, Shape perimeter, int limit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(perimeter);
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var angles = new List<double>(policy.EnumerateAngles());
        if (policy.Kind == RotationPolicyKind.Automatic && limit > angles.Count)
        {
            try
            {
                if (IsUsable(perimeter))
                {
                    var polygon = perimeter.ToPolygonWithTolerance(0.1);
                    // Polygon.FindBestRotation computes the convex hull and invokes RotatingCalipers.
                    var rectangle = polygon.FindBestRotation();
                    if (double.IsFinite(rectangle.Area) && rectangle.Area > 0)
                        for (var turn = 0; turn < 4; turn++)
                            policy.AddAngle(angles, -rectangle.Angle + turn * (System.Math.PI / 2));
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException or NotSupportedException or ArithmeticException)
            {
                // Shape-derived candidates are optional for unreadable geometry.
            }
        }
        return angles.Take(limit).ToArray();
    }

    /// <summary>
    /// Keeps the first angle for each distinct flattened perimeter, ignoring translation
    /// by moving each outline to its minimum X/Y corner. Angles are radians, normalized to
    /// [0, 2π), deduplicated with a circular tolerance of 1e-7 radians and kept in input order.
    /// Non-finite angles are omitted. No new orientations are introduced; callers requiring
    /// a policy should supply its legal candidates. The perimeter is cloned before rotation.
    /// Outlines are flattened with chord tolerance tolerance/4 and match when every vertex
    /// is within tolerance of the other outline's segments in both directions.
    /// This compares only the perimeter, not cutouts or marks.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Tolerance is not finite and positive.</exception>
    /// <exception cref="ArgumentException">The perimeter is not usable closed geometry.</exception>
    public static IReadOnlyList<double> DistinctOutlines(
        Shape perimeter, IEnumerable<double> angles, double tolerance = 1e-5)
    {
        ArgumentNullException.ThrowIfNull(perimeter);
        ArgumentNullException.ThrowIfNull(angles);
        if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance / 4 == 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!IsUsable(perimeter))
            throw new ArgumentException("A usable closed perimeter is required.", nameof(perimeter));

        var candidates = new List<double>();
        foreach (var angle in angles)
            RotationPolicy.Automatic.AddAngle(candidates, angle);
        var result = new List<double>();
        var outlines = new List<List<Vector>>();
        foreach (var angle in candidates)
        {
            var rotated = (Shape)perimeter.Clone();
            rotated.Rotate(angle);
            var points = rotated.ToPolygonWithTolerance(tolerance / 4).Vertices;
            var corner = new Vector(points.Min(p => p.X), points.Min(p => p.Y));
            var outline = points.Select(p => p - corner).ToList();
            if (outlines.Any(previous => Matches(previous, outline, tolerance)))
                continue;
            outlines.Add(outline);
            result.Add(angle);
        }
        return result;
    }

    private static bool IsUsable(Shape perimeter)
    {
        if (perimeter.Entities == null || perimeter.Entities.Count == 0)
            return false;
        foreach (var entity in perimeter.Entities)
        {
            var finite = entity switch
            {
                Line line => IsFinite(line.StartPoint) && IsFinite(line.EndPoint),
                Arc arc => IsFinite(arc.Center) && double.IsFinite(arc.Radius) && arc.Radius > 0
                    && double.IsFinite(arc.StartAngle) && double.IsFinite(arc.EndAngle),
                Circle circle => IsFinite(circle.Center)
                    && double.IsFinite(circle.Radius) && circle.Radius > 0,
                _ => false,
            };
            if (!finite || !double.IsFinite(entity.Length) || entity.Length <= 0)
                return false;
        }
        return perimeter.IsClosed() && double.IsFinite(perimeter.Area()) && perimeter.Area() > 0;
    }

    private static bool IsFinite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool Matches(List<Vector> first, List<Vector> second, double tolerance)
    {
        if (System.Math.Abs(first.Max(p => p.X) - second.Max(p => p.X)) > tolerance
            || System.Math.Abs(first.Max(p => p.Y) - second.Max(p => p.Y)) > tolerance)
            return false;
        if (first.Count == second.Count
            && first.Zip(second).All(pair => pair.First.DistanceTo(pair.Second) <= tolerance))
            return true;
        return NearSegments(first, second, tolerance) && NearSegments(second, first, tolerance);
    }

    private static bool NearSegments(List<Vector> points, List<Vector> outline, double tolerance)
    {
        foreach (var point in points)
        {
            var near = false;
            for (var index = 0; index < outline.Count; index++)
            {
                var start = outline[index];
                var edge = outline[(index + 1) % outline.Count] - start;
                var lengthSquared = edge.X * edge.X + edge.Y * edge.Y;
                var delta = point - start;
                var fraction = lengthSquared == 0 ? 0
                    : System.Math.Clamp((delta.X * edge.X + delta.Y * edge.Y) / lengthSquared, 0, 1);
                if (point.DistanceTo(start + edge * fraction) <= tolerance)
                {
                    near = true;
                    break;
                }
            }
            if (!near)
                return false;
        }
        return true;
    }
}

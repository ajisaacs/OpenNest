using System;
using System.Collections.Generic;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// Shared material of two hole-aware outlines as connected islands, through Clipper2.
/// Callers pass part-local coordinates (rebased near the origin) and add the origin back.
/// <para>
/// Coordinates are rounded to a 1e-8 model-unit grid, far below the 0.001 chord tolerance
/// and the <see cref="OpenNest.Math.Tolerance.Epsilon"/> area threshold. Each operand is
/// its outline minus the union of its holes (outline positive, holes negative,
/// <see cref="FillRule.Positive"/>).
/// Nonfinite or out-of-range coordinates throw <see cref="ArithmeticException"/>, which the
/// analyzer turns into an incomplete-check issue rather than a clear result.
/// </para>
/// </summary>
internal static class OverlapClipping
{
    private const double Scale = 1e8;

    // Scaled coordinates stay several orders of magnitude inside Clipper2's int64 range.
    private const double MaxCoordinate = 1e9;

    /// <summary>One connected island: a counterclockwise outline and clockwise holes, both closed.</summary>
    internal sealed record Island(List<Vector> Outline, List<List<Vector>> Holes, PolygonAreaMoments Moments);

    internal static List<Island> Intersect(Polygon outerA, IReadOnlyList<Polygon> holesA,
        Polygon outerB, IReadOnlyList<Polygon> holesB)
    {
        var clipper = new Clipper64();
        clipper.AddSubject(Region(outerA, holesA));
        clipper.AddClip(Region(outerB, holesB));
        var tree = new PolyTree64();
        if (!clipper.Execute(ClipType.Intersection, FillRule.Positive, tree))
            throw new InvalidOperationException("Clipper2 could not intersect the overlap material.");
        var islands = new List<Island>();
        Collect(tree, islands);
        return islands;
    }

    private static void Collect(PolyPath64 node, List<Island> islands)
    {
        for (var i = 0; i < node.Count; i++)
        {
            var outer = (PolyPath64)node[i];
            var outline = ToVectors(outer.Polygon, positive: true);
            var holes = new List<List<Vector>>();
            for (var h = 0; h < outer.Count; h++)
            {
                var hole = (PolyPath64)outer[h];
                holes.Add(ToVectors(hole.Polygon, positive: false));
                // An island can sit inside a hole formed by both operands' holes together.
                Collect(hole, islands);
            }
            if (TryMoments(outline, holes, out var moments))
                islands.Add(new Island(outline, holes, moments));
            else if (Clipper.Area(outer.Polygon) / (Scale * Scale) >= OpenNest.Math.Tolerance.Epsilon)
                throw new ArithmeticException("Overlap island area moments are invalid.");
            // Otherwise a degenerate dust island below the reporting threshold is skipped.
        }
    }

    /// <summary>
    /// Net area and centroid of an outline minus its holes. Fails for degenerate,
    /// nonfinite or nonpositive results.
    /// </summary>
    private static bool TryMoments(List<Vector> outline, List<List<Vector>> holes, out PolygonAreaMoments moments)
    {
        moments = default;
        if (!PolygonAreaMoments.TryCompute(outline, out var outer))
            return false;
        var removed = new List<PolygonAreaMoments>(holes.Count);
        foreach (var hole in holes)
        {
            if (!PolygonAreaMoments.TryCompute(hole, out var hollow))
                return false;
            removed.Add(hollow);
        }
        return PolygonAreaMoments.TrySubtract(outer, removed, out moments);
    }

    private static Paths64 Region(Polygon outer, IReadOnlyList<Polygon> holes)
    {
        var region = new Paths64 { ToPath(outer, positive: true) };
        if (holes != null)
            foreach (var hole in holes)
                region.Add(ToPath(hole, positive: false));
        return region;
    }

    private static Path64 ToPath(Polygon polygon, bool positive)
    {
        var vertices = polygon.Vertices;
        var count = vertices.Count;
        if (count > 1 && vertices[count - 1].X == vertices[0].X && vertices[count - 1].Y == vertices[0].Y)
            count--;
        var path = new Path64(count);
        for (var i = 0; i < count; i++)
        {
            var point = vertices[i];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)
                || System.Math.Abs(point.X) > MaxCoordinate || System.Math.Abs(point.Y) > MaxCoordinate)
                throw new ArithmeticException("Overlap material is outside the exact clipping range.");
            path.Add(new Point64(System.Math.Round(point.X * Scale), System.Math.Round(point.Y * Scale)));
        }
        if (Clipper.IsPositive(path) != positive)
            path.Reverse();
        return path;
    }

    private static List<Vector> ToVectors(Path64 path, bool positive)
    {
        var points = new List<Vector>(path.Count + 1);
        foreach (var point in path)
            points.Add(new Vector(point.X / Scale, point.Y / Scale));
        if (Clipper.IsPositive(path) != positive)
            points.Reverse();
        points.Add(points[0]); // Regions repeat their closing vertex exactly.
        return points;
    }
}

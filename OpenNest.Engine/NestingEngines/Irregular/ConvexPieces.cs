#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Splits a simple polygon into convex pieces on the Clipper lattice: ear-clipping
/// triangulation followed by Hertel-Mehlhorn merging of triangles across any diagonal whose
/// removal keeps both end vertices convex. Every decision is an exact integer cross product,
/// so the pieces tile the lattice-rounded polygon exactly; a polygon the clipper cannot
/// finish (self-touching, zero-area runs) yields null and callers keep their fallback.
/// </summary>
internal static class ConvexPieces
{
    /// <summary>
    /// Convex CCW pieces whose union is <paramref name="polygon"/> rounded to
    /// <paramref name="precision"/> decimals, or null when the polygon cannot be decomposed.
    /// A convex input comes back as a single piece.
    /// </summary>
    public static List<PathD>? Decompose(PathD polygon, int precision)
    {
        var scale = System.Math.Pow(10, precision);
        var points = Dedupe(Clipper.ScalePath64(polygon, scale));
        if (points.Count < 3)
            return null;
        if (Area2(points) < 0)
            points.Reverse();
        if (Area2(points) <= 0)
            return null;

        var triangles = Triangulate(points);
        if (triangles == null)
            return null;
        var pieces = Merge(points, triangles);
        var result = new List<PathD>(pieces.Count);
        foreach (var piece in pieces)
        {
            var path = new PathD(piece.Count);
            foreach (var index in piece)
                path.Add(new PointD(points[index].X / scale, points[index].Y / scale));
            result.Add(path);
        }
        return result;
    }

    private static List<Point64> Dedupe(Path64 source)
    {
        var points = new List<Point64>(source.Count);
        foreach (var point in source)
            if (points.Count == 0 || points[^1] != point)
                points.Add(point);
        while (points.Count > 1 && points[0] == points[^1])
            points.RemoveAt(points.Count - 1);
        return points;
    }

    /// <summary>Twice the signed area; positive for CCW. Measured from a vertex so that far-off
    /// outlines do not overflow.</summary>
    private static Int128 Area2(List<Point64> points)
    {
        Int128 area = 0;
        for (var i = 1; i + 1 < points.Count; i++)
            area += Cross(points[0], points[i], points[i + 1]);
        return area;
    }

    private static Int128 Cross(Point64 a, Point64 b, Point64 c) =>
        ((Int128)b.X - a.X) * ((Int128)c.Y - a.Y) - ((Int128)b.Y - a.Y) * ((Int128)c.X - a.X);

    /// <summary>
    /// Ear clipping over vertex indices. An ear is a strictly convex corner whose triangle has
    /// no other vertex inside or on its boundary; zero-area corners are never clipped, so a
    /// polygon that degenerates to collinear remains yields null.
    /// </summary>
    private static List<int[]>? Triangulate(List<Point64> points)
    {
        var ring = new List<int>(points.Count);
        for (var i = 0; i < points.Count; i++)
            ring.Add(i);
        var triangles = new List<int[]>(points.Count);
        var start = 0;
        while (ring.Count > 3)
        {
            var found = false;
            for (var attempt = 0; attempt < ring.Count; attempt++)
            {
                var at = (start + attempt) % ring.Count;
                var previous = ring[(at + ring.Count - 1) % ring.Count];
                var current = ring[at];
                var next = ring[(at + 1) % ring.Count];
                if (!IsEar(points, ring, previous, current, next))
                    continue;
                triangles.Add(new[] { previous, current, next });
                ring.RemoveAt(at);
                start = at % ring.Count;
                found = true;
                break;
            }
            if (!found)
                return null;
        }
        if (Cross(points[ring[0]], points[ring[1]], points[ring[2]]) <= 0)
            return null;
        triangles.Add(new[] { ring[0], ring[1], ring[2] });
        return triangles;
    }

    private static bool IsEar(List<Point64> points, List<int> ring, int previous, int current, int next)
    {
        var a = points[previous];
        var b = points[current];
        var c = points[next];
        if (Cross(a, b, c) <= 0)
            return false;
        foreach (var index in ring)
        {
            if (index == previous || index == current || index == next)
                continue;
            var p = points[index];
            if (p == a || p == b || p == c)
                continue;
            if (Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Hertel-Mehlhorn: drop every diagonal whose two end corners stay convex (or straight)
    /// without it. Pieces are cyclic index lists; a diagonal is an edge whose reverse lies in
    /// another piece.
    /// </summary>
    private static List<List<int>> Merge(List<Point64> points, List<int[]> triangles)
    {
        var pieces = new List<List<int>?>(triangles.Count);
        foreach (var triangle in triangles)
            pieces.Add(new List<int>(triangle));
        // Directed edge (from, to) -> piece holding it.
        var owner = new Dictionary<(int, int), int>();
        for (var p = 0; p < pieces.Count; p++)
            foreach (var edge in Edges(pieces[p]!))
                owner[edge] = p;

        for (var p = 0; p < pieces.Count; p++)
        {
            var merged = true;
            while (merged && pieces[p] != null)
            {
                merged = false;
                var piece = pieces[p]!;
                for (var e = 0; e < piece.Count; e++)
                {
                    var i = piece[e];
                    var j = piece[(e + 1) % piece.Count];
                    if (!owner.TryGetValue((j, i), out var q) || q == p || pieces[q] == null)
                        continue;
                    var other = pieces[q]!;
                    // piece: ... a, i, j, b ...   other: ... c, j, i, d ...
                    var a = piece[(e + piece.Count - 1) % piece.Count];
                    var b = piece[(e + 2) % piece.Count];
                    var f = other.IndexOf(j);
                    var c = other[(f + other.Count - 1) % other.Count];
                    var d = other[(f + 2) % other.Count];
                    if (Cross(points[a], points[i], points[d]) < 0 || Cross(points[c], points[j], points[b]) < 0)
                        continue;

                    // Walk piece from j around to i, then other from i around to j.
                    var union = new List<int>(piece.Count + other.Count - 2);
                    for (var k = 0; k < piece.Count - 1; k++)
                        union.Add(piece[(e + 1 + k) % piece.Count]);
                    for (var k = 0; k < other.Count - 1; k++)
                        union.Add(other[(f + 1 + k) % other.Count]);
                    foreach (var edge in Edges(other))
                        owner.Remove(edge);
                    foreach (var edge in Edges(piece))
                        owner.Remove(edge);
                    foreach (var edge in Edges(union))
                        owner[edge] = p;
                    pieces[p] = union;
                    pieces[q] = null;
                    merged = true;
                    break;
                }
            }
        }

        var result = new List<List<int>>();
        foreach (var piece in pieces)
            if (piece != null)
                result.Add(piece);
        return result;
    }

    private static IEnumerable<(int, int)> Edges(List<int> piece)
    {
        for (var k = 0; k < piece.Count; k++)
            yield return (piece[k], piece[(k + 1) % piece.Count]);
    }
}

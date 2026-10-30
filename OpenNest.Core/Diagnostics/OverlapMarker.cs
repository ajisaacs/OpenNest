using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// A marker point on one island of shared material. The area centroid of a concave,
/// holed or split overlap can lie off the material, so markers use the island's pole of
/// inaccessibility instead, with a guaranteed-interior fallback.
/// </summary>
internal static class OverlapMarker
{
    // Distance evaluations per island; a long sliver otherwise refines along its whole ridge.
    internal const int MaxCells = 4096;

    /// <summary>
    /// The island's pole of inaccessibility (searched to a tenth of its area/perimeter, a
    /// lower bound on a thin island's half-width) when that bounded search lands inside;
    /// otherwise the midpoint of the widest interior run of a horizontal scan line; otherwise
    /// <paramref name="fallback"/>. Rings are closed, as the clipping returns them.
    /// </summary>
    internal static Vector Find(IReadOnlyList<Vector> outline, IReadOnlyList<List<Vector>> holes, double area,
        Vector fallback, int maxCells = MaxCells)
    {
        var outer = ToPolygon(outline);
        var hollow = holes.Select(ToPolygon).ToList();
        var perimeter = outer.Perimeter() + hollow.Sum(hole => hole.Perimeter());
        var precision = 0.1 * area / perimeter;
        if (double.IsFinite(precision) && precision > 0)
        {
            var pole = PolyLabel.Find(outer, hollow, precision, maxCells);
            if (Inside(outline, holes, pole))
                return pole;
        }
        return TryScanLine(outline, holes, out var point) ? point : fallback;
    }

    /// <summary>
    /// Midpoint of the widest interior run along a horizontal line through the middle of the
    /// island, placed between vertex heights so crossings are unambiguous (as JTS's
    /// interior-point-of-area). Even-odd pairing of outline and hole crossings keeps it
    /// inside the material.
    /// </summary>
    private static bool TryScanLine(IReadOnlyList<Vector> outline, IReadOnlyList<List<Vector>> holes, out Vector point)
    {
        point = default;
        var minY = outline.Min(vertex => vertex.Y);
        var maxY = outline.Max(vertex => vertex.Y);
        var center = (minY + maxY) / 2;
        double below = minY, above = maxY;
        foreach (var vertex in outline.Concat(holes.SelectMany(hole => hole)))
        {
            if (vertex.Y <= center && vertex.Y > below)
                below = vertex.Y;
            else if (vertex.Y > center && vertex.Y < above)
                above = vertex.Y;
        }
        var y = (below + above) / 2;
        if (!(y > minY && y < maxY))
            return false;

        var crossings = new List<double>();
        AddCrossings(outline, y, crossings);
        foreach (var hole in holes)
            AddCrossings(hole, y, crossings);
        crossings.Sort();
        var widest = 0.0;
        for (var i = 0; i + 1 < crossings.Count; i += 2)
        {
            var width = crossings[i + 1] - crossings[i];
            if (width > widest)
            {
                widest = width;
                point = new Vector((crossings[i] + crossings[i + 1]) / 2, y);
            }
        }
        return widest > 0;
    }

    private static void AddCrossings(IReadOnlyList<Vector> ring, double y, List<double> crossings)
    {
        for (var i = 0; i + 1 < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[i + 1];
            if ((a.Y > y) != (b.Y > y))
                crossings.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
        }
    }

    private static bool Inside(IReadOnlyList<Vector> outline, IReadOnlyList<List<Vector>> holes, Vector point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y)
        && InsideRing(outline, point) && !holes.Any(hole => InsideRing(hole, point));

    // Crossing-number test over a closed ring (half-open edges, so a vertex counts once).
    private static bool InsideRing(IReadOnlyList<Vector> ring, Vector point)
    {
        var inside = false;
        for (var i = 0; i + 1 < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[i + 1];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                inside = !inside;
        }
        return inside;
    }

    private static Polygon ToPolygon(IReadOnlyList<Vector> ring)
    {
        var polygon = new Polygon();
        polygon.Vertices.AddRange(ring);
        polygon.UpdateBounds();
        return polygon;
    }
}

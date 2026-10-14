using System;
using System.Collections.Generic;

namespace OpenNest.Geometry;

/// <summary>
/// Strict positive-length entry of an open native line path into polygon material.
/// Boundary-only contact is not entry; holes subtract material. This deliberately
/// does not infer material from a path's bounding box or close the open path.
/// Curved contours are flattened by the caller, which must handle chord uncertainty.
/// </summary>
internal static class OpenPathMaterial
{
    public static bool Enters(Polygon outer, IReadOnlyList<Polygon> holes, IReadOnlyList<Line> path)
    {
        if (outer?.Vertices.Count < 4 || path == null)
            return true; // Unresolved geometry is not a certificate of clear material.

        foreach (var line in path)
        {
            var start = line.StartPoint;
            var end = line.EndPoint;
            var travel = end - start;
            var lengthSquared = travel.DotProduct(travel);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= 0)
                return true;

            var cuts = new List<double> { 0, 1 };
            CollectCuts(outer, start, travel, lengthSquared, cuts);
            if (holes != null)
                foreach (var hole in holes)
                    CollectCuts(hole, start, travel, lengthSquared, cuts);
            cuts.Sort();
            for (var i = 1; i < cuts.Count; i++)
            {
                if (cuts[i] <= cuts[i - 1])
                    continue;
                var midpoint = start + travel * ((cuts[i - 1] + cuts[i]) / 2);
                if (!StrictlyInside(outer, midpoint))
                    continue;
                var inHole = false;
                if (holes != null)
                    foreach (var hole in holes)
                        if (InsideOrOnBoundary(hole, midpoint))
                        {
                            inHole = true;
                            break;
                        }
                if (!inHole)
                    return true;
            }
        }
        return false;
    }

    private static void CollectCuts(Polygon ring, Vector start, Vector travel,
        double lengthSquared, List<double> cuts)
    {
        if (ring?.Vertices.Count < 4)
        {
            // A degenerate hole cannot be subtracted reliably. The caller's
            // unresolved-topology guard handles this case before this helper.
            return;
        }
        var vertices = ring.Vertices;
        for (var i = 1; i < vertices.Count; i++)
        {
            var a = vertices[i - 1];
            var edge = vertices[i] - a;
            var difference = a - start;
            var denominator = Cross(travel, edge);
            if (denominator == 0)
            {
                if (Cross(difference, travel) != 0)
                    continue;
                AddParameter(difference.DotProduct(travel) / lengthSquared, cuts);
                AddParameter((vertices[i] - start).DotProduct(travel) / lengthSquared, cuts);
                continue;
            }
            var t = Cross(difference, edge) / denominator;
            var u = Cross(difference, travel) / denominator;
            if (u >= -1e-12 && u <= 1 + 1e-12)
                AddParameter(t, cuts);
        }
    }

    private static void AddParameter(double t, List<double> cuts)
    {
        if (double.IsFinite(t) && t >= -1e-12 && t <= 1 + 1e-12)
            cuts.Add(System.Math.Clamp(t, 0, 1));
    }

    private static bool StrictlyInside(Polygon ring, Vector point) =>
        Contains(ring, point, boundaryIsInside: false);

    private static bool InsideOrOnBoundary(Polygon ring, Vector point) =>
        Contains(ring, point, boundaryIsInside: true);

    private static bool Contains(Polygon ring, Vector point, bool boundaryIsInside)
    {
        var vertices = ring.Vertices;
        var inside = false;
        for (var i = 1; i < vertices.Count; i++)
        {
            var a = vertices[i - 1];
            var b = vertices[i];
            var edge = b - a;
            var relative = point - a;
            if (Cross(edge, relative) == 0 &&
                point.X >= System.Math.Min(a.X, b.X) && point.X <= System.Math.Max(a.X, b.X) &&
                point.Y >= System.Math.Min(a.Y, b.Y) && point.Y <= System.Math.Max(a.Y, b.Y))
                return boundaryIsInside;
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < a.X + (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y))
                inside = !inside;
        }
        return inside;
    }

    private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
}

using System.Collections.Generic;

namespace OpenNest.Geometry;

/// <summary>
/// Positive area and area centroid of a simple polygon, independent of winding.
/// Moments are evaluated about a nearby origin rather than the world origin.
/// </summary>
public readonly struct PolygonAreaMoments
{
    private PolygonAreaMoments(double area, Vector centroid)
    {
        Area = area;
        Centroid = centroid;
    }

    public double Area { get; }
    public Vector Centroid { get; }

    /// <summary>
    /// Accepts an open vertex list or an exactly repeated closing vertex. Returns false
    /// for degenerate or nonfinite moments; does not validate polygon topology.
    /// </summary>
    public static bool TryCompute(IReadOnlyList<Vector> vertices, out PolygonAreaMoments moments)
    {
        moments = default;
        if (vertices == null || vertices.Count < 3)
            return false;
        foreach (var point in vertices)
            if (!IsFinite(point))
                return false;

        var origin = vertices[0];
        var count = vertices.Count;
        if (vertices[count - 1].X == origin.X && vertices[count - 1].Y == origin.Y)
            count--;
        if (count < 3)
            return false;

        var twiceArea = 0.0;
        var momentX = 0.0;
        var momentY = 0.0;
        // The closing edges meet the local origin and contribute zero. The signed
        // triangle fan also handles concavity without averaging polygon vertices.
        for (var i = 1; i + 1 < count; i++)
        {
            var a = vertices[i] - origin;
            var b = vertices[i + 1] - origin;
            var cross = a.X * b.Y - a.Y * b.X;
            twiceArea += cross;
            momentX += (a.X + b.X) * cross;
            momentY += (a.Y + b.Y) * cross;
        }

        var area = System.Math.Abs(twiceArea) * 0.5;
        if (!double.IsFinite(area) || area <= 0
            || !double.IsFinite(momentX) || !double.IsFinite(momentY))
            return false;
        // Dividing signed moments by signed area cancels the winding, while Area
        // stays positive so independently wound fragments always add material.
        var centroid = origin + new Vector(momentX / twiceArea / 3, momentY / twiceArea / 3);
        if (!IsFinite(centroid))
            return false;
        moments = new PolygonAreaMoments(area, centroid);
        return true;
    }

    /// <summary>
    /// Combines nonoverlapping, already hole-subtracted fragments using positive area
    /// weights and another local origin. The centroid may lie outside the material.
    /// Empty input or any invalid fragment fails the entire result.
    /// </summary>
    public static bool TryCombine(IEnumerable<PolygonAreaMoments> fragments, out PolygonAreaMoments moments)
    {
        moments = default;
        if (fragments == null)
            return false;
        var area = 0.0;
        var origin = Vector.Zero;
        var firstMoment = Vector.Zero;
        foreach (var fragment in fragments)
        {
            if (!double.IsFinite(fragment.Area) || fragment.Area <= 0 || !IsFinite(fragment.Centroid))
                return false;
            if (area == 0)
                origin = fragment.Centroid;
            firstMoment += (fragment.Centroid - origin) * fragment.Area;
            area += fragment.Area;
        }

        if (!double.IsFinite(area) || area <= 0 || !IsFinite(firstMoment))
            return false;
        var centroid = origin + firstMoment / area;
        if (!IsFinite(centroid))
            return false;
        moments = new PolygonAreaMoments(area, centroid);
        return true;
    }

    private static bool IsFinite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}

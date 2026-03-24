using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest;

/// <summary>
/// Static helpers for testing entity-splitline intersections.
/// </summary>
public static class SplitLineIntersect
{
    /// <summary>
    /// Finds the intersection point between an entity and a split line.
    /// Returns null if no intersection or the entity doesn't straddle the split line.
    /// </summary>
    public static Vector? FindIntersection(Entity entity, SplitLine sl)
    {
        if (!CrossesSplitLine(entity, sl))
            return null;

        var bbox = entity.BoundingBox;
        var margin = 1.0;

        // Create a line at the split position spanning the entity's bbox extent (with margin)
        Line splitLine;

        if (sl.Axis == CutOffAxis.Vertical)
            splitLine = sl.ToLine(bbox.Bottom - margin, bbox.Top + margin);
        else
            splitLine = sl.ToLine(bbox.Left - margin, bbox.Right + margin);

        switch (entity.Type)
        {
            case EntityType.Line:
                var line = (Line)entity;
                if (Intersect.Intersects(line, splitLine, out var pt))
                    return pt;
                return null;

            case EntityType.Arc:
                var arc = (Arc)entity;
                if (Intersect.Intersects(arc, splitLine, out var pts))
                    return pts.Count > 0 ? pts[0] : null;
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns true if the entity's bounding box straddles the split line,
    /// meaning it extends to both sides of the split position (not just touching).
    /// </summary>
    public static bool CrossesSplitLine(Entity entity, SplitLine sl)
    {
        var bbox = entity.BoundingBox;

        if (sl.Axis == CutOffAxis.Vertical)
            return bbox.Left < sl.Position - Tolerance.Epsilon
                && bbox.Right > sl.Position + Tolerance.Epsilon;
        else
            return bbox.Bottom < sl.Position - Tolerance.Epsilon
                && bbox.Top > sl.Position + Tolerance.Epsilon;
    }

    /// <summary>
    /// Returns -1 if the point is below/left of the split line,
    /// +1 if above/right, or 0 if on the line (within tolerance).
    /// </summary>
    public static int SideOf(Vector pt, SplitLine sl)
    {
        var value = sl.Axis == CutOffAxis.Vertical ? pt.X : pt.Y;
        var diff = value - sl.Position;

        if (System.Math.Abs(diff) <= Tolerance.Epsilon)
            return 0;

        return diff < 0 ? -1 : 1;
    }
}

using System.Collections.Generic;

namespace OpenNest.Geometry;

/// <summary>
/// Tolerance-free test of material against an axis-aligned keep-out rectangle, for
/// rectangles too small for the tolerance-based overlap machinery (validation offsets,
/// Clipper grids, triangulation and vector equality work at 1e-3 to 1e-5 scale and collapse
/// such a rectangle, PM 6859db53). Positive-area overlap only: flush contact is clear, as
/// everywhere else.
/// </summary>
public static class KeepOutOverlap
{
    /// <summary>The scale below which tolerance-based overlap tests are not trusted for a
    /// keep-out rectangle (the validation outline chord tolerance).</summary>
    public const double ToleranceFreeBelow = 0.001;

    /// <summary>A keep-out narrower than <see cref="ToleranceFreeBelow"/> on either axis.</summary>
    public static bool NeedsToleranceFreeTest(Box keepOut) =>
        keepOut.Length < ToleranceFreeBelow || keepOut.Width < ToleranceFreeBelow;

    /// <summary>
    /// Chord tolerance for flattening real part material against <paramref name="keepOut"/>:
    /// a quarter of its smaller side, at most <see cref="ToleranceFreeBelow"/>.
    /// </summary>
    public static double FlatteningTolerance(Box keepOut) =>
        System.Math.Min(ToleranceFreeBelow, System.Math.Min(keepOut.Length, keepOut.Width) / 4);

    /// <summary>
    /// A part's native contours flattened two ways for a tiny keep-out, never through a
    /// grid-rounding offset: lines exact, every arc (perimeter and cutouts) inscribed in the
    /// first and circumscribed in the second, at <see cref="FlatteningTolerance"/>. Along a
    /// convex arc the circumscribed version holds all real material, along a concave arc
    /// (a notch, a round hole) the inscribed one does, so together they hold all of it and
    /// testing both finds any real overlap. The price is a possible report within that
    /// tolerance of an arc, never a false clear.
    /// </summary>
    public static IEnumerable<(Polygon Perimeter, List<Polygon> Holes)> ConservativePolygons(
        ShapeProfile profile, Box keepOut)
    {
        var tolerance = FlatteningTolerance(keepOut);
        foreach (var circumscribe in new[] { false, true })
        {
            var perimeter = profile.Perimeter.ToPolygonWithTolerance(tolerance, circumscribe);
            var holes = new List<Polygon>(profile.Cutouts.Count);
            foreach (var cutout in profile.Cutouts)
                holes.Add(cutout.ToPolygonWithTolerance(tolerance, circumscribe));
            yield return (perimeter, holes);
        }
    }

    /// <summary>
    /// True when either of the <see cref="ConservativePolygons"/>, translated by
    /// <paramref name="offset"/>, enters <paramref name="keepOut"/>.
    /// </summary>
    public static bool ProfileEnters(ShapeProfile profile, Vector offset, Box keepOut)
    {
        foreach (var (perimeter, holes) in ConservativePolygons(profile, keepOut))
        {
            perimeter.Offset(offset);
            foreach (var hole in holes)
                hole.Offset(offset);
            if (MaterialEnters(perimeter, holes, keepOut))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the material (perimeter minus holes) shares positive area with the open
    /// rectangle: some boundary edge runs through its interior, or its interior lies wholly
    /// inside the material (tested at its centre).
    /// </summary>
    public static bool MaterialEnters(Polygon perimeter, IReadOnlyList<Polygon> holes, Box box)
    {
        if (RingEnters(perimeter, box))
            return true;
        if (holes != null)
            foreach (var hole in holes)
                if (RingEnters(hole, box))
                    return true;

        var centre = new Vector((box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);
        if (!Contains(perimeter, centre))
            return false;
        if (holes != null)
            foreach (var hole in holes)
                if (Contains(hole, centre))
                    return false;
        return true;
    }

    private static bool RingEnters(Polygon ring, Box box)
    {
        var vertices = ring.Vertices;
        var count = vertices.Count;
        for (var i = 0; i < count; i++)
        {
            var next = vertices[(i + 1) % count];
            if (SegmentEntersOpenBox(vertices[i], next, box))
                return true;
        }
        return false;
    }

    /// <summary>Liang-Barsky clip against the open rectangle; a positive-length part of the
    /// segment must lie strictly inside it.</summary>
    private static bool SegmentEntersOpenBox(Vector p, Vector q, Box box)
    {
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        var t0 = 0.0;
        var t1 = 1.0;
        return Clip(-dx, p.X - box.Left, ref t0, ref t1)
            && Clip(dx, box.Right - p.X, ref t0, ref t1)
            && Clip(-dy, p.Y - box.Bottom, ref t0, ref t1)
            && Clip(dy, box.Top - p.Y, ref t0, ref t1)
            && t0 < t1;
    }

    /// <summary>Restricts [t0, t1] to the parameters where <c>denominator * t &lt; numerator</c>.</summary>
    private static bool Clip(double denominator, double numerator, ref double t0, ref double t1)
    {
        if (denominator == 0)
            return numerator > 0;
        var r = numerator / denominator;
        if (denominator < 0)
        {
            if (r >= t1)
                return false;
            if (r > t0)
                t0 = r;
        }
        else
        {
            if (r <= t0)
                return false;
            if (r < t1)
                t1 = r;
        }
        return true;
    }

    /// <summary>Even-odd ray cast; exact for a point that is not on the ring (callers only
    /// ask after ruling out any edge through the rectangle containing the point).</summary>
    private static bool Contains(Polygon ring, Vector point)
    {
        var inside = false;
        var vertices = ring.Vertices;
        var count = vertices.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var a = vertices[i];
            var b = vertices[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

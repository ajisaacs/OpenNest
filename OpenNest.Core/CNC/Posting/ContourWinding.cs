using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.Posting;

/// <summary>
/// Which posted contour is the perimeter, and which compensation side keeps the kerf in the
/// scrap. Imported drawings are wound perimeter CW and holes CCW
/// (<see cref="ShapeProfile.NormalizeWinding"/>), which puts the part right of travel and gives
/// G41 throughout; a contour wound the other way (built in code or edited by hand) needs G42.
/// Paths that enclose no area (open break cuts) get G41, a defensive default.
/// </summary>
public static class ContourWinding
{
    /// <summary>Signed shoelace area of the cut path; negative when clockwise, 0 when degenerate.</summary>
    public static double SignedArea(PostContour contour)
    {
        var pts = SamplePoints(contour);
        if (pts.Count < 3)
            return 0.0;

        var sum = 0.0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2.0;
    }

    /// <summary>
    /// Index of the perimeter: the material contour enclosing the largest bounding box, as
    /// <see cref="ShapeProfile"/> picks it. -1 when none encloses any area.
    /// </summary>
    public static int PerimeterIndex(IReadOnlyList<PostContour> contours)
    {
        var best = -1;
        var bestArea = 0.0;
        for (var i = 0; i < contours.Count; i++)
        {
            if (contours[i].IsMark || !EnclosesArea(contours[i]))
                continue;

            var area = BoundingBoxArea(SamplePoints(contours[i]));
            if (best < 0 || area > bestArea)
            {
                best = i;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>
    /// <see cref="KerfType.Left"/> (G41, part right of travel) for a CW perimeter, a CCW hole or
    /// a path enclosing no area; otherwise <see cref="KerfType.Right"/> (G42).
    /// </summary>
    public static KerfType CompensationSide(PostContour contour, bool isPerimeter)
    {
        if (!EnclosesArea(contour))
            return KerfType.Left;

        var clockwise = SignedArea(contour) < 0.0;
        return clockwise == isPerimeter ? KerfType.Left : KerfType.Right;
    }

    private static bool EnclosesArea(PostContour contour) =>
        System.Math.Abs(SignedArea(contour)) > 1e-9;

    private static double BoundingBoxArea(List<Vector> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            minX = System.Math.Min(minX, p.X);
            minY = System.Math.Min(minY, p.Y);
            maxX = System.Math.Max(maxX, p.X);
            maxY = System.Math.Max(maxY, p.Y);
        }

        return (maxX - minX) * (maxY - minY);
    }

    /// <summary>
    /// Ordered points along the path: the contour start, then each cut endpoint, with arc
    /// interior samples at no more than 90-degree steps so full circles give a well-formed
    /// polygon. Consecutive duplicates are dropped so zero-length lead-ins do not skew it.
    /// </summary>
    private static List<Vector> SamplePoints(PostContour contour)
    {
        var pts = new List<Vector>();

        void Add(Vector v)
        {
            if (pts.Count == 0 || pts[^1].DistanceTo(v) > 1e-9)
                pts.Add(v);
        }

        var prev = contour.LeadIn != null ? contour.LeadIn.EndPoint : contour.Pierce;
        Add(prev);

        foreach (var cut in contour.Cuts)
        {
            if (cut is ArcMove arc)
            {
                foreach (var mid in ArcSamplePoints(prev, arc))
                    Add(mid);
            }
            Add(cut.EndPoint);
            prev = cut.EndPoint;
        }

        return pts;
    }

    /// <summary>
    /// Interior points on the arc from <paramref name="prev"/> to its endpoint at no more than
    /// ~90-degree steps. A full circle yields four, so its shoelace sign is its true winding.
    /// </summary>
    private static List<Vector> ArcSamplePoints(Vector prev, ArcMove arc)
    {
        var points = new List<Vector>();
        var center = arc.CenterPoint;
        var radius = center.DistanceTo(arc.EndPoint);
        if (radius < 1e-12)
            return points;

        var a0 = System.Math.Atan2(prev.Y - center.Y, prev.X - center.X);
        var a1 = System.Math.Atan2(arc.EndPoint.Y - center.Y, arc.EndPoint.X - center.X);

        var sweep = arc.Rotation == RotationType.CW ? a0 - a1 : a1 - a0;
        if (sweep <= 0)
            sweep += 2.0 * System.Math.PI;

        // Full circle (start == end) reads as zero sweep above.
        if (
            sweep < 1e-9
            && System.Math.Abs(prev.X - arc.EndPoint.X) < 1e-9
            && System.Math.Abs(prev.Y - arc.EndPoint.Y) < 1e-9
        )
            sweep = 2.0 * System.Math.PI;

        var direction = arc.Rotation == RotationType.CW ? -1.0 : 1.0;
        var steps = System.Math.Max(1, (int)System.Math.Ceiling(sweep / (System.Math.PI / 2.0)));

        for (var k = 1; k <= steps; k++)
        {
            var angle = a0 + direction * sweep * k / (steps + 1);
            points.Add(
                new Vector(
                    center.X + radius * System.Math.Cos(angle),
                    center.Y + radius * System.Math.Sin(angle)
                )
            );
        }

        return points;
    }
}

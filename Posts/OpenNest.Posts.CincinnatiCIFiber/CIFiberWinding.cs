using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Classifies a flattened contour as interior or exterior from the
    /// material side of its cut path, so the post picks the G41 (inside, kerf
    /// left of travel) vs G42 (outside, kerf right of travel) lead-in macro to
    /// keep the kerf on the scrap side, matching the machine sample (holes cut
    /// CCW + G41, perimeter cut CCW + G42).
    ///
    /// OpenNest contours all keep material on the left of travel (see
    /// OffsetSide / ContourCuttingStrategy): the perimeter runs clockwise
    /// (negative shoelace area) and holes run counter-clockwise (positive).
    /// A contour path that is not closed classifies as interior — a defensive
    /// default; open paths should not reach the post.
    /// </summary>
    public static class CIFiberWinding
    {
        /// <summary>
        /// Signed shoelace area of the contour path (arcs sampled at their
        /// arc-mid point). 0 for degenerate paths.
        /// </summary>
        public static double SignedArea(CIFiberContour contour)
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
        /// True when the contour is a closed CW path — an external perimeter
        /// under the OpenNest material-left convention.
        /// </summary>
        public static bool IsExterior(CIFiberContour contour)
        {
            if (!IsClosed(contour))
                return false;

            return SignedArea(contour) < 0.0;
        }

        /// <summary>True when the cut path returns to its start point.</summary>
        public static bool IsClosed(CIFiberContour contour, double tolerance = 1e-6)
        {
            var pts = SamplePoints(contour);
            if (pts.Count < 3)
                return false;

            return pts[0].DistanceTo(pts[^1]) <= tolerance;
        }

        /// <summary>
        /// Ordered points along the path: lead-in endpoint, then each cut move
        /// endpoint, with arc interior samples at <=90-degree steps (so full
        /// circles produce a well-formed polygon). Consecutive duplicates are
        /// dropped so zero-length lead-ins do not skew the sample.
        /// </summary>
        public static List<Vector> SamplePoints(CIFiberContour contour)
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
        /// Interior points on the arc from <paramref name="prev"/> to the arc
        /// endpoint at no more than ~90-degree sweep intervals. A full circle
        /// yields four interior points, giving the shoelace a non-degenerate
        /// polygon whose sign is the circle's true winding.
        /// </summary>
        public static List<Vector> ArcSamplePoints(Vector prev, ArcMove arc)
        {
            var points = new List<Vector>();
            var center = arc.CenterPoint;
            var radius = center.DistanceTo(arc.EndPoint);
            if (radius < 1e-12)
                return points;

            var a0 = System.Math.Atan2(prev.Y - center.Y, prev.X - center.X);
            var a1 = System.Math.Atan2(arc.EndPoint.Y - center.Y, arc.EndPoint.X - center.X);

            double sweep;
            if (arc.Rotation == RotationType.CW)
            {
                sweep = a0 - a1;
                if (sweep <= 0)
                    sweep += 2.0 * System.Math.PI;
            }
            else
            {
                sweep = a1 - a0;
                if (sweep <= 0)
                    sweep += 2.0 * System.Math.PI;
            }

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
}

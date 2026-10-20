using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Works out, from a part's flattened contours, which one is the perimeter
    /// (L4 lead-in macro, the rest are holes on L2) and which compensation
    /// side keeps the kerf in the scrap.
    ///
    /// G41 leaves the product right of travel. Imported drawings are wound
    /// perimeter CW and holes CCW (ShapeProfile.NormalizeWinding), which puts
    /// the part on the right and gives G41 throughout; a drawing that reaches
    /// the post wound the other way (built in code or edited by hand) needs G42.
    /// Paths that enclose no area (open break cuts) count as holes with G41,
    /// a defensive default.
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
        /// Index of the part's perimeter: the contour enclosing the largest
        /// bounding box, as ShapeProfile picks it. -1 when no contour encloses
        /// any area.
        /// </summary>
        public static int PerimeterIndex(IReadOnlyList<CIFiberContour> contours)
        {
            var best = -1;
            var bestArea = 0.0;
            for (var i = 0; i < contours.Count; i++)
            {
                if (!EnclosesArea(contours[i]))
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
        /// True when G41 (product right of travel) keeps the kerf in the scrap:
        /// a CW perimeter or a CCW hole. False calls for G42.
        /// </summary>
        public static bool CompensatesLeft(CIFiberContour contour, bool isPerimeter)
        {
            if (!EnclosesArea(contour))
                return true;

            var clockwise = SignedArea(contour) < 0.0;
            return clockwise == isPerimeter;
        }

        private static bool EnclosesArea(CIFiberContour contour) =>
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

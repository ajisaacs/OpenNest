using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Signed clearance between two closed polygons, plus the unit direction that
    /// increases it by moving the first polygon.
    /// </summary>
    public struct ClearanceResult
    {
        /// <summary>
        /// &gt; 0: minimum boundary distance. 0: touching. &lt; 0: penetration depth
        /// (the translation of <c>a</c> along <see cref="Direction"/> needed to end
        /// contact).
        /// </summary>
        public double Distance;

        /// <summary>
        /// Unit direction for translating <c>a</c> away from <c>b</c>. For penetration
        /// this is the minimum-translation direction. Never zero-length; degenerate
        /// (coincident-centroid) penetration resolves to a deterministic axis.
        /// </summary>
        public Vector Direction;

        public ClearanceResult(double distance, Vector direction)
        {
            Distance = distance;
            Direction = direction;
        }
    }

    /// <summary>
    /// Omnidirectional clearance between two closed, lines-only polygons.
    /// Complements <see cref="SpatialQuery.DirectionalDistance"/> (movement along a
    /// fixed ray) with the all-directions minimum distance and separating direction,
    /// and <see cref="Collision"/> (boolean overlap) with depth and direction.
    /// <para>
    /// Reference quality, not hot-loop quality: separation is a brute-force
    /// segment-pair minimum with a bounding-box reject, penetration is a
    /// separating-axis sweep over both polygons' edge normals. The overlap verdict
    /// defers to <see cref="Collision.HasOverlap(Polygon, Polygon, List{Polygon}, List{Polygon})"/>
    /// so callers that validate with Collision never see a disagreeing kernel.
    /// Rings with holes are handled by the caller: pass every ring pair (a part's
    /// material boundary is its outer ring plus its hole rings).
    /// </para>
    /// </summary>
    public static class Clearance
    {
        public static ClearanceResult Between(Polygon a, Polygon b)
        {
            var linesA = a.ToLines();
            var linesB = b.ToLines();

            if (linesA.Count == 0 || linesB.Count == 0)
                return new ClearanceResult(0, new Vector(1, 0));

            if (Collision.HasOverlap(a, b))
                return Penetration(linesA, linesB);

            return Separation(linesA, linesB);
        }

        /// <summary>
        /// Minimum boundary distance between two non-overlapping rings and the
        /// direction that translates <paramref name="linesA"/> away from
        /// <paramref name="linesB"/> at the closest contact.
        /// </summary>
        private static ClearanceResult Separation(List<Line> linesA, List<Line> linesB)
        {
            var minDist = double.MaxValue;
            var pa = Vector.Zero;
            var pb = Vector.Zero;

            var boxes = new Box[linesB.Count];
            for (var i = 0; i < linesB.Count; i++)
                boxes[i] = SegmentBox(linesB[i]);

            foreach (var la in linesA)
            {
                var boxA = SegmentBox(la);

                for (var i = 0; i < linesB.Count; i++)
                {
                    if (!BoxesWithin(boxA, boxes[i], minDist))
                        continue;

                    var d = SegmentDistance(la, linesB[i], out var qa, out var qb);
                    if (d < minDist)
                    {
                        minDist = d;
                        pa = qa;
                        pb = qb;
                    }
                }
            }

            var dir = pa - pb;
            var len = Magnitude(dir);

            if (len <= Tolerance.Epsilon)
                dir = CentroidAway(linesA, linesB);
            else
                dir = dir / len;

            return new ClearanceResult(minDist, dir);
        }

        /// <summary>
        /// Penetration depth and minimum-translation direction along the separating-
        /// axis candidates of both rings. Per candidate axis the true translation
        /// depth is used (exit distance to the far side), so containment reports the
        /// depth that actually ends contact, not the interval-intersection length.
        /// Depth is reported as a negative clearance.
        /// </summary>
        private static ClearanceResult Penetration(List<Line> linesA, List<Line> linesB)
        {
            var ca = Centroid(linesA);
            var cb = Centroid(linesB);

            var bestDepth = double.MaxValue;
            var bestDir = new Vector(1, 0);

            var bestAxis = -1;

            for (var axis = 0; axis < 2; axis++)
            {
                var lines = axis == 0 ? linesA : linesB;

                foreach (var line in lines)
                {
                    var edge = line.pt2 - line.pt1;
                    var n = new Vector(edge.Y, -edge.X);
                    var len = Magnitude(n);
                    if (len <= Tolerance.Epsilon)
                        continue;
                    n = n / len;

                    var (minA, maxA) = Project(linesA, n);
                    var (minB, maxB) = Project(linesB, n);

                    if (maxA <= minB || maxB <= minA)
                        continue; // separating axis found

                    // Depth pushing a away from b along ±n.
                    var forward = maxB - minA; // move a in +n until minA >= maxB
                    var backward = maxA - minB; // move a in -n until maxA <= minB

                    double depth;
                    Vector dir;
                    if (forward <= backward)
                    {
                        depth = forward;
                        dir = n;
                    }
                    else
                    {
                        depth = backward;
                        dir = -n;
                    }

                    if (depth < bestDepth - Tolerance.Epsilon || bestAxis < 0)
                    {
                        bestDepth = depth;
                        bestDir = dir;
                        bestAxis = axis;
                    }
                }
            }

            if (bestAxis < 0)
            {
                // No candidate axis (degenerate rings): deterministic fallback.
                var away = ca - cb;
                var len = Magnitude(away);
                bestDir = len > Tolerance.Epsilon ? away / len : new Vector(1, 0);
                bestDepth = 0;
            }

            return new ClearanceResult(-bestDepth, bestDir);
        }

        private static Vector CentroidAway(List<Line> linesA, List<Line> linesB)
        {
            var away = Centroid(linesA) - Centroid(linesB);
            var len = Magnitude(away);
            return len > Tolerance.Epsilon ? away / len : new Vector(1, 0);
        }

        private static Vector Centroid(List<Line> lines)
        {
            var sum = Vector.Zero;
            foreach (var line in lines)
            {
                sum += line.pt1;
                sum += line.pt2;
            }
            return sum / (2 * lines.Count);
        }

        private static (double Min, double Max) Project(List<Line> lines, Vector n)
        {
            var min = double.MaxValue;
            var max = double.MinValue;

            foreach (var line in lines)
            {
                var d1 = line.pt1.DotProduct(n);
                var d2 = line.pt2.DotProduct(n);
                if (d1 < min)
                    min = d1;
                if (d1 > max)
                    max = d1;
                if (d2 < min)
                    min = d2;
                if (d2 > max)
                    max = d2;
            }

            return (min, max);
        }

        /// <summary>
        /// Minimum distance between two segments with the closest points.
        /// Non-parallel segments use the classic clamped closest-point solve;
        /// (near-)parallel segments fall back to the four endpoint-to-segment
        /// distances, which is where the minimum always lies.
        /// </summary>
        private static double SegmentDistance(Line a, Line b, out Vector pa, out Vector pb)
        {
            var p = a.pt1;
            var r = a.pt2 - a.pt1;
            var q = b.pt1;
            var s = b.pt2 - b.pt1;

            var rxr = r.DotProduct(r);
            var sxs = s.DotProduct(s);
            var rxs = r.DotProduct(s);

            const double eps = 1e-12;

            var denom = rxr * sxs - rxs * rxs;
            if (denom > eps && rxr > eps && sxs > eps)
            {
                // Minimize |(p + r t) - (q + s u)|^2; setting both partials to
                // zero and solving (Cramer) with d0 = p - q:
                //   t = ((r.s)(d0.s) - (d0.r)(s.s)) / (rr.ss - (r.s)^2)
                //   u = ((r.r)(d0.s) - (r.s)(d0.r)) / (rr.ss - (r.s)^2)
                var d0 = p - q;
                var d0r = d0.DotProduct(r);
                var d0s = d0.DotProduct(s);

                var t = Clamp((rxs * d0s - d0r * sxs) / denom, 0, 1);
                var u = Clamp((rxs * t + d0s) / sxs, 0, 1); // nearest u on b for clamped t
                t = Clamp((rxs * u - d0r) / rxr, 0, 1); // re-solve t for clamped u

                pa = p + r * t;
                pb = q + s * u;
                return pa.DistanceTo(pb);
            }

            // Degenerate or parallel: the minimum is attained at an endpoint.
            var bestPa = p;
            var bestPb = q;
            var best = double.MaxValue;

            void Consider(Vector pt, Line seg, bool ptOnA)
            {
                var d = seg.pt2 - seg.pt1;
                var len2 = d.DotProduct(d);
                var u = len2 <= eps ? 0 : Clamp((pt - seg.pt1).DotProduct(d) / len2, 0, 1);
                var on = seg.pt1 + d * u;
                var dist = pt.DistanceTo(on);
                if (dist < best)
                {
                    best = dist;
                    bestPa = ptOnA ? pt : on;
                    bestPb = ptOnA ? on : pt;
                }
            }

            Consider(p, b, true);
            Consider(a.pt2, b, true);
            Consider(q, a, false);
            Consider(b.pt2, a, false);

            pa = bestPa;
            pb = bestPb;
            return best;
        }

        private static double Clamp(double v, double lo, double hi) =>
            v < lo ? lo : (v > hi ? hi : v);

        private static double Magnitude(Vector v) => System.Math.Sqrt(v.X * v.X + v.Y * v.Y);

        private static Box SegmentBox(Line line)
        {
            return new Box(
                System.Math.Min(line.pt1.X, line.pt2.X),
                System.Math.Min(line.pt1.Y, line.pt2.Y),
                System.Math.Abs(line.pt2.X - line.pt1.X),
                System.Math.Abs(line.pt2.Y - line.pt1.Y)
            );
        }

        private static bool BoxesWithin(Box a, Box b, double distance)
        {
            return !(
                a.Right + distance < b.Left
                || b.Right + distance < a.Left
                || a.Top + distance < b.Bottom
                || b.Top + distance < a.Bottom
            );
        }
    }
}

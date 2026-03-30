using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Shared arc-fitting utilities used by SplineConverter and GeometrySimplifier.
    /// </summary>
    internal static class ArcFit
    {
        /// <summary>
        /// Fits a circular arc constrained to be tangent to the given direction at the
        /// first point. The center lies at the intersection of the normal at P1 (perpendicular
        /// to the tangent) and the perpendicular bisector of the chord P1->Pn, guaranteeing
        /// the arc passes through both endpoints and departs P1 in the given direction.
        /// </summary>
        internal static (Vector center, double radius, double deviation) FitWithStartTangent(
            List<Vector> points, Vector tangent)
        {
            if (points.Count < 3)
                return (Vector.Invalid, 0, double.MaxValue);

            var p1 = points[0];
            var pn = points[^1];

            var mx = (p1.X + pn.X) / 2;
            var my = (p1.Y + pn.Y) / 2;
            var dx = pn.X - p1.X;
            var dy = pn.Y - p1.Y;
            var chordLen = System.Math.Sqrt(dx * dx + dy * dy);
            if (chordLen < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var bx = -dy / chordLen;
            var by = dx / chordLen;

            var tLen = System.Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
            if (tLen < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var nx = -tangent.Y / tLen;
            var ny = tangent.X / tLen;

            var det = nx * by - ny * bx;
            if (System.Math.Abs(det) < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var s = ((mx - p1.X) * by - (my - p1.Y) * bx) / det;

            var cx = p1.X + s * nx;
            var cy = p1.Y + s * ny;
            var radius = System.Math.Sqrt((cx - p1.X) * (cx - p1.X) + (cy - p1.Y) * (cy - p1.Y));

            if (radius < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            return (new Vector(cx, cy), radius, MaxRadialDeviation(points, cx, cy, radius));
        }

        /// <summary>
        /// Fits a circular arc constrained to be tangent to the given directions at both
        /// the first and last points. The center lies at the intersection of the normals
        /// at P1 and Pn, guaranteeing the arc departs P1 in the start direction and arrives
        /// at Pn in the end direction. Uses the radius from P1 (exact start tangent);
        /// deviation includes any endpoint gap at Pn.
        /// </summary>
        internal static (Vector center, double radius, double deviation) FitWithDualTangent(
            List<Vector> points, Vector startTangent, Vector endTangent)
        {
            if (points.Count < 3)
                return (Vector.Invalid, 0, double.MaxValue);

            var p1 = points[0];
            var pn = points[^1];

            var stLen = System.Math.Sqrt(startTangent.X * startTangent.X + startTangent.Y * startTangent.Y);
            var etLen = System.Math.Sqrt(endTangent.X * endTangent.X + endTangent.Y * endTangent.Y);
            if (stLen < 1e-10 || etLen < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            // Normal to start tangent at P1 (perpendicular)
            var n1x = -startTangent.Y / stLen;
            var n1y = startTangent.X / stLen;

            // Normal to end tangent at Pn
            var n2x = -endTangent.Y / etLen;
            var n2y = endTangent.X / etLen;

            // Solve: P1 + t1*N1 = Pn + t2*N2
            var det = n1x * (-n2y) - (-n2x) * n1y;
            if (System.Math.Abs(det) < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var dx = pn.X - p1.X;
            var dy = pn.Y - p1.Y;
            var t1 = (dx * (-n2y) - (-n2x) * dy) / det;

            var cx = p1.X + t1 * n1x;
            var cy = p1.Y + t1 * n1y;

            // Use radius from P1 (guarantees exact start tangent and passes through P1)
            var r1 = System.Math.Sqrt((cx - p1.X) * (cx - p1.X) + (cy - p1.Y) * (cy - p1.Y));
            if (r1 < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            // Measure endpoint gap at Pn
            var r2 = System.Math.Sqrt((cx - pn.X) * (cx - pn.X) + (cy - pn.Y) * (cy - pn.Y));
            var endpointDev = System.Math.Abs(r2 - r1);

            var interiorDev = MaxRadialDeviation(points, cx, cy, r1);
            return (new Vector(cx, cy), r1, System.Math.Max(endpointDev, interiorDev));
        }

        /// <summary>
        /// Computes the maximum radial deviation of interior points from a circle.
        /// </summary>
        internal static double MaxRadialDeviation(List<Vector> points, double cx, double cy, double radius)
        {
            var maxDev = 0.0;
            for (var i = 1; i < points.Count - 1; i++)
            {
                var px = points[i].X - cx;
                var py = points[i].Y - cy;
                var dist = System.Math.Sqrt(px * px + py * py);
                var dev = System.Math.Abs(dist - radius);
                if (dev > maxDev) maxDev = dev;
            }
            return maxDev;
        }
    }
}

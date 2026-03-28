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

using OpenNest.Math;
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class EllipseConverter
    {
        private const int MaxSubdivisionDepth = 12;
        private const int DeviationSamples = 20;

        internal static Vector EvaluatePoint(double semiMajor, double semiMinor, double rotation, Vector center, double t)
        {
            var x = semiMajor * System.Math.Cos(t);
            var y = semiMinor * System.Math.Sin(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                center.X + x * cos - y * sin,
                center.Y + x * sin + y * cos);
        }

        internal static Vector EvaluateTangent(double semiMajor, double semiMinor, double rotation, double t)
        {
            var tx = -semiMajor * System.Math.Sin(t);
            var ty = semiMinor * System.Math.Cos(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                tx * cos - ty * sin,
                tx * sin + ty * cos);
        }

        internal static Vector EvaluateNormal(double semiMajor, double semiMinor, double rotation, double t)
        {
            // Inward normal: perpendicular to tangent, pointing toward center of curvature.
            // In local coords: N(t) = (-b*cos(t), -a*sin(t))
            var nx = -semiMinor * System.Math.Cos(t);
            var ny = -semiMajor * System.Math.Sin(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                nx * cos - ny * sin,
                nx * sin + ny * cos);
        }

        internal static Vector IntersectNormals(Vector p1, Vector n1, Vector p2, Vector n2)
        {
            // Solve: p1 + s*n1 = p2 + t*n2
            var det = n1.X * (-n2.Y) - (-n2.X) * n1.Y;
            if (System.Math.Abs(det) < 1e-10)
                return Vector.Invalid;

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var s = (dx * (-n2.Y) - dy * (-n2.X)) / det;

            return new Vector(p1.X + s * n1.X, p1.Y + s * n1.Y);
        }
    }
}

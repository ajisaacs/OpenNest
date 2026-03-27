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

        public static List<Entity> Convert(Vector center, double semiMajor, double semiMinor,
            double rotation, double startParam, double endParam, double tolerance = 0.001)
        {
            if (tolerance <= 0)
                throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be positive.");
            if (semiMajor <= 0 || semiMinor <= 0)
                throw new ArgumentOutOfRangeException("Semi-axis lengths must be positive.");

            if (endParam <= startParam)
                endParam += Angle.TwoPI;

            // True circle — emit a single arc (or two for full circle)
            if (System.Math.Abs(semiMajor - semiMinor) < Tolerance.Epsilon)
                return ConvertCircle(center, semiMajor, rotation, startParam, endParam);

            var splits = GetInitialSplits(startParam, endParam);

            var entities = new List<Entity>();
            for (var i = 0; i < splits.Count - 1; i++)
                FitSegment(center, semiMajor, semiMinor, rotation,
                    splits[i], splits[i + 1], tolerance, entities, 0);

            return entities;
        }

        private static List<Entity> ConvertCircle(Vector center, double radius,
            double rotation, double startParam, double endParam)
        {
            var sweep = endParam - startParam;
            var isFull = System.Math.Abs(sweep - Angle.TwoPI) < 0.01;

            if (isFull)
            {
                var startAngle1 = Angle.NormalizeRad(startParam + rotation);
                var midAngle = Angle.NormalizeRad(startParam + System.Math.PI + rotation);
                var endAngle2 = startAngle1;

                return new List<Entity>
                {
                    new Arc(center, radius, startAngle1, midAngle, false),
                    new Arc(center, radius, midAngle, endAngle2, false)
                };
            }

            var sa = Angle.NormalizeRad(startParam + rotation);
            var ea = Angle.NormalizeRad(endParam + rotation);
            return new List<Entity> { new Arc(center, radius, sa, ea, false) };
        }

        private static List<double> GetInitialSplits(double startParam, double endParam)
        {
            var splits = new List<double> { startParam };

            var firstQuadrant = System.Math.Ceiling(startParam / (System.Math.PI / 2)) * (System.Math.PI / 2);
            for (var q = firstQuadrant; q < endParam; q += System.Math.PI / 2)
            {
                if (q > startParam + 1e-10 && q < endParam - 1e-10)
                    splits.Add(q);
            }

            splits.Add(endParam);
            return splits;
        }

        private static void FitSegment(Vector center, double semiMajor, double semiMinor,
            double rotation, double t0, double t1, double tolerance, List<Entity> results, int depth)
        {
            var p0 = EvaluatePoint(semiMajor, semiMinor, rotation, center, t0);
            var p1 = EvaluatePoint(semiMajor, semiMinor, rotation, center, t1);

            if (p0.DistanceTo(p1) < 1e-10)
                return;

            var n0 = EvaluateNormal(semiMajor, semiMinor, rotation, t0);
            var n1 = EvaluateNormal(semiMajor, semiMinor, rotation, t1);

            var arcCenter = IntersectNormals(p0, n0, p1, n1);

            if (!arcCenter.IsValid() || depth >= MaxSubdivisionDepth)
            {
                results.Add(new Line(p0, p1));
                return;
            }

            var radius = p0.DistanceTo(arcCenter);
            var maxDev = MeasureDeviation(center, semiMajor, semiMinor, rotation,
                t0, t1, arcCenter, radius);

            if (maxDev <= tolerance)
            {
                results.Add(CreateArc(arcCenter, radius, center, semiMajor, semiMinor, rotation, t0, t1));
            }
            else
            {
                var tMid = (t0 + t1) / 2.0;
                FitSegment(center, semiMajor, semiMinor, rotation, t0, tMid, tolerance, results, depth + 1);
                FitSegment(center, semiMajor, semiMinor, rotation, tMid, t1, tolerance, results, depth + 1);
            }
        }

        private static double MeasureDeviation(Vector center, double semiMajor, double semiMinor,
            double rotation, double t0, double t1, Vector arcCenter, double radius)
        {
            var maxDev = 0.0;
            for (var i = 1; i <= DeviationSamples; i++)
            {
                var t = t0 + (t1 - t0) * i / DeviationSamples;
                var p = EvaluatePoint(semiMajor, semiMinor, rotation, center, t);
                var dist = p.DistanceTo(arcCenter);
                var dev = System.Math.Abs(dist - radius);
                if (dev > maxDev) maxDev = dev;
            }
            return maxDev;
        }

        private static Arc CreateArc(Vector arcCenter, double radius,
            Vector ellipseCenter, double semiMajor, double semiMinor, double rotation,
            double t0, double t1)
        {
            var p0 = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t0);
            var p1 = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t1);

            var startAngle = System.Math.Atan2(p0.Y - arcCenter.Y, p0.X - arcCenter.X);
            var endAngle = System.Math.Atan2(p1.Y - arcCenter.Y, p1.X - arcCenter.X);

            var pMid = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, (t0 + t1) / 2);
            var points = new List<Vector> { p0, pMid, p1 };
            var isReversed = SumSignedAngles(arcCenter, points) < 0;

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            return new Arc(arcCenter, radius, startAngle, endAngle, isReversed);
        }

        private static double SumSignedAngles(Vector center, List<Vector> points)
        {
            var total = 0.0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a1 = System.Math.Atan2(points[i].Y - center.Y, points[i].X - center.X);
                var a2 = System.Math.Atan2(points[i + 1].Y - center.Y, points[i + 1].X - center.X);
                var da = a2 - a1;
                while (da > System.Math.PI) da -= Angle.TwoPI;
                while (da < -System.Math.PI) da += Angle.TwoPI;
                total += da;
            }
            return total;
        }
    }
}

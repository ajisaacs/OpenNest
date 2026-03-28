using OpenNest.Math;
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class SplineConverter
    {
        private const int MinPointsForArc = 3;

        public static List<Entity> Convert(List<Vector> points, bool isClosed, double tolerance = 0.001)
        {
            if (points == null || points.Count < 2)
                return new List<Entity>();

            var entities = new List<Entity>();
            var i = 0;
            var chainedTangent = Vector.Invalid;

            while (i < points.Count - 1)
            {
                var result = TryFitArc(points, i, chainedTangent, tolerance);
                if (result != null)
                {
                    entities.Add(result.Arc);
                    chainedTangent = result.EndTangent;
                    i = result.EndIndex;
                }
                else
                {
                    entities.Add(new Line(points[i], points[i + 1]));
                    chainedTangent = Vector.Invalid;
                    i++;
                }
            }

            return entities;
        }

        private static ArcFitResult TryFitArc(List<Vector> points, int start,
            Vector chainedTangent, double tolerance)
        {
            var minEnd = start + MinPointsForArc - 1;
            if (minEnd >= points.Count)
                return null;

            var hasTangent = chainedTangent.IsValid();

            var subPoints = points.GetRange(start, MinPointsForArc);
            var (center, radius, dev) = hasTangent
                ? FitWithStartTangent(subPoints, chainedTangent)
                : FitCircumscribed(subPoints);

            if (!center.IsValid() || dev > tolerance)
                return null;

            var endIdx = minEnd;
            while (endIdx + 1 < points.Count)
            {
                var extPoints = points.GetRange(start, endIdx + 1 - start + 1);
                var (nc, nr, nd) = hasTangent
                    ? FitWithStartTangent(extPoints, chainedTangent)
                    : FitCircumscribed(extPoints);

                if (!nc.IsValid() || nd > tolerance)
                    break;

                endIdx++;
                center = nc;
                radius = nr;
                dev = nd;
            }

            var finalPoints = points.GetRange(start, endIdx - start + 1);
            var sweep = System.Math.Abs(SumSignedAngles(center, finalPoints));
            if (sweep < Angle.ToRadians(5))
                return null;

            var arc = CreateArc(center, radius, finalPoints);
            var endTangent = ComputeEndTangent(center, finalPoints);

            return new ArcFitResult(arc, endTangent, endIdx);
        }

        private static (Vector center, double radius, double deviation) FitCircumscribed(
            List<Vector> points)
        {
            if (points.Count < 3)
                return (Vector.Invalid, 0, double.MaxValue);

            var p0 = points[0];
            var pMid = points[points.Count / 2];
            var pEnd = points[^1];

            // Find circumcenter by intersecting perpendicular bisectors of two chords
            var (center, radius) = Circumcenter(p0, pMid, pEnd);
            if (!center.IsValid())
                return (Vector.Invalid, 0, double.MaxValue);

            return (center, radius, MaxRadialDeviation(points, center.X, center.Y, radius));
        }

        private static (Vector center, double radius) Circumcenter(Vector a, Vector b, Vector c)
        {
            // Perpendicular bisector of chord a-b
            var m1x = (a.X + b.X) / 2;
            var m1y = (a.Y + b.Y) / 2;
            var d1x = -(b.Y - a.Y);
            var d1y = b.X - a.X;

            // Perpendicular bisector of chord b-c
            var m2x = (b.X + c.X) / 2;
            var m2y = (b.Y + c.Y) / 2;
            var d2x = -(c.Y - b.Y);
            var d2y = c.X - b.X;

            var det = d1x * d2y - d1y * d2x;
            if (System.Math.Abs(det) < 1e-10)
                return (Vector.Invalid, 0);

            var t = ((m2x - m1x) * d2y - (m2y - m1y) * d2x) / det;

            var cx = m1x + t * d1x;
            var cy = m1y + t * d1y;
            var radius = System.Math.Sqrt((cx - a.X) * (cx - a.X) + (cy - a.Y) * (cy - a.Y));

            if (radius < 1e-10)
                return (Vector.Invalid, 0);

            return (new Vector(cx, cy), radius);
        }

        private static (Vector center, double radius, double deviation) FitWithStartTangent(
            List<Vector> points, Vector tangent) =>
            ArcFit.FitWithStartTangent(points, tangent);

        private static double MaxRadialDeviation(List<Vector> points, double cx, double cy, double radius) =>
            ArcFit.MaxRadialDeviation(points, cx, cy, radius);

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

        private static Vector ComputeEndTangent(Vector center, List<Vector> points)
        {
            var lastPt = points[^1];
            var totalAngle = SumSignedAngles(center, points);

            var rx = lastPt.X - center.X;
            var ry = lastPt.Y - center.Y;

            return totalAngle >= 0
                ? new Vector(-ry, rx)
                : new Vector(ry, -rx);
        }

        private static Arc CreateArc(Vector center, double radius, List<Vector> points)
        {
            var firstPoint = points[0];
            var lastPoint = points[^1];

            var startAngle = System.Math.Atan2(firstPoint.Y - center.Y, firstPoint.X - center.X);
            var endAngle = System.Math.Atan2(lastPoint.Y - center.Y, lastPoint.X - center.X);
            var isReversed = SumSignedAngles(center, points) < 0;

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            return new Arc(center, radius, startAngle, endAngle, isReversed);
        }

        private sealed class ArcFitResult
        {
            public Arc Arc { get; }
            public Vector EndTangent { get; }
            public int EndIndex { get; }

            public ArcFitResult(Arc arc, Vector endTangent, int endIndex)
            {
                Arc = arc;
                EndTangent = endTangent;
                EndIndex = endIndex;
            }
        }
    }
}

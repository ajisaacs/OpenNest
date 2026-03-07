using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public class BoundingRectangleResult
    {
        public BoundingRectangleResult(double angle, double width, double height)
        {
            Angle = angle;
            Width = width;
            Height = height;
            Area = width * height;
        }

        public double Angle { get; }
        public double Width { get; }
        public double Height { get; }
        public double Area { get; }
    }

    public static class RotatingCalipers
    {
        public static BoundingRectangleResult MinimumBoundingRectangle(IList<Vector> points)
        {
            var hull = ConvexHull.Compute(points);
            return MinimumBoundingRectangle(hull);
        }

        public static BoundingRectangleResult MinimumBoundingRectangle(Polygon hull)
        {
            var vertices = hull.Vertices;
            int n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;

            if (n == 0)
                return new BoundingRectangleResult(0, 0, 0);

            if (n == 1)
                return new BoundingRectangleResult(0, 0, 0);

            if (n == 2)
            {
                var dx = vertices[1].X - vertices[0].X;
                var dy = vertices[1].Y - vertices[0].Y;
                var angle = System.Math.Atan2(dy, dx);
                var length = System.Math.Sqrt(dx * dx + dy * dy);
                return new BoundingRectangleResult(angle, length, 0);
            }

            BoundingRectangleResult best = null;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                var edgeX = vertices[next].X - vertices[i].X;
                var edgeY = vertices[next].Y - vertices[i].Y;
                var edgeLen = System.Math.Sqrt(edgeX * edgeX + edgeY * edgeY);

                if (edgeLen.IsEqualTo(0))
                    continue;

                // Unit vectors along and perpendicular to this edge
                var ux = edgeX / edgeLen;
                var uy = edgeY / edgeLen;
                var vx = -uy;
                var vy = ux;

                // Project all hull vertices onto edge direction (u) and perpendicular (v)
                double minU = double.MaxValue, maxU = double.MinValue;
                double minV = double.MaxValue, maxV = double.MinValue;

                for (int j = 0; j < n; j++)
                {
                    var projU = vertices[j].X * ux + vertices[j].Y * uy;
                    var projV = vertices[j].X * vx + vertices[j].Y * vy;

                    if (projU < minU) minU = projU;
                    if (projU > maxU) maxU = projU;
                    if (projV < minV) minV = projV;
                    if (projV > maxV) maxV = projV;
                }

                var width = maxU - minU;
                var height = maxV - minV;
                var area = width * height;

                if (best == null || area < best.Area)
                {
                    var edgeAngle = System.Math.Atan2(edgeY, edgeX);
                    best = new BoundingRectangleResult(edgeAngle, width, height);
                }
            }

            return best ?? new BoundingRectangleResult(0, 0, 0);
        }

        public static BoundingRectangleResult MinimumBoundingRectangle(Polygon hull, double startAngle, double endAngle)
        {
            var vertices = hull.Vertices;
            int n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;

            if (n < 3)
                return MinimumBoundingRectangle(hull);

            startAngle = Angle.NormalizeRad(startAngle);
            if (!endAngle.IsEqualTo(Angle.TwoPI))
                endAngle = Angle.NormalizeRad(endAngle);

            var candidates = new List<BoundingRectangleResult>();

            // Evaluate at each hull edge angle
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                var edgeX = vertices[next].X - vertices[i].X;
                var edgeY = vertices[next].Y - vertices[i].Y;
                var edgeLen = System.Math.Sqrt(edgeX * edgeX + edgeY * edgeY);

                if (edgeLen.IsEqualTo(0))
                    continue;

                var edgeAngle = Angle.NormalizeRad(System.Math.Atan2(edgeY, edgeX));

                // Each edge produces 4 candidate angles (0, 90, 180, 270 relative to edge)
                for (int k = 0; k < 4; k++)
                {
                    var candidateAngle = Angle.NormalizeRad(edgeAngle + k * Angle.HalfPI);

                    if (IsAngleInRange(candidateAngle, startAngle, endAngle))
                        candidates.Add(EvaluateAtAngle(vertices, n, candidateAngle));
                }
            }

            // Also evaluate at range boundaries
            candidates.Add(EvaluateAtAngle(vertices, n, startAngle));

            if (!endAngle.IsEqualTo(Angle.TwoPI))
                candidates.Add(EvaluateAtAngle(vertices, n, endAngle));

            BoundingRectangleResult best = null;

            foreach (var candidate in candidates)
            {
                if (best == null || candidate.Area < best.Area)
                    best = candidate;
            }

            return best ?? new BoundingRectangleResult(startAngle, 0, 0);
        }

        private static BoundingRectangleResult EvaluateAtAngle(IList<Vector> vertices, int n, double angle)
        {
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;

            for (int j = 0; j < n; j++)
            {
                var projU = vertices[j].X * cos + vertices[j].Y * sin;
                var projV = -vertices[j].X * sin + vertices[j].Y * cos;

                if (projU < minU) minU = projU;
                if (projU > maxU) maxU = projU;
                if (projV < minV) minV = projV;
                if (projV > maxV) maxV = projV;
            }

            var width = maxU - minU;
            var height = maxV - minV;
            return new BoundingRectangleResult(angle, width, height);
        }

        private static bool IsAngleInRange(double angle, double start, double end)
        {
            if (end.IsEqualTo(Angle.TwoPI) && start.IsEqualTo(0))
                return true;

            if (start <= end)
                return angle >= start - Tolerance.Epsilon && angle <= end + Tolerance.Epsilon;

            // Range wraps around 0 (e.g., 350 to 10 degrees)
            return angle >= start - Tolerance.Epsilon || angle <= end + Tolerance.Epsilon;
        }
    }
}

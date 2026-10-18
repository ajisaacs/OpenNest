using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Smallest circle containing every point (Welzl's algorithm, expected linear time). Points are
    /// visited in a fixed shuffled order, so the result is repeatable.
    /// </summary>
    public static class MinimumEnclosingCircle
    {
        public static Circle Compute(IEnumerable<Vector> points)
        {
            var shuffled = points.ToList();

            if (shuffled.Count == 0)
                throw new ArgumentException("At least one point is required.", nameof(points));

            var random = new Random(0);

            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var center = shuffled[0];
            var radius = 0.0;

            for (var i = 1; i < shuffled.Count; i++)
            {
                if (Contains(center, radius, shuffled[i]))
                    continue;

                center = shuffled[i];
                radius = 0;

                for (var j = 0; j < i; j++)
                {
                    if (Contains(center, radius, shuffled[j]))
                        continue;

                    center = (shuffled[i] + shuffled[j]) * 0.5;
                    radius = center.DistanceTo(shuffled[i]);

                    for (var k = 0; k < j; k++)
                    {
                        if (Contains(center, radius, shuffled[k]))
                            continue;

                        (center, radius) = Circumcircle(shuffled[i], shuffled[j], shuffled[k]);
                    }
                }
            }

            // Rounding can leave a defining point a hair outside; grow to cover every point exactly.
            radius = shuffled.Max(p => p.DistanceTo(center));

            return new Circle(center, radius);
        }

        private static bool Contains(Vector center, double radius, Vector point) =>
            point.DistanceTo(center) <= radius * (1 + 1e-12) + 1e-12;

        private static (Vector Center, double Radius) Circumcircle(Vector a, Vector b, Vector c)
        {
            var bx = b.X - a.X;
            var by = b.Y - a.Y;
            var cx = c.X - a.X;
            var cy = c.Y - a.Y;
            var d = 2 * (bx * cy - by * cx);

            if (System.Math.Abs(d) <= double.Epsilon)
            {
                // Collinear: the circle on the two farthest-apart points covers the third.
                var pairs = new[] { (a, b), (a, c), (b, c) };
                var (p, q) = pairs.OrderByDescending(pair => pair.Item1.DistanceTo(pair.Item2)).First();
                var mid = (p + q) * 0.5;
                return (mid, mid.DistanceTo(p));
            }

            var b2 = bx * bx + by * by;
            var c2 = cx * cx + cy * cy;
            var center = new Vector(a.X + (cy * b2 - by * c2) / d, a.Y + (bx * c2 - cx * b2) / d);

            return (center, center.DistanceTo(a));
        }
    }
}

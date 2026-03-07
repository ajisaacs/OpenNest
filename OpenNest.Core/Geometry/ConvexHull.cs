using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    public static class ConvexHull
    {
        public static Polygon Compute(IList<Vector> points)
        {
            if (points.Count < 3)
            {
                var result = new Polygon();
                result.Vertices.AddRange(points);
                return result;
            }

            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

            var lower = new List<Vector>();

            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);

                lower.Add(p);
            }

            var upper = new List<Vector>();

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                var p = sorted[i];

                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);

                upper.Add(p);
            }

            // Remove last point of each half because it's repeated
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            lower.AddRange(upper);

            var hull = new Polygon();
            hull.Vertices.AddRange(lower);
            hull.Close();
            hull.UpdateBounds();

            return hull;
        }

        private static double Cross(Vector o, Vector a, Vector b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }
    }
}

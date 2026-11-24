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

            // By X, then Y, equal points in their given order: the order a stable sort on
            // those two keys gives. The position breaks ties, so no two entries compare equal
            // and the unstable array sort has only one result. The buffer is kept per thread.
            var count = points.Count;
            var sorted = sortBuffer;

            if (sorted == null || sorted.Length < count)
                sortBuffer = sorted = new SortedPoint[System.Math.Max(count, 64)];

            for (var i = 0; i < count; i++)
                sorted[i] = new SortedPoint(points[i], i);

            System.Array.Sort(sorted, 0, count);

            var lower = new List<Vector>();

            for (var i = 0; i < count; i++)
            {
                var p = sorted[i].Point;

                while (
                    lower.Count >= 2
                    && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0
                )
                    lower.RemoveAt(lower.Count - 1);

                lower.Add(p);
            }

            var upper = new List<Vector>();

            for (int i = count - 1; i >= 0; i--)
            {
                var p = sorted[i].Point;

                while (
                    upper.Count >= 2
                    && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0
                )
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

        [System.ThreadStatic]
        private static SortedPoint[] sortBuffer;

        private readonly struct SortedPoint : System.IComparable<SortedPoint>
        {
            private readonly int position;

            public SortedPoint(Vector point, int position)
            {
                Point = point;
                this.position = position;
            }

            public Vector Point { get; }

            public int CompareTo(SortedPoint other)
            {
                var byX = Point.X.CompareTo(other.Point.X);

                if (byX != 0)
                    return byX;

                var byY = Point.Y.CompareTo(other.Point.Y);
                return byY != 0 ? byY : position.CompareTo(other.position);
            }
        }

        private static double Cross(Vector o, Vector a, Vector b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }
    }
}

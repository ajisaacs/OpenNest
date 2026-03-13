using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Decomposes concave polygons into convex sub-polygons using ear-clipping
    /// triangulation. Produces O(n-2) triangles per polygon.
    /// </summary>
    public static class ConvexDecomposition
    {
        /// <summary>
        /// Decomposes a polygon into a list of convex triangles using ear-clipping.
        /// The input polygon must be simple (non-self-intersecting).
        /// Returns a list of triangles, each represented as a Polygon with 3 vertices (closed).
        /// </summary>
        public static List<Polygon> Triangulate(Polygon polygon)
        {
            var triangles = new List<Polygon>();
            var verts = new List<Vector>(polygon.Vertices);

            // Remove closing vertex if polygon is closed.
            if (verts.Count > 1 && verts[0].X == verts[verts.Count - 1].X
                                 && verts[0].Y == verts[verts.Count - 1].Y)
                verts.RemoveAt(verts.Count - 1);

            if (verts.Count < 3)
                return triangles;

            // Ensure counter-clockwise winding for ear detection.
            if (SignedArea(verts) < 0)
                verts.Reverse();

            // Build a linked list of vertex indices.
            var indices = new List<int>(verts.Count);

            for (var i = 0; i < verts.Count; i++)
                indices.Add(i);

            var n = indices.Count;

            // Safety counter to avoid infinite loop on degenerate polygons.
            var maxIterations = n * n;
            var iterations = 0;
            var i0 = 0;

            while (n > 2 && iterations < maxIterations)
            {
                iterations++;

                var prevIdx = (i0 + n - 1) % n;
                var currIdx = i0 % n;
                var nextIdx = (i0 + 1) % n;

                var prev = verts[indices[prevIdx]];
                var curr = verts[indices[currIdx]];
                var next = verts[indices[nextIdx]];

                if (IsEar(prev, curr, next, verts, indices, n))
                {
                    var tri = new Polygon();
                    tri.Vertices.Add(prev);
                    tri.Vertices.Add(curr);
                    tri.Vertices.Add(next);
                    tri.Close();
                    triangles.Add(tri);

                    indices.RemoveAt(currIdx);
                    n--;
                    i0 = 0;
                }
                else
                {
                    i0++;

                    if (i0 >= n)
                        i0 = 0;
                }
            }

            return triangles;
        }

        /// <summary>
        /// Tests whether the vertex at curr forms an ear (a convex vertex whose
        /// triangle contains no other polygon vertices).
        /// </summary>
        private static bool IsEar(Vector prev, Vector curr, Vector next,
            List<Vector> verts, List<int> indices, int n)
        {
            // Must be convex (CCW turn).
            if (Cross(prev, curr, next) <= 0)
                return false;

            // Check that no other vertex lies inside the triangle.
            for (var i = 0; i < n; i++)
            {
                var v = verts[indices[i]];

                if (v.X == prev.X && v.Y == prev.Y)
                    continue;
                if (v.X == curr.X && v.Y == curr.Y)
                    continue;
                if (v.X == next.X && v.Y == next.Y)
                    continue;

                if (PointInTriangle(v, prev, curr, next))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns positive value if A→B→C is a CCW (left) turn.
        /// </summary>
        internal static double Cross(Vector a, Vector b, Vector c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        /// <summary>
        /// Returns true if point p is strictly inside triangle (a, b, c).
        /// Assumes CCW winding.
        /// </summary>
        private static bool PointInTriangle(Vector p, Vector a, Vector b, Vector c)
        {
            var d1 = Cross(a, b, p);
            var d2 = Cross(b, c, p);
            var d3 = Cross(c, a, p);

            var hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            var hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        /// <summary>
        /// Signed area of a polygon. Positive = CCW, negative = CW.
        /// </summary>
        private static double SignedArea(List<Vector> verts)
        {
            var area = 0.0;

            for (var i = 0; i < verts.Count; i++)
            {
                var j = (i + 1) % verts.Count;
                area += verts[i].X * verts[j].Y;
                area -= verts[j].X * verts[i].Y;
            }

            return area * 0.5;
        }
    }
}

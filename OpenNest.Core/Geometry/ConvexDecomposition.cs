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
            if (
                verts.Count > 1
                && verts[0].X == verts[verts.Count - 1].X
                && verts[0].Y == verts[verts.Count - 1].Y
            )
                verts.RemoveAt(verts.Count - 1);

            if (verts.Count < 3)
                return triangles;

            // Ensure counter-clockwise winding for ear detection.
            if (SignedArea(verts) < 0)
                verts.Reverse();

            // The remaining vertices, in their original order, as a circular linked list: head is
            // the first remaining vertex (scan position 0), and cutting an ear unlinks one vertex
            // instead of shifting every later index down.
            var count = verts.Count;
            var nextOf = new int[count];
            var prevOf = new int[count];
            for (var i = 0; i < count; i++)
            {
                nextOf[i] = (i + 1) % count;
                prevOf[i] = (i + count - 1) % count;
            }

            var head = 0;
            var n = count;

            // The scan restarts at the first remaining vertex after every ear. A vertex that
            // failed the ear test keeps failing it while its two neighbors are unchanged and
            // the vertex found inside its triangle (or none, when it was not convex) remains,
            // since the test depends on nothing else. Remembering why it failed skips repeating
            // the O(n) test; the vertices visited, their verdicts and the ears cut are the same.
            var removed = new bool[count];
            var failedPrev = new int[count];
            var failedNext = new int[count];
            var failedBlocker = new int[count];
            for (var i = 0; i < count; i++)
                failedPrev[i] = -1;

            // Safety counter to avoid infinite loop on degenerate polygons.
            var maxIterations = n * n;
            var iterations = 0;
            var cursor = head;

            while (n > 2 && iterations < maxIterations)
            {
                iterations++;

                var currVertex = cursor;
                var prevVertex = prevOf[currVertex];
                var nextVertex = nextOf[currVertex];

                var knownNotEar =
                    failedPrev[currVertex] == prevVertex
                    && failedNext[currVertex] == nextVertex
                    && (failedBlocker[currVertex] < 0 || !removed[failedBlocker[currVertex]]);

                var prev = verts[prevVertex];
                var curr = verts[currVertex];
                var next = verts[nextVertex];

                var blocker = failedBlocker[currVertex];
                if (!knownNotEar && IsEar(prev, curr, next, verts, nextOf, head, n, out blocker))
                {
                    var tri = new Polygon();
                    tri.Vertices.Add(prev);
                    tri.Vertices.Add(curr);
                    tri.Vertices.Add(next);
                    tri.Close();
                    triangles.Add(tri);

                    nextOf[prevVertex] = nextVertex;
                    prevOf[nextVertex] = prevVertex;
                    removed[currVertex] = true;
                    if (head == currVertex)
                        head = nextVertex;
                    n--;
                    cursor = head;
                }
                else
                {
                    if (!knownNotEar)
                    {
                        failedPrev[currVertex] = prevVertex;
                        failedNext[currVertex] = nextVertex;
                        failedBlocker[currVertex] = blocker;
                    }

                    // The last remaining vertex links back to head, as position n wraps to 0.
                    cursor = nextVertex;
                }
            }

            return triangles;
        }

        /// <summary>
        /// Tests whether the vertex at curr forms an ear (a convex vertex whose
        /// triangle contains no other polygon vertices). A vertex that fails reports the
        /// first remaining vertex found inside its triangle, or -1 when it is not convex.
        /// </summary>
        private static bool IsEar(
            Vector prev,
            Vector curr,
            Vector next,
            List<Vector> verts,
            int[] nextOf,
            int head,
            int n,
            out int blocker
        )
        {
            // The vertex found inside the triangle, or -1 when the vertex is not convex.
            blocker = -1;

            // Must be convex (CCW turn).
            if (Cross(prev, curr, next) <= 0)
                return false;

            // Check that no other vertex lies inside the triangle.
            for (int i = 0, index = head; i < n; i++, index = nextOf[index])
            {
                var v = verts[index];

                if (v.X == prev.X && v.Y == prev.Y)
                    continue;
                if (v.X == curr.X && v.Y == curr.Y)
                    continue;
                if (v.X == next.X && v.Y == next.Y)
                    continue;

                if (PointInTriangle(v, prev, curr, next))
                {
                    blocker = index;
                    return false;
                }
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
            // World-coordinate products can erase the sign of a small polygon's area
            // far from the origin, leaving CW outlines/holes untriangulated. Measure
            // relative to a vertex, just as the clipping kernel measures its fragments.
            var area = 0.0;
            for (var i = 1; i + 1 < verts.Count; i++)
                area += Cross(verts[0], verts[i], verts[i + 1]);

            return area * 0.5;
        }
    }
}

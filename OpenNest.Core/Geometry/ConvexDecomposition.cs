using System.Collections.Generic;
using NumericVector = System.Numerics.Vector;

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
            var sink = new PolygonSink(new List<Polygon>());
            Triangulate(polygon, ref sink);
            return sink.Triangles;
        }

        /// <summary>
        /// Receives each ear <see cref="Triangulate(Polygon)"/> cuts, in order, as the three
        /// vertices it would build its triangle from (previous, ear, next).
        /// </summary>
        internal interface IEarSink
        {
            void Add(Vector prev, Vector curr, Vector next);
        }

        private struct PolygonSink : IEarSink
        {
            public PolygonSink(List<Polygon> triangles) => Triangles = triangles;

            public List<Polygon> Triangles { get; }

            public void Add(Vector prev, Vector curr, Vector next)
            {
                var tri = new Polygon();
                tri.Vertices.Add(prev);
                tri.Vertices.Add(curr);
                tri.Vertices.Add(next);
                tri.Close();
                Triangles.Add(tri);
            }
        }

        /// <summary>
        /// The ear-clipping of <see cref="Triangulate(Polygon)"/>, handing each ear to
        /// <paramref name="sink"/> instead of building a triangle polygon for it: the same ears,
        /// from the same vertices, in the same order.
        /// </summary>
        internal static void Triangulate<TSink>(Polygon polygon, ref TSink sink)
            where TSink : struct, IEarSink
        {
            var source = polygon.Vertices;
            var count = source.Count;

            // Remove closing vertex if polygon is closed.
            if (
                count > 1
                && source[0].X == source[count - 1].X
                && source[0].Y == source[count - 1].Y
            )
                count--;

            if (count < 3)
                return;

            // The working arrays are kept per thread and reused: a triangulation fills the
            // part it needs before reading it, and no sink triangulates in turn.
            var scratch = workspace ??= new Workspace();
            scratch.Reserve(count);

            var verts = scratch.Vertices;
            for (var i = 0; i < count; i++)
                verts[i] = source[i];

            // Ensure counter-clockwise winding for ear detection.
            if (SignedArea(verts, count) < 0)
                System.Array.Reverse(verts, 0, count);

            // The remaining vertices, in their original order, as a circular linked list: cutting
            // an ear unlinks one vertex instead of shifting every later index down.
            var nextOf = scratch.NextOf;
            var prevOf = scratch.PrevOf;
            for (var i = 0; i < count; i++)
            {
                nextOf[i] = (i + 1) % count;
                prevOf[i] = (i + count - 1) % count;
            }

            var n = count;

            // The same remaining vertices packed in that order, with each one's original index,
            // so the ear test reads them straight through instead of following the links.
            var xs = scratch.Xs;
            var ys = scratch.Ys;
            var ids = scratch.Ids;
            for (var i = 0; i < count; i++)
            {
                xs[i] = verts[i].X;
                ys[i] = verts[i].Y;
                ids[i] = i;
            }

            // Ear clipping cuts the first remaining vertex, in order, that is an ear, and after
            // every ear looks again from the first remaining vertex. A vertex that failed the
            // ear test keeps failing it while its two neighbors are unchanged and the vertex
            // found inside its triangle (or none, when it was not convex) remains, since the
            // test depends on nothing else. So only the vertices in 'untested' can be ears: all
            // of them at first, and after an ear its two neighbors and the vertices it blocked.
            // Taking the lowest of those each time tests the same vertices in the same order as
            // walking every remaining vertex from the first, and cuts the same ears.
            var untested = scratch.Untested;
            var words = (count + 63) / 64;
            System.Array.Clear(untested, 0, words);
            for (var i = 0; i < count; i++)
                untested[i >> 6] |= 1UL << i;

            // The failed vertices each vertex blocks, as doubly linked lists threaded through
            // the vertices: a vertex waits on at most one blocker.
            var firstBlocked = scratch.FirstBlocked;
            var blockedBy = scratch.BlockedBy;
            var nextBlocked = scratch.NextBlocked;
            var prevBlocked = scratch.PrevBlocked;
            for (var i = 0; i < count; i++)
            {
                firstBlocked[i] = -1;
                blockedBy[i] = -1;
            }

            while (n > 2)
            {
                var currVertex = LowestSet(untested, words);

                // Every remaining vertex is known not to be an ear: nothing more can be cut.
                if (currVertex < 0)
                    break;

                untested[currVertex >> 6] &= ~(1UL << currVertex);

                // Tested again, so it no longer waits on the vertex that blocked it before.
                if (blockedBy[currVertex] >= 0)
                    Unblock(currVertex, firstBlocked, blockedBy, nextBlocked, prevBlocked);

                var prevVertex = prevOf[currVertex];
                var nextVertex = nextOf[currVertex];
                var prev = verts[prevVertex];
                var curr = verts[currVertex];
                var next = verts[nextVertex];

                if (IsEar(prev, curr, next, xs, ys, ids, n, out var blocker))
                {
                    sink.Add(prev, curr, next);

                    var packed = System.Array.BinarySearch(ids, 0, n, currVertex);
                    var after = n - packed - 1;
                    System.Array.Copy(xs, packed + 1, xs, packed, after);
                    System.Array.Copy(ys, packed + 1, ys, packed, after);
                    System.Array.Copy(ids, packed + 1, ids, packed, after);

                    nextOf[prevVertex] = nextVertex;
                    prevOf[nextVertex] = prevVertex;
                    n--;

                    untested[prevVertex >> 6] |= 1UL << prevVertex;
                    untested[nextVertex >> 6] |= 1UL << nextVertex;

                    for (var waiting = firstBlocked[currVertex]; waiting >= 0; waiting = nextBlocked[waiting])
                    {
                        blockedBy[waiting] = -1;
                        untested[waiting >> 6] |= 1UL << waiting;
                    }

                    firstBlocked[currVertex] = -1;
                }
                else if (blocker >= 0)
                {
                    var first = firstBlocked[blocker];
                    blockedBy[currVertex] = blocker;
                    prevBlocked[currVertex] = -1;
                    nextBlocked[currVertex] = first;
                    if (first >= 0)
                        prevBlocked[first] = currVertex;
                    firstBlocked[blocker] = currVertex;
                }
            }
        }

        [System.ThreadStatic]
        private static Workspace workspace;

        /// <summary>One thread's working arrays, grown to the largest polygon it has triangulated.</summary>
        private sealed class Workspace
        {
            public Vector[] Vertices = System.Array.Empty<Vector>();
            public double[] Xs;
            public double[] Ys;
            public int[] Ids;
            public int[] NextOf;
            public int[] PrevOf;
            public int[] FirstBlocked;
            public int[] BlockedBy;
            public int[] NextBlocked;
            public int[] PrevBlocked;
            public ulong[] Untested;

            public void Reserve(int count)
            {
                if (Vertices.Length >= count)
                    return;

                var size = System.Math.Max(count, Vertices.Length * 2);
                Vertices = new Vector[size];
                Xs = new double[size];
                Ys = new double[size];
                Ids = new int[size];
                NextOf = new int[size];
                PrevOf = new int[size];
                FirstBlocked = new int[size];
                BlockedBy = new int[size];
                NextBlocked = new int[size];
                PrevBlocked = new int[size];
                Untested = new ulong[(size + 63) / 64];
            }
        }

        /// <summary>The lowest set bit's position in the first words, or -1 when none is set.</summary>
        private static int LowestSet(ulong[] bits, int words)
        {
            for (var word = 0; word < words; word++)
            {
                if (bits[word] != 0)
                    return (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits[word]);
            }

            return -1;
        }

        /// <summary>Takes <paramref name="vertex"/> off the list of the vertex that blocks it.</summary>
        private static void Unblock(
            int vertex,
            int[] firstBlocked,
            int[] blockedBy,
            int[] nextBlocked,
            int[] prevBlocked
        )
        {
            var before = prevBlocked[vertex];
            var after = nextBlocked[vertex];

            if (before >= 0)
                nextBlocked[before] = after;
            else
                firstBlocked[blockedBy[vertex]] = after;

            if (after >= 0)
                prevBlocked[after] = before;

            blockedBy[vertex] = -1;
        }

        /// <summary>
        /// Tests whether the vertex at curr forms an ear (a convex vertex whose
        /// triangle contains no other polygon vertices). A vertex that fails reports the
        /// first remaining vertex found inside its triangle, or -1 when it is not convex.
        /// The remaining vertices are the first <paramref name="n"/> of <paramref name="xs"/>,
        /// <paramref name="ys"/> and <paramref name="ids"/>, in scan order.
        /// <para>
        /// Several vertices are tested at a time where the hardware allows. Each lane does the
        /// subtractions, multiplications and comparisons of <see cref="PointInTriangle"/> for
        /// its vertex, in the same order and never fused, so it reaches the same verdict; a
        /// group with a vertex that may block is then walked one by one to report the first.
        /// </para>
        /// </summary>
        private static bool IsEar(
            Vector prev,
            Vector curr,
            Vector next,
            double[] xs,
            double[] ys,
            int[] ids,
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
            var i = 0;

            if (NumericVector.IsHardwareAccelerated)
            {
                var width = System.Numerics.Vector<double>.Count;
                var zero = System.Numerics.Vector<double>.Zero;
                var all = new System.Numerics.Vector<long>(-1);
                var prevX = new System.Numerics.Vector<double>(prev.X);
                var prevY = new System.Numerics.Vector<double>(prev.Y);
                var currX = new System.Numerics.Vector<double>(curr.X);
                var currY = new System.Numerics.Vector<double>(curr.Y);
                var nextX = new System.Numerics.Vector<double>(next.X);
                var nextY = new System.Numerics.Vector<double>(next.Y);

                // The edge vectors Cross forms first: b - a for (prev, curr), (curr, next)
                // and (next, prev).
                var edge1X = new System.Numerics.Vector<double>(curr.X - prev.X);
                var edge1Y = new System.Numerics.Vector<double>(curr.Y - prev.Y);
                var edge2X = new System.Numerics.Vector<double>(next.X - curr.X);
                var edge2Y = new System.Numerics.Vector<double>(next.Y - curr.Y);
                var edge3X = new System.Numerics.Vector<double>(prev.X - next.X);
                var edge3Y = new System.Numerics.Vector<double>(prev.Y - next.Y);

                for (; i <= n - width; i += width)
                {
                    var x = new System.Numerics.Vector<double>(xs, i);
                    var y = new System.Numerics.Vector<double>(ys, i);

                    var d1 = edge1X * (y - prevY) - edge1Y * (x - prevX);
                    var d2 = edge2X * (y - currY) - edge2Y * (x - currX);
                    var d3 = edge3X * (y - nextY) - edge3Y * (x - nextX);

                    var hasNeg =
                        NumericVector.LessThan(d1, zero)
                        | NumericVector.LessThan(d2, zero)
                        | NumericVector.LessThan(d3, zero);
                    var hasPos =
                        NumericVector.GreaterThan(d1, zero)
                        | NumericVector.GreaterThan(d2, zero)
                        | NumericVector.GreaterThan(d3, zero);
                    var corner =
                        (NumericVector.Equals(x, prevX) & NumericVector.Equals(y, prevY))
                        | (NumericVector.Equals(x, currX) & NumericVector.Equals(y, currY))
                        | (NumericVector.Equals(x, nextX) & NumericVector.Equals(y, nextY));

                    // A lane passes when it is one of the triangle's corners or outside it.
                    if (NumericVector.EqualsAll((hasNeg & hasPos) | corner, all))
                        continue;

                    for (var lane = i; lane < i + width; lane++)
                    {
                        if (Blocks(new Vector(xs[lane], ys[lane]), prev, curr, next))
                        {
                            blocker = ids[lane];
                            return false;
                        }
                    }
                }
            }

            for (; i < n; i++)
            {
                if (Blocks(new Vector(xs[i], ys[i]), prev, curr, next))
                {
                    blocker = ids[i];
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a remaining vertex, not one of the triangle's corners, lies inside it.</summary>
        private static bool Blocks(Vector v, Vector prev, Vector curr, Vector next)
        {
            if (v.X == prev.X && v.Y == prev.Y)
                return false;
            if (v.X == curr.X && v.Y == curr.Y)
                return false;
            if (v.X == next.X && v.Y == next.Y)
                return false;

            return PointInTriangle(v, prev, curr, next);
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
        private static double SignedArea(Vector[] verts, int count)
        {
            // World-coordinate products can erase the sign of a small polygon's area
            // far from the origin, leaving CW outlines/holes untriangulated. Measure
            // relative to a vertex, just as the clipping kernel measures its fragments.
            var area = 0.0;
            for (var i = 1; i + 1 < count; i++)
                area += Cross(verts[0], verts[i], verts[i + 1]);

            return area * 0.5;
        }
    }
}

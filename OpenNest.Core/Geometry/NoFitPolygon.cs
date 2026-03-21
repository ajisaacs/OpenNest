using Clipper2Lib;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Computes the No-Fit Polygon (NFP) between two polygons.
    /// The NFP defines all positions where the orbiting polygon's reference point
    /// would cause overlap with the stationary polygon.
    /// </summary>
    public static class NoFitPolygon
    {
        private const double ClipperScale = 1000.0;

        /// <summary>
        /// Computes the NFP between a stationary polygon A and an orbiting polygon B.
        /// NFP(A, B) = Minkowski sum of A and -B (B reflected through its reference point).
        /// </summary>
        public static Polygon Compute(Polygon stationary, Polygon orbiting)
        {
            var reflected = Reflect(orbiting);
            return MinkowskiSum(stationary, reflected);
        }

        /// <summary>
        /// Reflects a polygon through the origin (negates all vertex coordinates).
        /// </summary>
        private static Polygon Reflect(Polygon polygon)
        {
            var result = new Polygon();

            foreach (var v in polygon.Vertices)
                result.Vertices.Add(new Vector(-v.X, -v.Y));

            // Reflecting reverses winding order — reverse to maintain CCW.
            result.Vertices.Reverse();
            return result;
        }

        /// <summary>
        /// Computes the Minkowski sum of two polygons using convex decomposition.
        /// For convex polygons, uses the direct O(n+m) merge-sort of edge vectors.
        /// For concave polygons, decomposes into triangles, computes pairwise
        /// convex Minkowski sums, and unions the results with Clipper2.
        /// </summary>
        private static Polygon MinkowskiSum(Polygon a, Polygon b)
        {
            var trisA = ConvexDecomposition.Triangulate(a);
            var trisB = ConvexDecomposition.Triangulate(b);

            if (trisA.Count == 0 || trisB.Count == 0)
                return new Polygon();

            var partialSums = new List<Polygon>();

            foreach (var ta in trisA)
            {
                foreach (var tb in trisB)
                {
                    var sum = ConvexMinkowskiSum(ta, tb);

                    if (sum.Vertices.Count >= 3)
                        partialSums.Add(sum);
                }
            }

            if (partialSums.Count == 0)
                return new Polygon();

            if (partialSums.Count == 1)
                return partialSums[0];

            return UnionPolygons(partialSums);
        }

        /// <summary>
        /// Computes the Minkowski sum of two convex polygons by merging their
        /// edge vectors sorted by angle. O(n+m) where n and m are vertex counts.
        /// Both polygons must have CCW winding.
        /// </summary>
        public static Polygon ConvexMinkowskiSum(Polygon a, Polygon b)
        {
            var edgesA = GetEdgeVectors(a);
            var edgesB = GetEdgeVectors(b);

            // Find bottom-most (then left-most) vertex for each polygon as starting point.
            var startA = FindBottomLeft(a);
            var startB = FindBottomLeft(b);

            var result = new Polygon();
            var current = new Vector(
                a.Vertices[startA].X + b.Vertices[startB].X,
                a.Vertices[startA].Y + b.Vertices[startB].Y);
            result.Vertices.Add(current);

            var ia = 0;
            var ib = 0;
            var na = edgesA.Count;
            var nb = edgesB.Count;

            // Reorder edges to start from the bottom-left vertex.
            var orderedA = ReorderEdges(edgesA, startA);
            var orderedB = ReorderEdges(edgesB, startB);

            while (ia < na || ib < nb)
            {
                Vector edge;

                if (ia >= na)
                {
                    edge = orderedB[ib++];
                }
                else if (ib >= nb)
                {
                    edge = orderedA[ia++];
                }
                else
                {
                    var angleA = System.Math.Atan2(orderedA[ia].Y, orderedA[ia].X);
                    var angleB = System.Math.Atan2(orderedB[ib].Y, orderedB[ib].X);

                    if (angleA < angleB)
                    {
                        edge = orderedA[ia++];
                    }
                    else if (angleB < angleA)
                    {
                        edge = orderedB[ib++];
                    }
                    else
                    {
                        // Same angle — merge both edges.
                        edge = new Vector(
                            orderedA[ia].X + orderedB[ib].X,
                            orderedA[ia].Y + orderedB[ib].Y);
                        ia++;
                        ib++;
                    }
                }

                current = new Vector(current.X + edge.X, current.Y + edge.Y);
                result.Vertices.Add(current);
            }

            result.Close();
            return result;
        }

        /// <summary>
        /// Gets edge vectors for a polygon (each edge as a direction vector).
        /// Assumes the polygon is closed (last vertex == first vertex) or handles open polygons.
        /// </summary>
        private static List<Vector> GetEdgeVectors(Polygon polygon)
        {
            var verts = polygon.Vertices;
            var n = verts.Count;

            // If closed, skip last duplicate vertex.
            if (n > 1 && verts[0].X == verts[n - 1].X && verts[0].Y == verts[n - 1].Y)
                n--;

            var edges = new List<Vector>(n);

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                edges.Add(new Vector(verts[next].X - verts[i].X, verts[next].Y - verts[i].Y));
            }

            return edges;
        }

        /// <summary>
        /// Finds the index of the bottom-most (then left-most) vertex.
        /// </summary>
        private static int FindBottomLeft(Polygon polygon)
        {
            var verts = polygon.Vertices;
            var n = verts.Count;

            if (n > 1 && verts[0].X == verts[n - 1].X && verts[0].Y == verts[n - 1].Y)
                n--;

            var best = 0;

            for (var i = 1; i < n; i++)
            {
                if (verts[i].Y < verts[best].Y ||
                    (verts[i].Y == verts[best].Y && verts[i].X < verts[best].X))
                    best = i;
            }

            return best;
        }

        /// <summary>
        /// Reorders edge vectors to start from the given vertex index.
        /// </summary>
        private static List<Vector> ReorderEdges(List<Vector> edges, int startIndex)
        {
            var n = edges.Count;
            var result = new List<Vector>(n);

            for (var i = 0; i < n; i++)
                result.Add(edges[(startIndex + i) % n]);

            return result;
        }

        /// <summary>
        /// Unions multiple polygons using Clipper2.
        /// Returns the outer boundary of the union as a single polygon.
        /// </summary>
        internal static Polygon UnionPolygons(List<Polygon> polygons)
        {
            var paths = new PathsD();

            foreach (var poly in polygons)
            {
                var path = ToClipperPath(poly);

                if (path.Count >= 3)
                    paths.Add(path);
            }

            if (paths.Count == 0)
                return new Polygon();

            var result = Clipper.Union(paths, FillRule.NonZero);

            if (result.Count == 0)
                return new Polygon();

            // Find the largest polygon (by area) as the outer boundary.
            var largest = result[0];
            var largestArea = System.Math.Abs(Clipper.Area(largest));

            for (var i = 1; i < result.Count; i++)
            {
                var area = System.Math.Abs(Clipper.Area(result[i]));

                if (area > largestArea)
                {
                    largest = result[i];
                    largestArea = area;
                }
            }

            return FromClipperPath(largest);
        }

        /// <summary>
        /// Converts an OpenNest Polygon to a Clipper2 PathD, with an optional offset.
        /// </summary>
        public static PathD ToClipperPath(Polygon polygon, Vector offset = default)
        {
            var path = new PathD();
            var verts = polygon.Vertices;
            var n = verts.Count;

            // Skip closing vertex if present.
            if (n > 1 && verts[0].X == verts[n - 1].X && verts[0].Y == verts[n - 1].Y)
                n--;

            for (var i = 0; i < n; i++)
                path.Add(new PointD(verts[i].X + offset.X, verts[i].Y + offset.Y));

            return path;
        }

        /// <summary>
        /// Converts a Clipper2 PathD to an OpenNest Polygon.
        /// </summary>
        public static Polygon FromClipperPath(PathD path)
        {
            var polygon = new Polygon();

            foreach (var pt in path)
                polygon.Vertices.Add(new Vector(pt.x, pt.y));

            polygon.Close();
            polygon.UpdateBounds();
            return polygon;
        }
    }
}

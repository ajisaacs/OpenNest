using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Computes the No-Fit Polygon (NFP) between two polygons.
    /// The NFP defines all positions where the orbiting polygon's reference point
    /// would cause overlap with the stationary polygon.
    /// </summary>
    public static class NoFitPolygon
    {
        /// <summary>
        /// Computes the NFP between a convex stationary polygon A and a convex orbiting
        /// polygon B: the Minkowski sum of A and -B (B reflected through its reference point).
        /// </summary>
        public static Polygon ComputeConvex(Polygon stationary, Polygon orbiting)
        {
            var reflected = Reflect(orbiting);
            return ConvexMinkowskiSum(stationary, reflected);
        }

        /// <summary>
        /// Reflects a polygon through the origin (negates all vertex coordinates).
        /// Point reflection (negating both axes) is equivalent to 180° rotation,
        /// which preserves winding order. No reversal needed.
        /// </summary>
        private static Polygon Reflect(Polygon polygon)
        {
            var result = new Polygon();

            foreach (var v in polygon.Vertices)
                result.Vertices.Add(new Vector(-v.X, -v.Y));

            return result;
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

            // Find indices of bottom-left vertices for both.
            var startA = FindBottomLeft(a);
            var startB = FindBottomLeft(b);

            var result = new Polygon();

            // The starting point of the Minkowski sum A + B is the sum of the
            // starting points of A and B. For NFP = A + (-B), this is
            // startA + startReflectedB.
            var current = new Vector(
                a.Vertices[startA].X + b.Vertices[startB].X,
                a.Vertices[startA].Y + b.Vertices[startB].Y
            );

            result.Vertices.Add(current);

            var ia = 0;
            var ib = 0;
            var na = edgesA.Count;
            var nb = edgesB.Count;

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
                    if (angleA < 0)
                        angleA += Angle.TwoPI;

                    var angleB = System.Math.Atan2(orderedB[ib].Y, orderedB[ib].X);
                    if (angleB < 0)
                        angleB += Angle.TwoPI;

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
                        edge = new Vector(
                            orderedA[ia].X + orderedB[ib].X,
                            orderedA[ia].Y + orderedB[ib].Y
                        );
                        ia++;
                        ib++;
                    }
                }

                current = new Vector(current.X + edge.X, current.Y + edge.Y);
                result.Vertices.Add(current);
            }

            result.Close();
            result.UpdateBounds();
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
                if (
                    verts[i].Y < verts[best].Y
                    || (verts[i].Y == verts[best].Y && verts[i].X < verts[best].X)
                )
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
    }
}

using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Polygon overlap test with hole subtraction. This is the reference implementation
    /// for a future GPU kernel, so it deliberately stays hand-rolled instead of using
    /// Clipper (which is CPU-only and allocation-heavy; see <see cref="ClipperBridge"/>
    /// for the CPU preparation that feeds it).
    /// <para>
    /// GPU-port contract. Per-polygon preparation, done once per drawing and rotation,
    /// then cached and uploaded: the spacing offset (<see cref="ClipperBridge"/>),
    /// triangulation (<see cref="ConvexDecomposition.Triangulate"/>) of the outline and
    /// each hole, and the bounding box of every polygon and triangle. Per-pair work,
    /// kernel-shaped (fixed-size, loop-only, no recursion): the bounding-box rejects,
    /// Sutherland-Hodgman clipping of convex triangle pairs (<c>ClipConvex</c>), and
    /// subtraction of hole triangles from the clipped regions (<c>SubtractTriangles</c>).
    /// Inputs are closed, lines-only polygons; winding is normalized by triangulation.
    /// </para>
    /// </summary>
    public static class Collision
    {
        public static CollisionResult Check(
            Polygon a,
            Polygon b,
            List<Polygon> holesA = null,
            List<Polygon> holesB = null
        )
        {
            // Step 1: Bounding box pre-filter
            if (!BoundingBoxesOverlap(a.BoundingBox, b.BoundingBox))
                return CollisionResult.None;

            // Step 2: Quick intersection test for crossing points
            var intersectionPoints = FindCrossingPoints(a, b);

            // Step 3: Convex decomposition
            var trisA = TriangulateWithBounds(a);
            var trisB = TriangulateWithBounds(b);

            // Step 4: Clip all triangle pairs
            var regions = new List<Polygon>();

            foreach (var triA in trisA)
            {
                foreach (var triB in trisB)
                {
                    if (!BoundingBoxesOverlap(triA.BoundingBox, triB.BoundingBox))
                        continue;

                    var clipped = ClipConvex(triA, triB);
                    if (clipped != null)
                        regions.Add(clipped);
                }
            }

            // Step 5: Hole subtraction
            if (regions.Count > 0)
                regions = SubtractHoles(regions, holesA, holesB);

            if (regions.Count == 0)
                return new CollisionResult(false, regions, intersectionPoints);

            // Step 6: Build result
            return new CollisionResult(true, regions, intersectionPoints);
        }

        public static bool HasOverlap(
            Polygon a,
            Polygon b,
            List<Polygon> holesA = null,
            List<Polygon> holesB = null
        )
        {
            if (!BoundingBoxesOverlap(a.BoundingBox, b.BoundingBox))
                return false;

            // Full check is needed: crossing points alone miss containment cases
            // (one polygon entirely inside another has zero edge crossings).
            return Check(a, b, holesA, holesB).Overlaps;
        }

        public static List<CollisionResult> CheckAll(
            List<Polygon> polygons,
            List<List<Polygon>> holes = null
        )
        {
            var results = new List<CollisionResult>();

            for (var i = 0; i < polygons.Count; i++)
            {
                for (var j = i + 1; j < polygons.Count; j++)
                {
                    var holesA = holes != null && i < holes.Count ? holes[i] : null;
                    var holesB = holes != null && j < holes.Count ? holes[j] : null;
                    var result = Check(polygons[i], polygons[j], holesA, holesB);

                    if (result.Overlaps)
                        results.Add(result);
                }
            }

            return results;
        }

        public static bool HasAnyOverlap(List<Polygon> polygons, List<List<Polygon>> holes = null)
        {
            for (var i = 0; i < polygons.Count; i++)
            {
                for (var j = i + 1; j < polygons.Count; j++)
                {
                    var holesA = holes != null && i < holes.Count ? holes[i] : null;
                    var holesB = holes != null && j < holes.Count ? holes[j] : null;

                    if (HasOverlap(polygons[i], polygons[j], holesA, holesB))
                        return true;
                }
            }

            return false;
        }

        private static bool BoundingBoxesOverlap(Box a, Box b)
        {
            var overlapX = System.Math.Min(a.Right, b.Right) - System.Math.Max(a.Left, b.Left);
            var overlapY = System.Math.Min(a.Top, b.Top) - System.Math.Max(a.Bottom, b.Bottom);

            return overlapX > Tolerance.Epsilon && overlapY > Tolerance.Epsilon;
        }

        private static List<Vector> FindCrossingPoints(Polygon a, Polygon b)
        {
            if (!Intersect.Intersects(a, b, out var rawPts))
                return new List<Vector>();

            // Filter boundary contacts (vertex touches)
            var vertsA = CollectVertices(a);
            var vertsB = CollectVertices(b);
            var filtered = new List<Vector>();

            foreach (var pt in rawPts)
            {
                if (IsNearAnyVertex(pt, vertsA) || IsNearAnyVertex(pt, vertsB))
                    continue;
                filtered.Add(pt);
            }

            return filtered;
        }

        private static List<Vector> CollectVertices(Polygon polygon)
        {
            var verts = new List<Vector>(polygon.Vertices.Count);
            foreach (var v in polygon.Vertices)
                verts.Add(v);
            return verts;
        }

        private static bool IsNearAnyVertex(Vector pt, List<Vector> vertices)
        {
            foreach (var v in vertices)
            {
                if (pt.X.IsEqualTo(v.X) && pt.Y.IsEqualTo(v.Y))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Triangulates a polygon and ensures each triangle has its bounding box updated.
        /// </summary>
        private static List<Polygon> TriangulateWithBounds(Polygon polygon)
        {
            var tris = ConvexDecomposition.Triangulate(polygon);
            foreach (var tri in tris)
                tri.UpdateBounds();
            return tris;
        }

        /// <summary>
        /// Sutherland-Hodgman polygon clipping. Clips subject against each edge
        /// of clip. Both must be convex. Returns null if no overlap.
        /// </summary>
        private static Polygon ClipConvex(Polygon subject, Polygon clip)
        {
            var output = OpenVertices(subject);
            var clipVerts = OpenVertices(clip);
            for (var i = 0; i < clipVerts.Count && output.Count >= 3; i++)
            {
                output = ClipHalfSpace(output, clipVerts[i], clipVerts[(i + 1) % clipVerts.Count], true);
            }

            return PositiveAreaPolygon(output);
        }

        /// <summary>
        /// Cross product of vectors (edgeStart->edgeEnd) and (edgeStart->point).
        /// Positive = point is left of edge (inside for CCW polygon).
        /// </summary>
        private static double Cross(Vector edgeStart, Vector edgeEnd, Vector point)
        {
            return (edgeEnd.X - edgeStart.X) * (point.Y - edgeStart.Y)
                - (edgeEnd.Y - edgeStart.Y) * (point.X - edgeStart.X);
        }

        /// <summary>
        /// Subtracts holes from overlap regions.
        /// </summary>
        private static List<Polygon> SubtractHoles(
            List<Polygon> regions,
            List<Polygon> holesA,
            List<Polygon> holesB
        )
        {
            var allHoles = new List<Polygon>();
            if (holesA != null)
                allHoles.AddRange(holesA);
            if (holesB != null)
                allHoles.AddRange(holesB);

            if (allHoles.Count == 0)
                return regions;

            foreach (var hole in allHoles)
            {
                var holeTris = TriangulateWithBounds(hole);
                var surviving = new List<Polygon>();

                foreach (var region in regions)
                {
                    var pieces = SubtractTriangles(region, holeTris);
                    surviving.AddRange(pieces);
                }

                regions = surviving;

                if (regions.Count == 0)
                    break;
            }

            return regions;
        }

        /// <summary>
        /// Subtracts hole triangles from a convex region. At each edge, emit the outside
        /// portion and carry only the inside remainder to the next edge. The emitted
        /// pieces are disjoint and convex, so no repeated triangulation is needed.
        /// </summary>
        private static List<Polygon> SubtractTriangles(Polygon region, List<Polygon> holeTris)
        {
            var current = new List<Polygon> { region };

            foreach (var holeTri in holeTris)
            {
                var next = new List<Polygon>();

                foreach (var piece in current)
                {
                    // Subtraction must also remove thin fragments created by clipping.
                    // The pair-level length tolerance would skip some of these even
                    // when their area is large enough to count as an overlap.
                    var a = piece.BoundingBox;
                    var b = holeTri.BoundingBox;
                    if (a.Right <= b.Left || b.Right <= a.Left || a.Top <= b.Bottom || b.Top <= a.Bottom)
                    {
                        next.Add(piece);
                        continue;
                    }

                    var remainder = OpenVertices(piece);
                    var holeVerts = OpenVertices(holeTri);
                    for (var i = 0; i < holeVerts.Count && remainder.Count >= 3; i++)
                    {
                        var start = holeVerts[i];
                        var end = holeVerts[(i + 1) % holeVerts.Count];
                        var outside = PositiveAreaPolygon(ClipHalfSpace(remainder, start, end, false));
                        if (outside != null)
                            next.Add(outside);
                        remainder = ClipHalfSpace(remainder, start, end, true);
                    }
                }

                current = next;
                if (current.Count == 0)
                    break;
            }

            return current;
        }

        /// <summary>
        /// Clips an open vertex list against one half-space. Classification and
        /// interpolation use the same signed cross products: intersections always
        /// lie on the input segment. An epsilon-shifted inside test combined with
        /// intersections on the unshifted line can extrapolate and create material.
        /// Apply the area tolerance only to the resulting polygons, not to edge signs.
        /// </summary>
        private static List<Vector> ClipHalfSpace(
            List<Vector> vertices,
            Vector edgeStart,
            Vector edgeEnd,
            bool inside
        )
        {
            var kept = new List<Vector>();
            for (var i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                var currentDistance = Cross(edgeStart, edgeEnd, current);
                var nextDistance = Cross(edgeStart, edgeEnd, next);
                if (inside ? currentDistance >= 0 : currentDistance <= 0)
                    AddDistinct(kept, current);

                // Only strict opposite signs cross the line. Boundary endpoints
                // are already kept, and near-parallel crossings need no cutoff.
                if ((currentDistance < 0 && nextDistance > 0) || (currentDistance > 0 && nextDistance < 0))
                {
                    var t = currentDistance / (currentDistance - nextDistance);
                    AddDistinct(kept, new Vector(
                        current.X + t * (next.X - current.X),
                        current.Y + t * (next.Y - current.Y)));
                }
            }
            if (kept.Count > 1 && SamePoint(kept[0], kept[kept.Count - 1]))
                kept.RemoveAt(kept.Count - 1);
            return kept;
        }

        private static bool SamePoint(Vector a, Vector b) => a.X == b.X && a.Y == b.Y;

        private static void AddDistinct(List<Vector> vertices, Vector point)
        {
            if (vertices.Count == 0 || !SamePoint(vertices[vertices.Count - 1], point))
                vertices.Add(point);
        }

        private static List<Vector> OpenVertices(Polygon polygon)
        {
            var vertices = new List<Vector>(polygon.Vertices);
            if (vertices.Count > 1 && SamePoint(vertices[0], vertices[vertices.Count - 1]))
                vertices.RemoveAt(vertices.Count - 1);
            return vertices;
        }

        private static Polygon PositiveAreaPolygon(List<Vector> vertices)
        {
            if (vertices.Count < 3)
                return null;

            // Measure relative to a vertex to avoid cancellation of world-coordinate
            // products when a small clipped fragment is far from the origin.
            var twiceArea = 0.0;
            for (var i = 1; i + 1 < vertices.Count; i++)
                twiceArea += Cross(vertices[0], vertices[i], vertices[i + 1]);
            if (System.Math.Abs(twiceArea) <= 2 * Tolerance.Epsilon)
                return null;

            var polygon = new Polygon();
            polygon.Vertices.AddRange(vertices);
            // Polygon.Close uses fuzzy Vector equality; clipping needs an exact
            // closing vertex even when the last edge is shorter than Epsilon.
            polygon.Vertices.Add(vertices[0]);
            polygon.UpdateBounds();
            return polygon;
        }
    }
}

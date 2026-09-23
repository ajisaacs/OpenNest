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
            var output = new List<Vector>(subject.Vertices);

            // Remove closing vertex if present
            if (
                output.Count > 1
                && output[0].X == output[output.Count - 1].X
                && output[0].Y == output[output.Count - 1].Y
            )
                output.RemoveAt(output.Count - 1);

            var clipVerts = new List<Vector>(clip.Vertices);
            if (
                clipVerts.Count > 1
                && clipVerts[0].X == clipVerts[clipVerts.Count - 1].X
                && clipVerts[0].Y == clipVerts[clipVerts.Count - 1].Y
            )
                clipVerts.RemoveAt(clipVerts.Count - 1);

            for (var i = 0; i < clipVerts.Count; i++)
            {
                if (output.Count == 0)
                    return null;

                var edgeStart = clipVerts[i];
                var edgeEnd = clipVerts[(i + 1) % clipVerts.Count];
                var input = output;
                output = new List<Vector>();

                for (var j = 0; j < input.Count; j++)
                {
                    var current = input[j];
                    var next = input[(j + 1) % input.Count];
                    var currentInside = Cross(edgeStart, edgeEnd, current) >= -Tolerance.Epsilon;
                    var nextInside = Cross(edgeStart, edgeEnd, next) >= -Tolerance.Epsilon;

                    if (currentInside)
                    {
                        output.Add(current);
                        if (!nextInside)
                        {
                            var ix = LineIntersection(edgeStart, edgeEnd, current, next);
                            if (ix.IsValid())
                                output.Add(ix);
                        }
                    }
                    else if (nextInside)
                    {
                        var ix = LineIntersection(edgeStart, edgeEnd, current, next);
                        if (ix.IsValid())
                            output.Add(ix);
                    }
                }
            }

            if (output.Count < 3)
                return null;

            var result = new Polygon();
            result.Vertices.AddRange(output);
            result.Close();
            result.UpdateBounds();

            // Reject degenerate slivers
            if (result.Area() < Tolerance.Epsilon)
                return null;

            return result;
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
        /// Intersection of lines (a1->a2) and (b1->b2). Returns Vector.Invalid if parallel.
        /// </summary>
        private static Vector LineIntersection(Vector a1, Vector a2, Vector b1, Vector b2)
        {
            var d1x = a2.X - a1.X;
            var d1y = a2.Y - a1.Y;
            var d2x = b2.X - b1.X;
            var d2y = b2.Y - b1.Y;
            var cross = d1x * d2y - d1y * d2x;

            if (System.Math.Abs(cross) < Tolerance.Epsilon)
                return Vector.Invalid;

            var t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / cross;
            return new Vector(a1.X + t * d1x, a1.Y + t * d1y);
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
        /// Subtracts hole triangles from a region. Exact: a piece outside a convex hole
        /// triangle equals the union of its clips against each triangle edge's outside
        /// half-space, so overlap confined to a cutout disappears while any material
        /// sliver outside the hole survives.
        /// </summary>
        private static List<Polygon> SubtractTriangles(Polygon region, List<Polygon> holeTris)
        {
            var current = new List<Polygon> { region };

            foreach (var holeTri in holeTris)
            {
                var next = new List<Polygon>();

                foreach (var piece in current)
                {
                    if (!BoundingBoxesOverlap(piece.BoundingBox, holeTri.BoundingBox))
                    {
                        next.Add(piece);
                        continue;
                    }

                    foreach (var pieceTri in TriangulateWithBounds(piece))
                    {
                        var holeVerts = holeTri.Vertices;
                        var holeCount = holeTri.IsClosed() ? holeVerts.Count - 1 : holeVerts.Count;
                        var survived = false;
                        for (var i = 0; i < holeCount; i++)
                            survived |= AddIfPositiveArea(
                                next,
                                ClipOutsideHalfSpace(
                                    pieceTri,
                                    holeVerts[i],
                                    holeVerts[(i + 1) % holeCount]
                                )
                            );
                        if (!survived)
                            continue; // piece lies entirely within the hole
                    }
                }

                current = next;
            }

            return current;
        }

        /// <summary>
        /// Sutherland-Hodgman clip of a convex polygon to the strict outside of the
        /// infinite line edgeStart->edgeEnd of a CCW hole edge (Cross &lt; -Epsilon).
        /// </summary>
        private static List<Vector> ClipOutsideHalfSpace(
            Polygon piece,
            Vector edgeStart,
            Vector edgeEnd
        )
        {
            var verts = piece.Vertices;
            var count = piece.IsClosed() ? verts.Count - 1 : verts.Count;
            var kept = new List<Vector>();
            for (var i = 0; i < count; i++)
            {
                var current = verts[i];
                var next = verts[(i + 1) % count];
                var currentInside = Cross(edgeStart, edgeEnd, current) >= -Tolerance.Epsilon;
                var nextInside = Cross(edgeStart, edgeEnd, next) >= -Tolerance.Epsilon;
                if (!currentInside)
                    kept.Add(current);
                if (currentInside == nextInside)
                    continue;
                var intersection = LineIntersection(edgeStart, edgeEnd, current, next);
                if (intersection.IsValid())
                    kept.Add(intersection);
            }
            return kept;
        }

        private static bool AddIfPositiveArea(List<Polygon> polygons, List<Vector> vertices)
        {
            if (vertices.Count < 3)
                return false;
            var polygon = new Polygon();
            polygon.Vertices.AddRange(vertices);
            polygon.Close();
            polygon.UpdateBounds();
            if (polygon.Area() <= Tolerance.Epsilon)
                return false;
            polygons.Add(polygon);
            return true;
        }
    }
}

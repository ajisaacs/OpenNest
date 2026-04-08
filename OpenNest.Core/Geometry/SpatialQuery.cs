using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    public static class SpatialQuery
    {
        /// <summary>
        /// Finds the distance from a vertex to a line segment along a push axis.
        /// Returns double.MaxValue if the ray does not hit the segment.
        /// </summary>
        private static double RayEdgeDistance(Vector vertex, Line edge, PushDirection direction)
        {
            return RayEdgeDistance(
                vertex.X, vertex.Y,
                edge.pt1.X, edge.pt1.Y, edge.pt2.X, edge.pt2.Y,
                direction);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double RayEdgeDistance(
            double vx, double vy,
            double p1x, double p1y, double p2x, double p2y,
            PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left:
                case PushDirection.Right:
                    {
                        var dy = p2y - p1y;
                        if (System.Math.Abs(dy) < Tolerance.Epsilon)
                            return double.MaxValue;

                        var t = (vy - p1y) / dy;
                        if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                            return double.MaxValue;

                        var ix = p1x + t * (p2x - p1x);
                        var dist = direction == PushDirection.Left ? vx - ix : ix - vx;

                        if (dist > Tolerance.Epsilon) return dist;
                        if (dist >= -Tolerance.Epsilon) return 0;
                        return double.MaxValue;
                    }

                case PushDirection.Down:
                case PushDirection.Up:
                    {
                        var dx = p2x - p1x;
                        if (System.Math.Abs(dx) < Tolerance.Epsilon)
                            return double.MaxValue;

                        var t = (vx - p1x) / dx;
                        if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                            return double.MaxValue;

                        var iy = p1y + t * (p2y - p1y);
                        var dist = direction == PushDirection.Down ? vy - iy : iy - vy;

                        if (dist > Tolerance.Epsilon) return dist;
                        if (dist >= -Tolerance.Epsilon) return 0;
                        return double.MaxValue;
                    }

                default:
                    return double.MaxValue;
            }
        }

        /// <summary>
        /// Generalized ray-edge distance along an arbitrary unit direction vector.
        /// Returns double.MaxValue if the ray does not hit the segment.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static double RayEdgeDistance(
            double vx, double vy,
            double p1x, double p1y, double p2x, double p2y,
            double dirX, double dirY)
        {
            var ex = p2x - p1x;
            var ey = p2y - p1y;

            var det = ex * dirY - ey * dirX;
            if (System.Math.Abs(det) < Tolerance.Epsilon)
                return double.MaxValue;

            var dvx = p1x - vx;
            var dvy = p1y - vy;

            var t = (ex * dvy - ey * dvx) / det;
            if (t < -Tolerance.Epsilon)
                return double.MaxValue;

            var s = (dirX * dvy - dirY * dvx) / det;
            if (s < -Tolerance.Epsilon || s > 1.0 + Tolerance.Epsilon)
                return double.MaxValue;

            if (t > Tolerance.Epsilon) return t;
            if (t >= -Tolerance.Epsilon) return 0;
            return double.MaxValue;
        }

        /// <summary>
        /// Solves ray-circle intersection, returning the two parametric t values.
        /// Returns false if no real intersection exists.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool SolveRayCircle(
            double vx, double vy,
            double cx, double cy, double r,
            double dirX, double dirY,
            out double t1, out double t2)
        {
            var ox = vx - cx;
            var oy = vy - cy;

            var a = dirX * dirX + dirY * dirY;
            var b = 2.0 * (ox * dirX + oy * dirY);
            var c = ox * ox + oy * oy - r * r;

            var discriminant = b * b - 4.0 * a * c;
            if (discriminant < 0)
            {
                t1 = t2 = double.MaxValue;
                return false;
            }

            var sqrtD = System.Math.Sqrt(discriminant);
            var inv2a = 1.0 / (2.0 * a);
            t1 = (-b - sqrtD) * inv2a;
            t2 = (-b + sqrtD) * inv2a;
            return true;
        }

        /// <summary>
        /// Computes the distance from a point along a direction to an arc.
        /// Solves ray-circle intersection, then constrains hits to the arc's
        /// angular span. Returns double.MaxValue if no hit.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static double RayArcDistance(
            double vx, double vy,
            double cx, double cy, double r,
            double startAngle, double endAngle, bool reversed,
            double dirX, double dirY)
        {
            if (!SolveRayCircle(vx, vy, cx, cy, r, dirX, dirY, out var t1, out var t2))
                return double.MaxValue;

            var best = double.MaxValue;

            if (t1 > -Tolerance.Epsilon)
            {
                var hitAngle = Angle.NormalizeRad(System.Math.Atan2(
                    vy + t1 * dirY - cy, vx + t1 * dirX - cx));
                if (Angle.IsBetweenRad(hitAngle, startAngle, endAngle, reversed))
                    best = t1 > Tolerance.Epsilon ? t1 : 0;
            }

            if (t2 > -Tolerance.Epsilon && t2 < best)
            {
                var hitAngle = Angle.NormalizeRad(System.Math.Atan2(
                    vy + t2 * dirY - cy, vx + t2 * dirX - cx));
                if (Angle.IsBetweenRad(hitAngle, startAngle, endAngle, reversed))
                    best = t2 > Tolerance.Epsilon ? t2 : 0;
            }

            return best;
        }

        /// <summary>
        /// Computes the distance from a point along a direction to a full circle.
        /// Returns double.MaxValue if no hit.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static double RayCircleDistance(
            double vx, double vy,
            double cx, double cy, double r,
            double dirX, double dirY)
        {
            if (!SolveRayCircle(vx, vy, cx, cy, r, dirX, dirY, out var t1, out var t2))
                return double.MaxValue;

            if (t1 > Tolerance.Epsilon) return t1;
            if (t1 >= -Tolerance.Epsilon) return 0;
            if (t2 > Tolerance.Epsilon) return t2;
            if (t2 >= -Tolerance.Epsilon) return 0;

            return double.MaxValue;
        }

        /// <summary>
        /// Computes the minimum translation distance along a push direction before
        /// any edge of movingLines contacts any edge of stationaryLines.
        /// Returns double.MaxValue if no collision path exists.
        /// </summary>
        public static double DirectionalDistance(List<Line> movingLines, List<Line> stationaryLines, PushDirection direction)
        {
            return DirectionalDistance(movingLines, 0, 0, stationaryLines, direction);
        }

        /// <summary>
        /// Computes the minimum directional distance with the moving lines translated
        /// by (movingDx, movingDy) without creating new Line objects.
        /// </summary>
        public static double DirectionalDistance(
            List<Line> movingLines, double movingDx, double movingDy,
            List<Line> stationaryLines, PushDirection direction)
        {
            var minDist = double.MaxValue;
            var movingOffset = new Vector(movingDx, movingDy);

            // Case 1: Each moving vertex -> each stationary edge
            var movingVertices = CollectVertices(movingLines, movingOffset);

            var stationaryEdges = ToEdgeArray(stationaryLines);
            SortEdgesForPruning(stationaryEdges, direction);

            foreach (var mv in movingVertices)
            {
                var d = OneWayDistance(mv, stationaryEdges, Vector.Zero, direction);
                if (d < minDist) minDist = d;
            }

            // Case 2: Each stationary vertex -> each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);
            var stationaryVertices = CollectVertices(stationaryLines, Vector.Zero);

            var movingEdges = ToEdgeArray(movingLines);
            SortEdgesForPruning(movingEdges, opposite);

            foreach (var sv in stationaryVertices)
            {
                var d = OneWayDistance(sv, movingEdges, movingOffset, opposite);
                if (d < minDist) minDist = d;
            }

            return minDist;
        }

        /// <summary>
        /// Packs line segments into a flat double array [x1,y1,x2,y2, ...] for GPU transfer.
        /// </summary>
        public static double[] FlattenLines(List<Line> lines)
        {
            var result = new double[lines.Count * 4];
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                result[i * 4] = line.pt1.X;
                result[i * 4 + 1] = line.pt1.Y;
                result[i * 4 + 2] = line.pt2.X;
                result[i * 4 + 3] = line.pt2.Y;
            }
            return result;
        }

        /// <summary>
        /// Computes the minimum directional distance using raw edge arrays and location offsets
        /// to avoid all intermediate object allocations.
        /// </summary>
        public static double DirectionalDistance(
            (Vector start, Vector end)[] movingEdges, Vector movingOffset,
            (Vector start, Vector end)[] stationaryEdges, Vector stationaryOffset,
            PushDirection direction)
        {
            var minDist = double.MaxValue;

            SortEdgesForPruning(stationaryEdges, direction);

            // Case 1: Each moving vertex -> each stationary edge
            var movingVertices = CollectVertices(movingEdges, movingOffset);

            foreach (var mv in movingVertices)
            {
                var d = OneWayDistance(mv, stationaryEdges, stationaryOffset, direction);
                if (d < minDist) minDist = d;
            }

            // Case 2: Each stationary vertex -> each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);
            SortEdgesForPruning(movingEdges, opposite);

            var stationaryVertices = CollectVertices(stationaryEdges, stationaryOffset);

            foreach (var sv in stationaryVertices)
            {
                var d = OneWayDistance(sv, movingEdges, movingOffset, opposite);
                if (d < minDist) minDist = d;
            }

            return minDist;
        }

        public static double OneWayDistance(
            Vector vertex, (Vector start, Vector end)[] edges, Vector edgeOffset,
            PushDirection direction)
        {
            var minDist = double.MaxValue;
            var vx = vertex.X;
            var vy = vertex.Y;

            // Pruning: edges are sorted by their perpendicular min-coordinate in PartBoundary.
            if (direction == PushDirection.Left || direction == PushDirection.Right)
            {
                for (var i = 0; i < edges.Length; i++)
                {
                    var e1 = edges[i].start + edgeOffset;
                    var e2 = edges[i].end + edgeOffset;

                    var minY = e1.Y < e2.Y ? e1.Y : e2.Y;
                    var maxY = e1.Y > e2.Y ? e1.Y : e2.Y;

                    // Since edges are sorted by minY, if vy < minY, then vy < all subsequent minY.
                    if (vy < minY - Tolerance.Epsilon)
                        break;

                    if (vy > maxY + Tolerance.Epsilon)
                        continue;

                    var d = RayEdgeDistance(vx, vy, e1.X, e1.Y, e2.X, e2.Y, direction);
                    if (d < minDist) minDist = d;
                }
            }
            else // Up/Down
            {
                for (var i = 0; i < edges.Length; i++)
                {
                    var e1 = edges[i].start + edgeOffset;
                    var e2 = edges[i].end + edgeOffset;

                    var minX = e1.X < e2.X ? e1.X : e2.X;
                    var maxX = e1.X > e2.X ? e1.X : e2.X;

                    // Since edges are sorted by minX, if vx < minX, then vx < all subsequent minX.
                    if (vx < minX - Tolerance.Epsilon)
                        break;

                    if (vx > maxX + Tolerance.Epsilon)
                        continue;

                    var d = RayEdgeDistance(vx, vy, e1.X, e1.Y, e2.X, e2.Y, direction);
                    if (d < minDist) minDist = d;
                }
            }

            return minDist;
        }

        public static PushDirection OppositeDirection(PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left: return PushDirection.Right;
                case PushDirection.Right: return PushDirection.Left;
                case PushDirection.Up: return PushDirection.Down;
                case PushDirection.Down: return PushDirection.Up;
                default: return direction;
            }
        }

        public static bool IsHorizontalDirection(PushDirection direction)
        {
            return direction is PushDirection.Left or PushDirection.Right;
        }

        public static double EdgeDistance(Box box, Box boundary, PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left: return box.Left - boundary.Left;
                case PushDirection.Right: return boundary.Right - box.Right;
                case PushDirection.Up: return boundary.Top - box.Top;
                case PushDirection.Down: return box.Bottom - boundary.Bottom;
                default: return double.MaxValue;
            }
        }

        public static Vector DirectionToOffset(PushDirection direction, double distance)
        {
            switch (direction)
            {
                case PushDirection.Left: return new Vector(-distance, 0);
                case PushDirection.Right: return new Vector(distance, 0);
                case PushDirection.Up: return new Vector(0, distance);
                case PushDirection.Down: return new Vector(0, -distance);
                default: return new Vector();
            }
        }

        public static double DirectionalGap(Box from, Box to, PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left: return from.Left - to.Right;
                case PushDirection.Right: return to.Left - from.Right;
                case PushDirection.Up: return to.Bottom - from.Top;
                case PushDirection.Down: return from.Bottom - to.Top;
                default: return double.MaxValue;
            }
        }

        #region Generalized direction (Vector) overloads

        /// <summary>
        /// Computes how far a box can travel along the given unit direction
        /// before exiting the boundary box.
        /// </summary>
        public static double EdgeDistance(Box box, Box boundary, Vector direction)
        {
            var dist = double.MaxValue;

            if (direction.X < -Tolerance.Epsilon)
            {
                var d = (box.Left - boundary.Left) / -direction.X;
                if (d < dist) dist = d;
            }
            else if (direction.X > Tolerance.Epsilon)
            {
                var d = (boundary.Right - box.Right) / direction.X;
                if (d < dist) dist = d;
            }

            if (direction.Y < -Tolerance.Epsilon)
            {
                var d = (box.Bottom - boundary.Bottom) / -direction.Y;
                if (d < dist) dist = d;
            }
            else if (direction.Y > Tolerance.Epsilon)
            {
                var d = (boundary.Top - box.Top) / direction.Y;
                if (d < dist) dist = d;
            }

            return dist < 0 ? 0 : dist;
        }

        /// <summary>
        /// Computes the directional gap between two boxes along an arbitrary unit direction.
        /// Positive means 'to' is ahead of 'from' in the push direction.
        /// </summary>
        public static double DirectionalGap(Box from, Box to, Vector direction)
        {
            var fromMax = BoxProjectionMax(from, direction.X, direction.Y);
            var toMin = BoxProjectionMin(to, direction.X, direction.Y);
            return toMin - fromMax;
        }

        /// <summary>
        /// Returns true if two boxes overlap when projected onto the axis
        /// perpendicular to the given unit direction.
        /// </summary>
        public static bool PerpendicularOverlap(Box a, Box b, Vector direction)
        {
            var px = -direction.Y;
            var py = direction.X;

            var aMin = BoxProjectionMin(a, px, py);
            var aMax = BoxProjectionMax(a, px, py);
            var bMin = BoxProjectionMin(b, px, py);
            var bMax = BoxProjectionMax(b, px, py);

            return aMin <= bMax + Tolerance.Epsilon && bMin <= aMax + Tolerance.Epsilon;
        }

        /// <summary>
        /// Computes the minimum translation distance along an arbitrary unit direction
        /// before any edge of movingLines contacts any edge of stationaryLines.
        /// </summary>
        public static double DirectionalDistance(List<Line> movingLines, List<Line> stationaryLines, Vector direction)
        {
            var minDist = double.MaxValue;
            var dirX = direction.X;
            var dirY = direction.Y;

            var movingVertices = CollectVertices(movingLines, Vector.Zero);

            foreach (var mv in movingVertices)
            {
                for (var i = 0; i < stationaryLines.Count; i++)
                {
                    var e = stationaryLines[i];
                    var d = RayEdgeDistance(mv.X, mv.Y, e.pt1.X, e.pt1.Y, e.pt2.X, e.pt2.Y, dirX, dirY);
                    if (d < minDist) minDist = d;
                }
            }

            var oppX = -dirX;
            var oppY = -dirY;

            var stationaryVertices = CollectVertices(stationaryLines, Vector.Zero);

            foreach (var sv in stationaryVertices)
            {
                for (var i = 0; i < movingLines.Count; i++)
                {
                    var e = movingLines[i];
                    var d = RayEdgeDistance(sv.X, sv.Y, e.pt1.X, e.pt1.Y, e.pt2.X, e.pt2.Y, oppX, oppY);
                    if (d < minDist) minDist = d;
                }
            }

            return minDist;
        }

        /// <summary>
        /// Computes the minimum translation distance along an arbitrary unit direction
        /// before any vertex/edge of movingEntities contacts any vertex/edge of
        /// stationaryEntities. Works with native Line, Arc, and Circle entities
        /// without tessellation.
        /// </summary>
        public static double DirectionalDistance(
            List<Entity> movingEntities, List<Entity> stationaryEntities, Vector direction)
        {
            var minDist = double.MaxValue;
            var dirX = direction.X;
            var dirY = direction.Y;

            var movingVertices = ExtractEntityVertices(movingEntities);

            for (var v = 0; v < movingVertices.Length; v++)
            {
                var vx = movingVertices[v].X;
                var vy = movingVertices[v].Y;

                for (var j = 0; j < stationaryEntities.Count; j++)
                {
                    var d = RayEntityDistance(vx, vy, stationaryEntities[j], dirX, dirY);
                    if (d < minDist)
                    {
                        minDist = d;
                        if (d <= 0) return 0;
                    }
                }
            }

            var oppX = -dirX;
            var oppY = -dirY;

            var stationaryVertices = ExtractEntityVertices(stationaryEntities);

            for (var v = 0; v < stationaryVertices.Length; v++)
            {
                var vx = stationaryVertices[v].X;
                var vy = stationaryVertices[v].Y;

                for (var j = 0; j < movingEntities.Count; j++)
                {
                    var d = RayEntityDistance(vx, vy, movingEntities[j], oppX, oppY);
                    if (d < minDist)
                    {
                        minDist = d;
                        if (d <= 0) return 0;
                    }
                }
            }

            // Phase 3: Arc-to-line closest-point check.
            // Phases 1-2 sample arc endpoints and cardinal extremes, but the actual
            // closest point on a small corner arc to a straight edge may lie between
            // those samples. Use ClosestPointTo to find it and fire a ray from there.
            for (var i = 0; i < movingEntities.Count; i++)
            {
                if (movingEntities[i] is Arc mArc)
                {
                    for (var j = 0; j < stationaryEntities.Count; j++)
                    {
                        if (stationaryEntities[j] is Line sLine)
                        {
                            var linePt = sLine.ClosestPointTo(mArc.Center);
                            var arcPt = mArc.ClosestPointTo(linePt);
                            var d = RayEdgeDistance(arcPt.X, arcPt.Y,
                                sLine.pt1.X, sLine.pt1.Y, sLine.pt2.X, sLine.pt2.Y,
                                dirX, dirY);
                            if (d < minDist) { minDist = d; if (d <= 0) return 0; }
                        }
                    }
                }
            }

            for (var i = 0; i < stationaryEntities.Count; i++)
            {
                if (stationaryEntities[i] is Arc sArc2)
                {
                    for (var j = 0; j < movingEntities.Count; j++)
                    {
                        if (movingEntities[j] is Line mLine)
                        {
                            var linePt = mLine.ClosestPointTo(sArc2.Center);
                            var arcPt = sArc2.ClosestPointTo(linePt);
                            var d = RayEdgeDistance(arcPt.X, arcPt.Y,
                                mLine.pt1.X, mLine.pt1.Y, mLine.pt2.X, mLine.pt2.Y,
                                oppX, oppY);
                            if (d < minDist) { minDist = d; if (d <= 0) return 0; }
                        }
                    }
                }
            }

            // Phase 4: Curve-to-curve direct distance.
            // The vertex-to-entity approach misses the closest contact between two
            // curved entities (circles/arcs) because only a few cardinal vertices are
            // sampled. The true closest contact along the push direction is found by
            // treating it as a ray from one center to an expanded circle at the other
            // center (radius = r1 + r2).
            for (var i = 0; i < movingEntities.Count; i++)
            {
                var me = movingEntities[i];
                if (!TryGetCurveParams(me, out var mcx, out var mcy, out var mr))
                    continue;

                for (var j = 0; j < stationaryEntities.Count; j++)
                {
                    var se = stationaryEntities[j];
                    if (!TryGetCurveParams(se, out var scx, out var scy, out var sr))
                        continue;

                    var d = RayCircleDistance(mcx, mcy, scx, scy, mr + sr, dirX, dirY);

                    if (d >= minDist || d == double.MaxValue)
                        continue;

                    // For arcs, verify the contact point falls within both arcs' angular ranges.
                    if (me is Arc || se is Arc)
                    {
                        var mx = mcx + d * dirX;
                        var my = mcy + d * dirY;
                        var toCx = scx - mx;
                        var toCy = scy - my;

                        if (me is Arc mArc)
                        {
                            var angle = Angle.NormalizeRad(System.Math.Atan2(toCy, toCx));
                            if (!Angle.IsBetweenRad(angle, mArc.StartAngle, mArc.EndAngle, mArc.IsReversed))
                                continue;
                        }

                        if (se is Arc sArc)
                        {
                            var angle = Angle.NormalizeRad(System.Math.Atan2(-toCy, -toCx));
                            if (!Angle.IsBetweenRad(angle, sArc.StartAngle, sArc.EndAngle, sArc.IsReversed))
                                continue;
                        }
                    }

                    minDist = d;
                    if (d <= 0) return 0;
                }
            }

            return minDist;
        }

        private static double RayEntityDistance(
            double vx, double vy, Entity entity, double dirX, double dirY)
        {
            if (entity is Line line)
            {
                return RayEdgeDistance(vx, vy,
                    line.pt1.X, line.pt1.Y, line.pt2.X, line.pt2.Y,
                    dirX, dirY);
            }

            if (entity is Arc arc)
            {
                return RayArcDistance(vx, vy,
                    arc.Center.X, arc.Center.Y, arc.Radius,
                    arc.StartAngle, arc.EndAngle, arc.IsReversed,
                    dirX, dirY);
            }

            if (entity is Circle circle)
            {
                return RayCircleDistance(vx, vy,
                    circle.Center.X, circle.Center.Y, circle.Radius,
                    dirX, dirY);
            }

            return double.MaxValue;
        }

        private static Vector[] ExtractEntityVertices(List<Entity> entities)
        {
            var vertices = new HashSet<Vector>();

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];

                if (entity is Line line)
                {
                    vertices.Add(line.pt1);
                    vertices.Add(line.pt2);
                }
                else if (entity is Arc arc)
                {
                    vertices.Add(arc.StartPoint());
                    vertices.Add(arc.EndPoint());
                    AddArcExtremeVertices(vertices, arc);
                }
                else if (entity is Circle circle)
                {
                    vertices.Add(new Vector(circle.Center.X + circle.Radius, circle.Center.Y));
                    vertices.Add(new Vector(circle.Center.X - circle.Radius, circle.Center.Y));
                    vertices.Add(new Vector(circle.Center.X, circle.Center.Y + circle.Radius));
                    vertices.Add(new Vector(circle.Center.X, circle.Center.Y - circle.Radius));
                }
            }

            return vertices.ToArray();
        }

        private static void AddArcExtremeVertices(HashSet<Vector> points, Arc arc)
        {
            var a1 = arc.StartAngle;
            var a2 = arc.EndAngle;

            if (arc.IsReversed)
                Generic.Swap(ref a1, ref a2);

            if (Angle.IsBetweenRad(Angle.TwoPI, a1, a2))
                points.Add(new Vector(arc.Center.X + arc.Radius, arc.Center.Y));
            if (Angle.IsBetweenRad(Angle.HalfPI, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y + arc.Radius));
            if (Angle.IsBetweenRad(System.Math.PI, a1, a2))
                points.Add(new Vector(arc.Center.X - arc.Radius, arc.Center.Y));
            if (Angle.IsBetweenRad(System.Math.PI * 1.5, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y - arc.Radius));
        }

        private static HashSet<Vector> CollectVertices(List<Line> lines, Vector offset)
        {
            var vertices = new HashSet<Vector>();
            for (var i = 0; i < lines.Count; i++)
            {
                vertices.Add(lines[i].pt1 + offset);
                vertices.Add(lines[i].pt2 + offset);
            }
            return vertices;
        }

        private static HashSet<Vector> CollectVertices((Vector start, Vector end)[] edges, Vector offset)
        {
            var vertices = new HashSet<Vector>();
            for (var i = 0; i < edges.Length; i++)
            {
                vertices.Add(edges[i].start + offset);
                vertices.Add(edges[i].end + offset);
            }
            return vertices;
        }

        private static (Vector start, Vector end)[] ToEdgeArray(List<Line> lines)
        {
            var edges = new (Vector start, Vector end)[lines.Count];
            for (var i = 0; i < lines.Count; i++)
                edges[i] = (lines[i].pt1, lines[i].pt2);
            return edges;
        }

        private static void SortEdgesForPruning((Vector start, Vector end)[] edges, PushDirection direction)
        {
            if (direction == PushDirection.Left || direction == PushDirection.Right)
                System.Array.Sort(edges, (a, b) =>
                    System.Math.Min(a.start.Y, a.end.Y).CompareTo(System.Math.Min(b.start.Y, b.end.Y)));
            else
                System.Array.Sort(edges, (a, b) =>
                    System.Math.Min(a.start.X, a.end.X).CompareTo(System.Math.Min(b.start.X, b.end.X)));
        }

        private static bool TryGetCurveParams(Entity entity, out double cx, out double cy, out double r)
        {
            if (entity is Circle circle)
            {
                cx = circle.Center.X; cy = circle.Center.Y; r = circle.Radius;
                return true;
            }
            if (entity is Arc arc)
            {
                cx = arc.Center.X; cy = arc.Center.Y; r = arc.Radius;
                return true;
            }
            cx = cy = r = 0;
            return false;
        }

        private static double BoxProjectionMin(Box box, double dx, double dy)
        {
            var x = dx >= 0 ? box.Left : box.Right;
            var y = dy >= 0 ? box.Bottom : box.Top;
            return x * dx + y * dy;
        }

        private static double BoxProjectionMax(Box box, double dx, double dy)
        {
            var x = dx >= 0 ? box.Right : box.Left;
            var y = dy >= 0 ? box.Top : box.Bottom;
            return x * dx + y * dy;
        }

        #endregion

        public static Box GetLargestBoxVertically(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var verticalBoxes = boxes.Where(b => !(b.Left > pt.X || b.Right < pt.X)).ToList();

            if (!FindVerticalLimits(pt, bounds, verticalBoxes, out var top, out var btm))
                return Box.Empty;

            var horizontalBoxes = boxes.Where(b => !(b.Bottom >= top || b.Top <= btm)).ToList();

            if (!FindHorizontalLimits(pt, bounds, horizontalBoxes, out var lft, out var rgt))
                return Box.Empty;

            return new Box(lft, btm, rgt - lft, top - btm);
        }

        public static Box GetLargestBoxHorizontally(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var horizontalBoxes = boxes.Where(b => !(b.Bottom > pt.Y || b.Top < pt.Y)).ToList();

            if (!FindHorizontalLimits(pt, bounds, horizontalBoxes, out var lft, out var rgt))
                return Box.Empty;

            var verticalBoxes = boxes.Where(b => !(b.Left >= rgt || b.Right <= lft)).ToList();

            if (!FindVerticalLimits(pt, bounds, verticalBoxes, out var top, out var btm))
                return Box.Empty;

            return new Box(lft, btm, rgt - lft, top - btm);
        }

        private static bool FindVerticalLimits(Vector pt, Box bounds, List<Box> boxes, out double top, out double btm)
        {
            top = double.MaxValue;
            btm = double.MinValue;

            foreach (var box in boxes)
            {
                var boxBtm = box.Bottom;
                var boxTop = box.Top;

                if (boxBtm > pt.Y && boxBtm < top)
                    top = boxBtm;
                else if (box.Top < pt.Y && boxTop > btm)
                    btm = boxTop;
            }

            if (top == double.MaxValue)
            {
                if (bounds.Top > pt.Y) top = bounds.Top;
                else return false;
            }

            if (btm == double.MinValue)
            {
                if (bounds.Bottom < pt.Y) btm = bounds.Bottom;
                else return false;
            }

            return true;
        }

        private static bool FindHorizontalLimits(Vector pt, Box bounds, List<Box> boxes, out double lft, out double rgt)
        {
            lft = double.MinValue;
            rgt = double.MaxValue;

            foreach (var box in boxes)
            {
                var boxLft = box.Left;
                var boxRgt = box.Right;

                if (boxLft > pt.X && boxLft < rgt)
                    rgt = boxLft;
                else if (boxRgt < pt.X && boxRgt > lft)
                    lft = boxRgt;
            }

            if (rgt == double.MaxValue)
            {
                if (bounds.Right > pt.X) rgt = bounds.Right;
                else return false;
            }

            if (lft == double.MinValue)
            {
                if (bounds.Left < pt.X) lft = bounds.Left;
                else return false;
            }

            return true;
        }
    }
}

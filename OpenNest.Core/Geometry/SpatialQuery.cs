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
        /// Computes the minimum translation distance along a push direction before
        /// any edge of movingLines contacts any edge of stationaryLines.
        /// Returns double.MaxValue if no collision path exists.
        /// </summary>
        public static double DirectionalDistance(List<Line> movingLines, List<Line> stationaryLines, PushDirection direction)
        {
            var minDist = double.MaxValue;

            // Case 1: Each moving vertex -> each stationary edge
            var movingVertices = new HashSet<Vector>();
            for (int i = 0; i < movingLines.Count; i++)
            {
                movingVertices.Add(movingLines[i].pt1);
                movingVertices.Add(movingLines[i].pt2);
            }

            var stationaryEdges = new (Vector start, Vector end)[stationaryLines.Count];
            for (int i = 0; i < stationaryLines.Count; i++)
                stationaryEdges[i] = (stationaryLines[i].pt1, stationaryLines[i].pt2);

            // Sort edges for pruning if not already sorted (usually they aren't here)
            if (direction == PushDirection.Left || direction == PushDirection.Right)
                stationaryEdges = stationaryEdges.OrderBy(e => System.Math.Min(e.start.Y, e.end.Y)).ToArray();
            else
                stationaryEdges = stationaryEdges.OrderBy(e => System.Math.Min(e.start.X, e.end.X)).ToArray();

            foreach (var mv in movingVertices)
            {
                var d = OneWayDistance(mv, stationaryEdges, Vector.Zero, direction);
                if (d < minDist) minDist = d;
            }

            // Case 2: Each stationary vertex -> each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);
            var stationaryVertices = new HashSet<Vector>();
            for (int i = 0; i < stationaryLines.Count; i++)
            {
                stationaryVertices.Add(stationaryLines[i].pt1);
                stationaryVertices.Add(stationaryLines[i].pt2);
            }

            var movingEdges = new (Vector start, Vector end)[movingLines.Count];
            for (int i = 0; i < movingLines.Count; i++)
                movingEdges[i] = (movingLines[i].pt1, movingLines[i].pt2);

            if (opposite == PushDirection.Left || opposite == PushDirection.Right)
                movingEdges = movingEdges.OrderBy(e => System.Math.Min(e.start.Y, e.end.Y)).ToArray();
            else
                movingEdges = movingEdges.OrderBy(e => System.Math.Min(e.start.X, e.end.X)).ToArray();

            foreach (var sv in stationaryVertices)
            {
                var d = OneWayDistance(sv, movingEdges, Vector.Zero, opposite);
                if (d < minDist) minDist = d;
            }

            return minDist;
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
            var movingVertices = new HashSet<Vector>();
            for (int i = 0; i < movingLines.Count; i++)
            {
                movingVertices.Add(movingLines[i].pt1 + movingOffset);
                movingVertices.Add(movingLines[i].pt2 + movingOffset);
            }

            var stationaryEdges = new (Vector start, Vector end)[stationaryLines.Count];
            for (int i = 0; i < stationaryLines.Count; i++)
                stationaryEdges[i] = (stationaryLines[i].pt1, stationaryLines[i].pt2);

            if (direction == PushDirection.Left || direction == PushDirection.Right)
                stationaryEdges = stationaryEdges.OrderBy(e => System.Math.Min(e.start.Y, e.end.Y)).ToArray();
            else
                stationaryEdges = stationaryEdges.OrderBy(e => System.Math.Min(e.start.X, e.end.X)).ToArray();

            foreach (var mv in movingVertices)
            {
                var d = OneWayDistance(mv, stationaryEdges, Vector.Zero, direction);
                if (d < minDist) minDist = d;
            }

            // Case 2: Each stationary vertex -> each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);
            var stationaryVertices = new HashSet<Vector>();
            for (int i = 0; i < stationaryLines.Count; i++)
            {
                stationaryVertices.Add(stationaryLines[i].pt1);
                stationaryVertices.Add(stationaryLines[i].pt2);
            }

            var movingEdges = new (Vector start, Vector end)[movingLines.Count];
            for (int i = 0; i < movingLines.Count; i++)
                movingEdges[i] = (movingLines[i].pt1, movingLines[i].pt2);

            if (opposite == PushDirection.Left || opposite == PushDirection.Right)
                movingEdges = movingEdges.OrderBy(e => System.Math.Min(e.start.Y, e.end.Y)).ToArray();
            else
                movingEdges = movingEdges.OrderBy(e => System.Math.Min(e.start.X, e.end.X)).ToArray();

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

            // Extract unique vertices from moving edges.
            var movingVertices = new HashSet<Vector>();
            for (var i = 0; i < movingEdges.Length; i++)
            {
                movingVertices.Add(movingEdges[i].start + movingOffset);
                movingVertices.Add(movingEdges[i].end + movingOffset);
            }

            // Case 1: Each moving vertex -> each stationary edge
            foreach (var mv in movingVertices)
            {
                var d = OneWayDistance(mv, stationaryEdges, stationaryOffset, direction);
                if (d < minDist) minDist = d;
            }

            // Case 2: Each stationary vertex -> each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);
            var stationaryVertices = new HashSet<Vector>();
            for (var i = 0; i < stationaryEdges.Length; i++)
            {
                stationaryVertices.Add(stationaryEdges[i].start + stationaryOffset);
                stationaryVertices.Add(stationaryEdges[i].end + stationaryOffset);
            }

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

            var movingVertices = new HashSet<Vector>();
            for (var i = 0; i < movingLines.Count; i++)
            {
                movingVertices.Add(movingLines[i].pt1);
                movingVertices.Add(movingLines[i].pt2);
            }

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

            var stationaryVertices = new HashSet<Vector>();
            for (var i = 0; i < stationaryLines.Count; i++)
            {
                stationaryVertices.Add(stationaryLines[i].pt1);
                stationaryVertices.Add(stationaryLines[i].pt2);
            }

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

        public static double ClosestDistanceLeft(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsHorizontalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Right)
                    continue;

                var distance = box.Left - compareBox.Right;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceRight(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsHorizontalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Left)
                    continue;

                var distance = compareBox.Left - box.Right;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceUp(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsVerticalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Bottom)
                    continue;

                var distance = compareBox.Bottom - box.Top;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceDown(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsVerticalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Top)
                    continue;

                var distance = box.Bottom - compareBox.Top;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static Box GetLargestBoxVertically(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var verticalBoxes = boxes.Where(b => !(b.Left > pt.X || b.Right < pt.X)).ToList();

            #region Find Top/Bottom Limits

            var top = double.MaxValue;
            var btm = double.MinValue;

            foreach (var box in verticalBoxes)
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
                if (bounds.Top > pt.Y)
                    top = bounds.Top;
                else return Box.Empty;
            }

            if (btm == double.MinValue)
            {
                if (bounds.Bottom < pt.Y)
                    btm = bounds.Bottom;
                else return Box.Empty;
            }

            #endregion

            var horizontalBoxes = boxes.Where(b => !(b.Bottom >= top || b.Top <= btm)).ToList();

            #region Find Left/Right Limits

            var lft = double.MinValue;
            var rgt = double.MaxValue;

            foreach (var box in horizontalBoxes)
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
                if (bounds.Right > pt.X)
                    rgt = bounds.Right;
                else return Box.Empty;
            }

            if (lft == double.MinValue)
            {
                if (bounds.Left < pt.X)
                    lft = bounds.Left;
                else return Box.Empty;
            }

            #endregion

            return new Box(lft, btm, rgt - lft, top - btm);
        }

        public static Box GetLargestBoxHorizontally(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var horizontalBoxes = boxes.Where(b => !(b.Bottom > pt.Y || b.Top < pt.Y)).ToList();

            #region Find Left/Right Limits

            var lft = double.MinValue;
            var rgt = double.MaxValue;

            foreach (var box in horizontalBoxes)
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
                if (bounds.Right > pt.X)
                    rgt = bounds.Right;
                else return Box.Empty;
            }

            if (lft == double.MinValue)
            {
                if (bounds.Left < pt.X)
                    lft = bounds.Left;
                else return Box.Empty;
            }

            #endregion

            var verticalBoxes = boxes.Where(b => !(b.Left >= rgt || b.Right <= lft)).ToList();

            #region Find Top/Bottom Limits

            var top = double.MaxValue;
            var btm = double.MinValue;

            foreach (var box in verticalBoxes)
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
                if (bounds.Top > pt.Y)
                    top = bounds.Top;
                else return Box.Empty;
            }

            if (btm == double.MinValue)
            {
                if (bounds.Bottom < pt.Y)
                    btm = bounds.Bottom;
                else return Box.Empty;
            }

            #endregion

            return new Box(lft, btm, rgt - lft, top - btm);
        }
    }
}

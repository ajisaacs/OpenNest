using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Pole of inaccessibility: the interior point farthest from the outline and holes,
    /// found by best-first quadtree refinement to within <c>precision</c>.
    /// </summary>
    public static class PolyLabel
    {
        public static Vector Find(
            Polygon outer,
            IList<Polygon> holes = null,
            double precision = 0.5
        ) => Find(outer, holes, precision, int.MaxValue, out _);

        /// <summary>
        /// Bounded search for callers that cannot afford refinement along a long thin shape:
        /// stops after about <paramref name="maxCells"/> distance evaluations and returns the best
        /// cell center so far. When the starting grid would exceed a quarter of the budget it is
        /// coarsened so the whole shape is sampled first. The result can lie outside a shape
        /// thinner than the remaining refinement, so callers must check it.
        /// </summary>
        public static Vector Find(Polygon outer, IList<Polygon> holes, double precision, int maxCells) =>
            Find(outer, holes, precision, maxCells, out _);

        /// <summary>
        /// A precision proportional to the shape's mean material width (twice the net area over
        /// the total outline length, holes included), so small or slender shapes refine further
        /// than large solid ones. A fixed precision near the shape's size stops at the first
        /// interior cell. Holes that leave no net area (a second outline mistaken for a hole, so
        /// lying outside the first) fall back to the outline alone: a near-zero precision would
        /// refine a pole ridge, such as a rectangle's centerline, into billions of cells.
        /// </summary>
        public static double ThicknessPrecision(Polygon outer, IList<Polygon> holes = null, double fraction = 0.05)
        {
            var outerArea = outer.Area();
            var outerLength = ClosedLength(outer);
            var area = outerArea;
            var length = outerLength;

            if (holes != null)
            {
                for (var i = 0; i < holes.Count; i++)
                {
                    area -= holes[i].Area();
                    length += ClosedLength(holes[i]);
                }
            }

            // Equal outlines leave rounding noise, not material.
            if (!(area > outerArea * 1e-6) || !(length > 0))
            {
                area = outerArea;
                length = outerLength;
            }

            if (!(area > 0) || !(length > 0))
                return 1e-9;

            return System.Math.Max(fraction * 2 * area / length, 1e-9);
        }

        private static double ClosedLength(Polygon polygon)
        {
            var verts = polygon.Vertices;
            var sum = 0.0;

            for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
                sum += verts[j].DistanceTo(verts[i]);

            return sum;
        }

        internal static Vector Find(
            Polygon outer,
            IList<Polygon> holes,
            double precision,
            int maxCells,
            out int evaluatedCells
        )
        {
            evaluatedCells = 0;
            if (outer.Vertices.Count < 3)
                return outer.Vertices.Count > 0 ? outer.Vertices[0] : new Vector();

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            for (var i = 0; i < outer.Vertices.Count; i++)
            {
                var v = outer.Vertices[i];
                if (v.X < minX)
                    minX = v.X;
                if (v.Y < minY)
                    minY = v.Y;
                if (v.X > maxX)
                    maxX = v.X;
                if (v.Y > maxY)
                    maxY = v.Y;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var cellSize = System.Math.Min(width, height);

            if (cellSize == 0)
                return new Vector((minX + maxX) / 2, (minY + maxY) / 2);

            if (maxCells < int.MaxValue)
            {
                var gridLimit = System.Math.Max(1, maxCells / 4);
                while (System.Math.Ceiling(width / cellSize) * System.Math.Ceiling(height / cellSize) > gridLimit)
                    cellSize *= 2;
            }

            var halfCell = cellSize / 2;
            var boxes = HoleBoxes(holes);

            var initial = new List<Cell>();

            for (var x = minX; x < maxX; x += cellSize)
            {
                for (var y = minY; y < maxY; y += cellSize)
                {
                    initial.Add(new Cell(x + halfCell, y + halfCell, halfCell, outer, holes, boxes));
                    // Far from the origin a step smaller than the coordinate's precision
                    // no longer advances; the grid would never end.
                    if (y + cellSize == y)
                        break;
                }
                if (x + cellSize == x)
                    break;
            }

            initial.Sort((a, b) => b.MaxDist.CompareTo(a.MaxDist));

            var bestCell = GetCentroidCell(outer, holes, boxes);
            evaluatedCells = initial.Count + 1;

            for (var i = 0; i < initial.Count; i++)
                if (initial[i].Dist > bestCell.Dist)
                {
                    bestCell = initial[i];
                    break;
                }

            // Highest MaxDist first. Equal bounds pop newest first, and the initial cells in
            // sorted order, exactly as the former sorted list did (insertion ahead of equals).
            var queue = new PriorityQueue<Cell, (double, long)>();
            for (var i = 0; i < initial.Count; i++)
                queue.Enqueue(initial[i], (-initial[i].MaxDist, i - (long)initial.Count));
            var sequence = (long)initial.Count;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();

                if (cell.Dist > bestCell.Dist)
                    bestCell = cell;

                if (cell.MaxDist - bestCell.Dist <= precision)
                    continue;

                if (evaluatedCells > maxCells - 4)
                    break;

                halfCell = cell.HalfSize / 2;

                // Far from the origin a half-step may no longer move the center; such a cell
                // cannot be split, and its identical children would multiply without end.
                if (cell.X - halfCell == cell.X || cell.X + halfCell == cell.X
                    || cell.Y - halfCell == cell.Y || cell.Y + halfCell == cell.Y)
                    continue;

                var newCells = new[]
                {
                    new Cell(cell.X - halfCell, cell.Y - halfCell, halfCell, outer, holes, boxes),
                    new Cell(cell.X + halfCell, cell.Y - halfCell, halfCell, outer, holes, boxes),
                    new Cell(cell.X - halfCell, cell.Y + halfCell, halfCell, outer, holes, boxes),
                    new Cell(cell.X + halfCell, cell.Y + halfCell, halfCell, outer, holes, boxes),
                };
                evaluatedCells += newCells.Length;

                for (var i = 0; i < newCells.Length; i++)
                {
                    if (newCells[i].MaxDist > bestCell.Dist + precision)
                        queue.Enqueue(newCells[i], (-newCells[i].MaxDist, -++sequence));
                }
            }

            return new Vector(bestCell.X, bestCell.Y);
        }

        private static Cell GetCentroidCell(Polygon outer, IList<Polygon> holes, Box[] boxes)
        {
            var area = 0.0;
            var cx = 0.0;
            var cy = 0.0;
            var verts = outer.Vertices;

            for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
            {
                var a = verts[i];
                var b = verts[j];
                var cross = a.X * b.Y - b.X * a.Y;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
                area += cross;
            }

            area *= 0.5;

            if (System.Math.Abs(area) < 1e-10)
                return new Cell(verts[0].X, verts[0].Y, 0, outer, holes, boxes);

            cx /= (6 * area);
            cy /= (6 * area);

            return new Cell(cx, cy, 0, outer, holes, boxes);
        }

        private static double PointToPolygonDist(double x, double y, Polygon polygon)
        {
            var minDist = double.MaxValue;
            var verts = polygon.Vertices;

            for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
            {
                var a = verts[i];
                var b = verts[j];

                var dx = b.X - a.X;
                var dy = b.Y - a.Y;

                if (dx != 0 || dy != 0)
                {
                    var t = ((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy);

                    if (t > 1)
                    {
                        a = b;
                    }
                    else if (t > 0)
                    {
                        a = new Vector(a.X + dx * t, a.Y + dy * t);
                    }
                }

                var segDx = x - a.X;
                var segDy = y - a.Y;
                var dist = System.Math.Sqrt(segDx * segDx + segDy * segDy);

                if (dist < minDist)
                    minDist = dist;
            }

            return minDist;
        }

        private sealed class Cell
        {
            public readonly double X;
            public readonly double Y;
            public readonly double HalfSize;
            public readonly double Dist;
            public readonly double MaxDist;

            public Cell(double x, double y, double halfSize, Polygon outer, IList<Polygon> holes, Box[] boxes)
            {
                X = x;
                Y = y;
                HalfSize = halfSize;

                var pt = new Vector(x, y);
                var inside = outer.ContainsPoint(pt);

                if (inside && holes != null)
                {
                    for (var i = 0; i < holes.Count; i++)
                    {
                        if (boxes[i].Contains(x, y) && holes[i].ContainsPoint(pt))
                        {
                            inside = false;
                            break;
                        }
                    }
                }

                Dist = PointToAllEdgesDist(x, y, outer, holes, boxes);

                if (!inside)
                    Dist = -Dist;

                MaxDist = Dist + HalfSize * System.Math.Sqrt(2);
            }
        }

        private static double PointToAllEdgesDist(
            double x,
            double y,
            Polygon outer,
            IList<Polygon> holes,
            Box[] boxes
        )
        {
            var minDist = PointToPolygonDist(x, y, outer);

            if (holes != null)
            {
                for (var i = 0; i < holes.Count; i++)
                {
                    // No edge is nearer than its hole's box, so a farther box cannot lower the minimum.
                    if (boxes[i].Distance(x, y) > minDist)
                        continue;
                    var d = PointToPolygonDist(x, y, holes[i]);
                    if (d < minDist)
                        minDist = d;
                }
            }

            return minDist;
        }

        // Each hole's vertex bounds: a cell skips the containment test outside them and the
        // edge distances when they are farther than the nearest edge found so far. Both skips
        // leave the result exactly as without them; parts with many small holes search far faster.
        private static Box[] HoleBoxes(IList<Polygon> holes)
        {
            if (holes == null)
                return null;

            var boxes = new Box[holes.Count];
            for (var i = 0; i < holes.Count; i++)
            {
                var box = new Box(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                foreach (var v in holes[i].Vertices)
                    box = new Box(System.Math.Min(box.MinX, v.X), System.Math.Min(box.MinY, v.Y),
                        System.Math.Max(box.MaxX, v.X), System.Math.Max(box.MaxY, v.Y));
                boxes[i] = box;
            }
            return boxes;
        }

        private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
        {
            // Inclusive, so a point on a hole's edge is still tested.
            public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

            public double Distance(double x, double y)
            {
                var dx = System.Math.Max(System.Math.Max(MinX - x, x - MaxX), 0);
                var dy = System.Math.Max(System.Math.Max(MinY - y, y - MaxY), 0);
                return System.Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }
}

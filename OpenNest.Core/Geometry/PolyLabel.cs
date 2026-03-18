using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class PolyLabel
    {
        public static Vector Find(Polygon outer, IList<Polygon> holes = null, double precision = 0.5)
        {
            if (outer.Vertices.Count < 3)
                return outer.Vertices.Count > 0
                    ? outer.Vertices[0]
                    : new Vector();

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            for (var i = 0; i < outer.Vertices.Count; i++)
            {
                var v = outer.Vertices[i];
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var cellSize = System.Math.Min(width, height);

            if (cellSize == 0)
                return new Vector((minX + maxX) / 2, (minY + maxY) / 2);

            var halfCell = cellSize / 2;

            var queue = new List<Cell>();

            for (var x = minX; x < maxX; x += cellSize)
                for (var y = minY; y < maxY; y += cellSize)
                    queue.Add(new Cell(x + halfCell, y + halfCell, halfCell, outer, holes));

            queue.Sort((a, b) => b.MaxDist.CompareTo(a.MaxDist));

            var bestCell = GetCentroidCell(outer, holes);

            for (var i = 0; i < queue.Count; i++)
                if (queue[i].Dist > bestCell.Dist)
                {
                    bestCell = queue[i];
                    break;
                }

            while (queue.Count > 0)
            {
                var cell = queue[0];
                queue.RemoveAt(0);

                if (cell.Dist > bestCell.Dist)
                    bestCell = cell;

                if (cell.MaxDist - bestCell.Dist <= precision)
                    continue;

                halfCell = cell.HalfSize / 2;

                var newCells = new[]
                {
                    new Cell(cell.X - halfCell, cell.Y - halfCell, halfCell, outer, holes),
                    new Cell(cell.X + halfCell, cell.Y - halfCell, halfCell, outer, holes),
                    new Cell(cell.X - halfCell, cell.Y + halfCell, halfCell, outer, holes),
                    new Cell(cell.X + halfCell, cell.Y + halfCell, halfCell, outer, holes),
                };

                for (var i = 0; i < newCells.Length; i++)
                {
                    if (newCells[i].MaxDist > bestCell.Dist + precision)
                        InsertSorted(queue, newCells[i]);
                }
            }

            return new Vector(bestCell.X, bestCell.Y);
        }

        private static void InsertSorted(List<Cell> list, Cell cell)
        {
            var idx = 0;
            while (idx < list.Count && list[idx].MaxDist > cell.MaxDist)
                idx++;
            list.Insert(idx, cell);
        }

        private static Cell GetCentroidCell(Polygon outer, IList<Polygon> holes)
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
                return new Cell(verts[0].X, verts[0].Y, 0, outer, holes);

            cx /= (6 * area);
            cy /= (6 * area);

            return new Cell(cx, cy, 0, outer, holes);
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

            public Cell(double x, double y, double halfSize, Polygon outer, IList<Polygon> holes)
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
                        if (holes[i].ContainsPoint(pt))
                        {
                            inside = false;
                            break;
                        }
                    }
                }

                Dist = PointToAllEdgesDist(x, y, outer, holes);

                if (!inside)
                    Dist = -Dist;

                MaxDist = Dist + HalfSize * System.Math.Sqrt(2);
            }
        }

        private static double PointToAllEdgesDist(double x, double y, Polygon outer, IList<Polygon> holes)
        {
            var minDist = PointToPolygonDist(x, y, outer);

            if (holes != null)
            {
                for (var i = 0; i < holes.Count; i++)
                {
                    var d = PointToPolygonDist(x, y, holes[i]);
                    if (d < minDist)
                        minDist = d;
                }
            }

            return minDist;
        }
    }
}

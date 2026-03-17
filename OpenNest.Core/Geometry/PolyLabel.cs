using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Finds the pole of inaccessibility — the point inside a polygon that is
    /// farthest from any edge. Based on the polylabel algorithm by Mapbox.
    /// </summary>
    public static class PolyLabel
    {
        public static Vector Find(List<Vector> vertices, double precision = 1.0)
        {
            if (vertices == null || vertices.Count < 3)
                return Vector.Zero;

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var cellSize = System.Math.Min(width, height);

            if (cellSize == 0)
                return new Vector(minX, minY);

            var halfCell = cellSize / 2.0;

            // Priority queue (sorted list, largest distance first)
            var queue = new List<Cell>();

            for (var x = minX; x < maxX; x += cellSize)
            {
                for (var y = minY; y < maxY; y += cellSize)
                {
                    queue.Add(new Cell(x + halfCell, y + halfCell, halfCell, vertices));
                }
            }

            queue.Sort((a, b) => b.Max.CompareTo(a.Max));

            var bestCell = GetCentroidCell(vertices);

            var bboxCell = new Cell(minX + width / 2, minY + height / 2, 0, vertices);
            if (bboxCell.Distance > bestCell.Distance)
                bestCell = bboxCell;

            while (queue.Count > 0)
            {
                var cell = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);

                if (cell.Distance > bestCell.Distance)
                    bestCell = cell;

                if (cell.Max - bestCell.Distance <= precision)
                    continue;

                halfCell = cell.HalfSize / 2;

                var c1 = new Cell(cell.X - halfCell, cell.Y - halfCell, halfCell, vertices);
                var c2 = new Cell(cell.X + halfCell, cell.Y - halfCell, halfCell, vertices);
                var c3 = new Cell(cell.X - halfCell, cell.Y + halfCell, halfCell, vertices);
                var c4 = new Cell(cell.X + halfCell, cell.Y + halfCell, halfCell, vertices);

                InsertSorted(queue, c1);
                InsertSorted(queue, c2);
                InsertSorted(queue, c3);
                InsertSorted(queue, c4);
            }

            return new Vector(bestCell.X, bestCell.Y);
        }

        private static void InsertSorted(List<Cell> queue, Cell cell)
        {
            var index = queue.BinarySearch(cell, CellComparer.Instance);
            if (index < 0) index = ~index;
            queue.Insert(index, cell);
        }

        private static Cell GetCentroidCell(List<Vector> vertices)
        {
            var area = 0.0;
            var cx = 0.0;
            var cy = 0.0;
            var n = vertices.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = vertices[i];
                var b = vertices[j];
                var f = a.X * b.Y - b.X * a.Y;
                cx += (a.X + b.X) * f;
                cy += (a.Y + b.Y) * f;
                area += f * 3;
            }

            if (area == 0)
                return new Cell(vertices[0].X, vertices[0].Y, 0, vertices);

            return new Cell(cx / area, cy / area, 0, vertices);
        }

        private static double PointToPolygonDistance(double x, double y, List<Vector> vertices)
        {
            var inside = false;
            var minDistSq = double.MaxValue;
            var n = vertices.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = vertices[i];
                var b = vertices[j];

                if ((a.Y > y) != (b.Y > y) &&
                    x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }

                var distSq = SegmentDistanceSq(x, y, a.X, a.Y, b.X, b.Y);
                if (distSq < minDistSq)
                    minDistSq = distSq;
            }

            var dist = System.Math.Sqrt(minDistSq);
            return inside ? dist : -dist;
        }

        private static double SegmentDistanceSq(double px, double py,
            double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;

            if (dx != 0 || dy != 0)
            {
                var t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy);

                if (t > 1)
                {
                    ax = bx;
                    ay = by;
                }
                else if (t > 0)
                {
                    ax += dx * t;
                    ay += dy * t;
                }
            }

            dx = px - ax;
            dy = py - ay;

            return dx * dx + dy * dy;
        }

        private struct Cell
        {
            public readonly double X;
            public readonly double Y;
            public readonly double HalfSize;
            public readonly double Distance;
            public readonly double Max;

            public Cell(double x, double y, double halfSize, List<Vector> vertices)
            {
                X = x;
                Y = y;
                HalfSize = halfSize;
                Distance = PointToPolygonDistance(x, y, vertices);
                Max = Distance + halfSize * System.Math.Sqrt(2);
            }
        }

        private class CellComparer : IComparer<Cell>
        {
            public static readonly CellComparer Instance = new CellComparer();
            public int Compare(Cell a, Cell b) => b.Max.CompareTo(a.Max);
        }
    }
}

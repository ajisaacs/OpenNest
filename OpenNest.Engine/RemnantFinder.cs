using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    public class RemnantFinder
    {
        private readonly Box workArea;

        public List<Box> Obstacles { get; } = new();

        public RemnantFinder(Box workArea, List<Box> obstacles = null)
        {
            this.workArea = workArea;

            if (obstacles != null)
                Obstacles.AddRange(obstacles);
        }

        public void AddObstacle(Box obstacle) => Obstacles.Add(obstacle);

        public void AddObstacles(IEnumerable<Box> obstacles) => Obstacles.AddRange(obstacles);

        public void ClearObstacles() => Obstacles.Clear();

        public List<Box> FindRemnants(double minDimension = 0)
        {
            var xs = new SortedSet<double> { workArea.Left, workArea.Right };
            var ys = new SortedSet<double> { workArea.Bottom, workArea.Top };

            foreach (var obs in Obstacles)
            {
                var clipped = ClipToWorkArea(obs);
                if (clipped.Width <= 0 || clipped.Length <= 0)
                    continue;

                xs.Add(clipped.Left);
                xs.Add(clipped.Right);
                ys.Add(clipped.Bottom);
                ys.Add(clipped.Top);
            }

            var xList = xs.ToList();
            var yList = ys.ToList();

            var cols = xList.Count - 1;
            var rows = yList.Count - 1;

            if (cols <= 0 || rows <= 0)
                return new List<Box>();

            var empty = new bool[rows, cols];

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var cell = new Box(xList[c], yList[r],
                        xList[c + 1] - xList[c], yList[r + 1] - yList[r]);

                    empty[r, c] = !OverlapsAnyObstacle(cell);
                }
            }

            var merged = MergeCells(empty, xList, yList, rows, cols);

            var results = new List<Box>();

            foreach (var box in merged)
            {
                if (box.Width >= minDimension && box.Length >= minDimension)
                    results.Add(box);
            }

            results.Sort((a, b) => b.Area().CompareTo(a.Area()));
            return results;
        }

        public static RemnantFinder FromPlate(Plate plate)
        {
            var obstacles = new List<Box>(plate.Parts.Count);

            foreach (var part in plate.Parts)
                obstacles.Add(part.BoundingBox.Offset(plate.PartSpacing));

            return new RemnantFinder(plate.WorkArea(), obstacles);
        }

        private Box ClipToWorkArea(Box obs)
        {
            var left = System.Math.Max(obs.Left, workArea.Left);
            var bottom = System.Math.Max(obs.Bottom, workArea.Bottom);
            var right = System.Math.Min(obs.Right, workArea.Right);
            var top = System.Math.Min(obs.Top, workArea.Top);

            if (right <= left || top <= bottom)
                return Box.Empty;

            return new Box(left, bottom, right - left, top - bottom);
        }

        private bool OverlapsAnyObstacle(Box cell)
        {
            foreach (var obs in Obstacles)
            {
                var clipped = ClipToWorkArea(obs);

                if (clipped.Width <= 0 || clipped.Length <= 0)
                    continue;

                if (cell.Left < clipped.Right &&
                    cell.Right > clipped.Left &&
                    cell.Bottom < clipped.Top &&
                    cell.Top > clipped.Bottom)
                    return true;
            }

            return false;
        }

        private static List<Box> MergeCells(bool[,] empty, List<double> xList, List<double> yList, int rows, int cols)
        {
            var used = new bool[rows, cols];
            var results = new List<Box>();

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    if (!empty[r, c] || used[r, c])
                        continue;

                    var maxC = c;
                    while (maxC + 1 < cols && empty[r, maxC + 1] && !used[r, maxC + 1])
                        maxC++;

                    var maxR = r;
                    while (maxR + 1 < rows)
                    {
                        var rowOk = true;
                        for (var cc = c; cc <= maxC; cc++)
                        {
                            if (!empty[maxR + 1, cc] || used[maxR + 1, cc])
                            {
                                rowOk = false;
                                break;
                            }
                        }

                        if (!rowOk) break;
                        maxR++;
                    }

                    for (var rr = r; rr <= maxR; rr++)
                        for (var cc = c; cc <= maxC; cc++)
                            used[rr, cc] = true;

                    var box = new Box(
                        xList[c], yList[r],
                        xList[maxC + 1] - xList[c],
                        yList[maxR + 1] - yList[r]);

                    results.Add(box);
                }
            }

            return results;
        }
    }
}

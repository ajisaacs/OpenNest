using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// A remnant box with a priority tier.
    /// 0 = within the used envelope (best), 1 = extends past one edge, 2 = fully outside.
    /// </summary>
    public struct TieredRemnant
    {
        public Box Box;
        public int Priority;

        public TieredRemnant(Box box, int priority)
        {
            Box = box;
            Priority = priority;
        }
    }

    public class RemnantFinder
    {
        private readonly Box workArea;

        public List<Box> Obstacles { get; } = new();

        private struct CellGrid
        {
            public bool[,] Empty;
            public List<double> XCoords;
            public List<double> YCoords;
            public int Rows;
            public int Cols;
        }

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
            var grid = BuildGrid();

            if (grid.Rows <= 0 || grid.Cols <= 0)
                return new List<Box>();

            var merged = MergeCells(grid);
            var sized = FilterBySize(merged, minDimension);
            var unique = RemoveDominated(sized);
            SortByEdgeProximity(unique);
            return unique;
        }

        /// <summary>
        /// Finds remnants and splits them into priority tiers based on the
        /// bounding box of all placed parts (the "used envelope").
        ///   Priority 0: fully within the used envelope — compact, preferred.
        ///   Priority 1: extends past one edge of the envelope.
        ///   Priority 2: fully outside the envelope — last resort.
        /// </summary>
        public List<TieredRemnant> FindTieredRemnants(double minDimension = 0)
        {
            var remnants = FindRemnants(minDimension);

            if (Obstacles.Count == 0 || remnants.Count == 0)
                return remnants.Select(r => new TieredRemnant(r, 0)).ToList();

            var envelope = ComputeEnvelope();
            var results = new List<TieredRemnant>();

            foreach (var remnant in remnants)
            {
                var before = results.Count;
                SplitAtEnvelope(remnant, envelope, minDimension, results);

                // If all splits fell below minDim, keep the original unsplit.
                if (results.Count == before)
                    results.Add(new TieredRemnant(remnant, 1));
            }

            results.Sort((a, b) =>
            {
                if (a.Priority != b.Priority)
                    return a.Priority.CompareTo(b.Priority);
                return b.Box.Area().CompareTo(a.Box.Area());
            });

            return results;
        }

        public static RemnantFinder FromPlate(Plate plate)
        {
            var obstacles = new List<Box>(plate.Parts.Count);

            foreach (var part in plate.Parts)
                obstacles.Add(part.BoundingBox.Offset(plate.PartSpacing));

            return new RemnantFinder(plate.WorkArea(), obstacles);
        }

        private CellGrid BuildGrid()
        {
            var clipped = ClipObstacles();

            var xs = new SortedSet<double> { workArea.Left, workArea.Right };
            var ys = new SortedSet<double> { workArea.Bottom, workArea.Top };

            foreach (var obs in clipped)
            {
                xs.Add(obs.Left);
                xs.Add(obs.Right);
                ys.Add(obs.Bottom);
                ys.Add(obs.Top);
            }

            var grid = new CellGrid
            {
                XCoords = xs.ToList(),
                YCoords = ys.ToList(),
            };

            grid.Cols = grid.XCoords.Count - 1;
            grid.Rows = grid.YCoords.Count - 1;

            if (grid.Cols <= 0 || grid.Rows <= 0)
            {
                grid.Empty = new bool[0, 0];
                return grid;
            }

            grid.Empty = new bool[grid.Rows, grid.Cols];

            for (var r = 0; r < grid.Rows; r++)
            {
                for (var c = 0; c < grid.Cols; c++)
                {
                    var cell = new Box(grid.XCoords[c], grid.YCoords[r],
                        grid.XCoords[c + 1] - grid.XCoords[c],
                        grid.YCoords[r + 1] - grid.YCoords[r]);

                    grid.Empty[r, c] = !OverlapsAny(cell, clipped);
                }
            }

            return grid;
        }

        private List<Box> ClipObstacles()
        {
            var clipped = new List<Box>(Obstacles.Count);

            foreach (var obs in Obstacles)
            {
                var c = ClipToWorkArea(obs);
                if (c.Width > 0 && c.Length > 0)
                    clipped.Add(c);
            }

            return clipped;
        }

        private static bool OverlapsAny(Box cell, List<Box> obstacles)
        {
            foreach (var obs in obstacles)
            {
                if (cell.Left < obs.Right && cell.Right > obs.Left &&
                    cell.Bottom < obs.Top && cell.Top > obs.Bottom)
                    return true;
            }

            return false;
        }

        private static List<Box> FilterBySize(List<Box> boxes, double minDimension)
        {
            if (minDimension <= 0)
                return boxes;

            var result = new List<Box>();

            foreach (var box in boxes)
            {
                if (box.Width >= minDimension && box.Length >= minDimension)
                    result.Add(box);
            }

            return result;
        }

        private static List<Box> RemoveDominated(List<Box> boxes)
        {
            boxes.Sort((a, b) => b.Area().CompareTo(a.Area()));
            var results = new List<Box>();

            foreach (var box in boxes)
            {
                var dominated = false;

                foreach (var larger in results)
                {
                    if (IsContainedIn(box, larger))
                    {
                        dominated = true;
                        break;
                    }
                }

                if (!dominated)
                    results.Add(box);
            }

            return results;
        }

        private static bool IsContainedIn(Box inner, Box outer)
        {
            var eps = Math.Tolerance.Epsilon;
            return inner.Left >= outer.Left - eps &&
                   inner.Right <= outer.Right + eps &&
                   inner.Bottom >= outer.Bottom - eps &&
                   inner.Top <= outer.Top + eps;
        }

        private void SortByEdgeProximity(List<Box> boxes)
        {
            boxes.Sort((a, b) =>
            {
                var aEdge = TouchesEdge(a) ? 1 : 0;
                var bEdge = TouchesEdge(b) ? 1 : 0;

                if (aEdge != bEdge)
                    return bEdge.CompareTo(aEdge);

                return b.Area().CompareTo(a.Area());
            });
        }

        private bool TouchesEdge(Box box)
        {
            return box.Left <= workArea.Left + Math.Tolerance.Epsilon
                || box.Right >= workArea.Right - Math.Tolerance.Epsilon
                || box.Bottom <= workArea.Bottom + Math.Tolerance.Epsilon
                || box.Top >= workArea.Top - Math.Tolerance.Epsilon;
        }

        private Box ComputeEnvelope()
        {
            var envLeft = double.MaxValue;
            var envBottom = double.MaxValue;
            var envRight = double.MinValue;
            var envTop = double.MinValue;

            foreach (var obs in Obstacles)
            {
                if (obs.Left < envLeft) envLeft = obs.Left;
                if (obs.Bottom < envBottom) envBottom = obs.Bottom;
                if (obs.Right > envRight) envRight = obs.Right;
                if (obs.Top > envTop) envTop = obs.Top;
            }

            return new Box(envLeft, envBottom, envRight - envLeft, envTop - envBottom);
        }

        private static void SplitAtEnvelope(Box remnant, Box envelope, double minDim, List<TieredRemnant> results)
        {
            var eps = Math.Tolerance.Epsilon;

            // Fully within the envelope.
            if (remnant.Left >= envelope.Left - eps && remnant.Right <= envelope.Right + eps &&
                remnant.Bottom >= envelope.Bottom - eps && remnant.Top <= envelope.Top + eps)
            {
                results.Add(new TieredRemnant(remnant, 0));
                return;
            }

            // Fully outside the envelope (no overlap).
            if (remnant.Left >= envelope.Right - eps || remnant.Right <= envelope.Left + eps ||
                remnant.Bottom >= envelope.Top - eps || remnant.Top <= envelope.Bottom + eps)
            {
                results.Add(new TieredRemnant(remnant, 2));
                return;
            }

            // Partially overlapping — split at envelope edges.
            var innerLeft = System.Math.Max(remnant.Left, envelope.Left);
            var innerBottom = System.Math.Max(remnant.Bottom, envelope.Bottom);
            var innerRight = System.Math.Min(remnant.Right, envelope.Right);
            var innerTop = System.Math.Min(remnant.Top, envelope.Top);

            // Inner portion (priority 0).
            TryAdd(results, innerLeft, innerBottom, innerRight - innerLeft, innerTop - innerBottom, 0, minDim);

            // Edge extensions (priority 1).
            if (remnant.Right > envelope.Right + eps)
                TryAdd(results, envelope.Right, remnant.Bottom, remnant.Right - envelope.Right, remnant.Length, 1, minDim);

            if (remnant.Left < envelope.Left - eps)
                TryAdd(results, remnant.Left, remnant.Bottom, envelope.Left - remnant.Left, remnant.Length, 1, minDim);

            if (remnant.Top > envelope.Top + eps)
                TryAdd(results, innerLeft, envelope.Top, innerRight - innerLeft, remnant.Top - envelope.Top, 1, minDim);

            if (remnant.Bottom < envelope.Bottom - eps)
                TryAdd(results, innerLeft, remnant.Bottom, innerRight - innerLeft, envelope.Bottom - remnant.Bottom, 1, minDim);

            // Corner extensions (priority 2).
            if (remnant.Right > envelope.Right + eps && remnant.Top > envelope.Top + eps)
                TryAdd(results, envelope.Right, envelope.Top, remnant.Right - envelope.Right, remnant.Top - envelope.Top, 2, minDim);

            if (remnant.Right > envelope.Right + eps && remnant.Bottom < envelope.Bottom - eps)
                TryAdd(results, envelope.Right, remnant.Bottom, remnant.Right - envelope.Right, envelope.Bottom - remnant.Bottom, 2, minDim);

            if (remnant.Left < envelope.Left - eps && remnant.Top > envelope.Top + eps)
                TryAdd(results, remnant.Left, envelope.Top, envelope.Left - remnant.Left, remnant.Top - envelope.Top, 2, minDim);

            if (remnant.Left < envelope.Left - eps && remnant.Bottom < envelope.Bottom - eps)
                TryAdd(results, remnant.Left, remnant.Bottom, envelope.Left - remnant.Left, envelope.Bottom - remnant.Bottom, 2, minDim);
        }

        private static void TryAdd(List<TieredRemnant> results, double x, double y, double w, double h, int priority, double minDim)
        {
            if (w >= minDim && h >= minDim)
                results.Add(new TieredRemnant(new Box(x, y, w, h), priority));
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

        /// <summary>
        /// Finds maximal empty rectangles using the histogram method.
        /// For each row, builds a height histogram of consecutive empty cells
        /// above, then extracts the largest rectangles from the histogram.
        /// </summary>
        private static List<Box> MergeCells(CellGrid grid)
        {
            var height = new int[grid.Rows, grid.Cols];

            for (var c = 0; c < grid.Cols; c++)
            {
                for (var r = 0; r < grid.Rows; r++)
                    height[r, c] = grid.Empty[r, c] ? (r > 0 ? height[r - 1, c] + 1 : 1) : 0;
            }

            var candidates = new List<Box>();

            for (var r = 0; r < grid.Rows; r++)
            {
                var stack = new Stack<(int startCol, int h)>();

                for (var c = 0; c <= grid.Cols; c++)
                {
                    var h = c < grid.Cols ? height[r, c] : 0;
                    var startCol = c;

                    while (stack.Count > 0 && stack.Peek().h > h)
                    {
                        var top = stack.Pop();
                        startCol = top.startCol;

                        candidates.Add(new Box(
                            grid.XCoords[top.startCol], grid.YCoords[r - top.h + 1],
                            grid.XCoords[c] - grid.XCoords[top.startCol],
                            grid.YCoords[r + 1] - grid.YCoords[r - top.h + 1]));
                    }

                    if (h > 0)
                        stack.Push((startCol, h));
                }
            }

            return candidates;
        }
    }
}

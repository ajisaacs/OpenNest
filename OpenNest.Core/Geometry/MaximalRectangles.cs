using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace OpenNest.Geometry;

/// <summary>
/// Maximal empty axis-aligned rectangles: rectangles of free space that cannot grow in any
/// direction. The first result is the largest by area.
/// </summary>
public static class MaximalRectangles
{
    /// <summary>
    /// Finds maximal axis-aligned rectangles that lie wholly inside a region, such as a cutout
    /// already shrunk by the part spacing. Rectangles may touch the region's boundary but never
    /// cross it.
    /// </summary>
    /// <remarks>
    /// The grid has a line through every vertex coordinate plus <paramref name="divisions"/>
    /// evenly spaced lines per axis. A cell is free only when no edge passes through its
    /// interior and its centre is inside the region, so results are exact for regions whose
    /// edges are all horizontal or vertical. Slanted and curved edges are followed as a
    /// staircase: results stay inside, but can fall short of the true maximum by up to about
    /// one cell on each side. Rotate the region to search other rectangle angles.
    /// </remarks>
    /// <param name="region">Closed, non-crossing paths, as returned by a Clipper Boolean or offset.
    /// A point is inside when an odd number of paths enclose it, so holes are subtracted.</param>
    /// <param name="minDimension">Rectangles narrower than this in either axis are dropped.</param>
    /// <param name="divisions">Even subdivisions of the region's bounds per axis, which bound the
    /// staircase loss along slanted edges.</param>
    /// <returns>Rectangles not contained in another result, largest area first.</returns>
    public static List<Box> InRegion(PathsD region, double minDimension = 0, int divisions = 64)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentOutOfRangeException.ThrowIfLessThan(divisions, 1);

        var paths = region.Where(path => path.Count >= 3).ToList();
        if (paths.Count == 0)
            return new List<Box>();
        if (paths.Any(path => path.Any(point => !double.IsFinite(point.x) || !double.IsFinite(point.y))))
            throw new ArgumentException("Region coordinates must be finite.", nameof(region));

        var bounds = Clipper.GetBounds(new PathsD(paths));
        var xs = GridLines(paths.SelectMany(path => path).Select(point => point.x), bounds.left, bounds.right, divisions);
        var ys = GridLines(paths.SelectMany(path => path).Select(point => point.y), bounds.top, bounds.bottom, divisions);
        if (xs.Count < 2 || ys.Count < 2)
            return new List<Box>();

        var rows = ys.Count - 1;
        var cols = xs.Count - 1;
        var crossed = new bool[rows, cols];
        foreach (var path in paths)
        {
            var previous = path[^1];
            foreach (var current in path)
            {
                MarkCrossedCells(previous, current, xs, ys, crossed);
                previous = current;
            }
        }

        var empty = new bool[rows, cols];
        var crossings = new List<double>();
        for (var r = 0; r < rows; r++)
        {
            // Even-odd scan along the row's centre line. No vertex lies on it, and an edge that
            // meets it strictly inside a cell has already marked that cell crossed, so each
            // uncrossed cell is on the same side as its centre.
            var y = (ys[r] + ys[r + 1]) / 2;
            crossings.Clear();
            foreach (var path in paths)
            {
                var previous = path[^1];
                foreach (var current in path)
                {
                    if ((previous.y > y) != (current.y > y))
                        crossings.Add(previous.x + (y - previous.y) * (current.x - previous.x) / (current.y - previous.y));
                    previous = current;
                }
            }
            crossings.Sort();

            var passed = 0;
            for (var c = 0; c < cols; c++)
            {
                var x = (xs[c] + xs[c + 1]) / 2;
                while (passed < crossings.Count && crossings[passed] < x)
                    passed++;
                empty[r, c] = !crossed[r, c] && passed % 2 == 1;
            }
        }

        return FromGrid(xs, ys, empty, minDimension);
    }

    /// <summary>
    /// Finds the maximal rectangles of empty cells in a rectilinear grid, using the histogram
    /// method: for each row, a height histogram of consecutive empty cells below it, scanned
    /// with a stack.
    /// </summary>
    /// <param name="xs">Ascending column boundaries; column c spans xs[c] to xs[c + 1].</param>
    /// <param name="ys">Ascending row boundaries; row r spans ys[r] to ys[r + 1].</param>
    /// <param name="empty">Free cells, indexed [row, column].</param>
    /// <param name="minDimension">Rectangles narrower than this in either axis are dropped.</param>
    /// <returns>Rectangles not contained in another result, largest area first.</returns>
    public static List<Box> FromGrid(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        bool[,] empty,
        double minDimension = 0
    )
    {
        var merged = MergeCells(xs, ys, empty);
        var sized = FilterBySize(merged, minDimension);
        return RemoveDominated(sized);
    }

    private static List<double> GridLines(IEnumerable<double> vertices, double min, double max, int divisions)
    {
        var lines = new SortedSet<double>(vertices);
        var exact = lines.ToList();
        var step = (max - min) / divisions;
        for (var i = 1; i < divisions; i++)
        {
            // Skip even lines that would only cut a sliver off a vertex line.
            var line = min + i * step;
            var index = exact.BinarySearch(line);
            if (index >= 0)
                continue;
            index = ~index;
            var near = (index > 0 && line - exact[index - 1] < Math.Tolerance.Epsilon)
                || (index < exact.Count && exact[index] - line < Math.Tolerance.Epsilon);
            if (!near)
                lines.Add(line);
        }
        return lines.ToList();
    }

    /// <summary>Marks every cell whose open interior a slanted edge passes through.</summary>
    private static void MarkCrossedCells(PointD a, PointD b, List<double> xs, List<double> ys, bool[,] crossed)
    {
        // Edges along a grid line touch cells without entering them; vertex coordinates
        // are grid lines, so every horizontal or vertical edge lies on one.
        if (a.x == b.x || a.y == b.y)
            return;

        var c0 = xs.BinarySearch(System.Math.Min(a.x, b.x));
        var c1 = xs.BinarySearch(System.Math.Max(a.x, b.x));
        var r0 = ys.BinarySearch(System.Math.Min(a.y, b.y));
        var r1 = ys.BinarySearch(System.Math.Max(a.y, b.y));
        for (var r = r0; r < r1; r++)
        {
            for (var c = c0; c < c1; c++)
            {
                if (!crossed[r, c] && EntersInterior(a, b, xs[c], ys[r], xs[c + 1], ys[r + 1]))
                    crossed[r, c] = true;
            }
        }
    }

    /// <summary>
    /// Clips the segment to the closed cell (Liang-Barsky). A segment that enters the open
    /// interior has the midpoint of its clipped piece strictly inside; one that only touches
    /// a side or corner does not.
    /// </summary>
    private static bool EntersInterior(PointD a, PointD b, double left, double bottom, double right, double top)
    {
        var dx = b.x - a.x;
        var dy = b.y - a.y;
        var t0 = 0.0;
        var t1 = 1.0;
        if (
            !Clip(-dx, a.x - left, ref t0, ref t1)
            || !Clip(dx, right - a.x, ref t0, ref t1)
            || !Clip(-dy, a.y - bottom, ref t0, ref t1)
            || !Clip(dy, top - a.y, ref t0, ref t1)
        )
            return false;

        var t = (t0 + t1) / 2;
        var x = a.x + t * dx;
        var y = a.y + t * dy;
        return x > left && x < right && y > bottom && y < top;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0)
            return q >= 0;
        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > t1)
                return false;
            if (ratio > t0)
                t0 = ratio;
        }
        else
        {
            if (ratio < t0)
                return false;
            if (ratio < t1)
                t1 = ratio;
        }
        return true;
    }

    private static List<Box> MergeCells(IReadOnlyList<double> xs, IReadOnlyList<double> ys, bool[,] empty)
    {
        var rows = empty.GetLength(0);
        var cols = empty.GetLength(1);
        var height = new int[rows, cols];

        for (var c = 0; c < cols; c++)
        {
            for (var r = 0; r < rows; r++)
                height[r, c] = empty[r, c] ? (r > 0 ? height[r - 1, c] + 1 : 1) : 0;
        }

        var candidates = new List<Box>();

        for (var r = 0; r < rows; r++)
        {
            var stack = new Stack<(int startCol, int h)>();

            for (var c = 0; c <= cols; c++)
            {
                var h = c < cols ? height[r, c] : 0;
                var startCol = c;

                while (stack.Count > 0 && stack.Peek().h > h)
                {
                    var top = stack.Pop();
                    startCol = top.startCol;

                    candidates.Add(
                        new Box(
                            xs[top.startCol],
                            ys[r - top.h + 1],
                            xs[c] - xs[top.startCol],
                            ys[r + 1] - ys[r - top.h + 1]
                        )
                    );
                }

                if (h > 0)
                    stack.Push((startCol, h));
            }
        }

        return candidates;
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
        return inner.Left >= outer.Left - eps
            && inner.Right <= outer.Right + eps
            && inner.Bottom >= outer.Bottom - eps
            && inner.Top <= outer.Top + eps;
    }
}

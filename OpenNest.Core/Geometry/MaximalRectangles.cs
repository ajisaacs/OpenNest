using System.Collections.Generic;

namespace OpenNest.Geometry;

/// <summary>
/// Maximal empty axis-aligned rectangles: rectangles of free space that cannot grow in any
/// direction. The first result is the largest by area.
/// </summary>
public static class MaximalRectangles
{
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

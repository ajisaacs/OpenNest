using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Proposes a serpentine whole-part order for large plates whose parts sit in aligned rows and
/// columns: each row in turn, alternating direction, starting from the corner nearest the tool.
/// Part centers are binned into rows by Y and columns by X within a quarter of the smallest part
/// extent; a plate is a grid when every gap between neighboring rows and columns is wider than
/// that, no two parts share a cell and the cells are at least a quarter occupied (interleaved
/// rows fill half). Rows run along whichever axis gives the shorter route. It replaces the
/// traveling-salesman improvement, whose passes grow with the square of the part count, and,
/// like that order, is only a proposal: the forward search still checks every rapid and lead.
/// </summary>
internal static class CuttingGridOrder
{
    /// <summary>Smaller plates keep the traveling-salesman order, which is fast enough there.</summary>
    internal const int MinimumParts = 200;

    /// <summary>The bin tolerance as a fraction of the smallest part extent on that axis.</summary>
    internal const double BinFraction = 0.25;

    /// <summary>The fewest grid cells, as a fraction, that must hold a part.</summary>
    internal const double MinimumOccupancy = 0.25;

    /// <param name="extents">Each part's cut extent, by ordinal.</param>
    /// <param name="start">The modeled tool position before the first part.</param>
    /// <returns>The serpentine order of every ordinal, or null when the parts do not form a grid.</returns>
    internal static int[] Plan(IReadOnlyList<Box> extents, Vector start, CancellationToken token)
    {
        var count = extents.Count;
        if (count < MinimumParts)
            return null;
        var centers = extents.Select(box => box.Center).ToArray();
        var rows = Bins(centers.Select(c => c.Y).ToArray(),
            BinFraction * extents.Min(box => box.Top - box.Bottom), token);
        var columns = Bins(centers.Select(c => c.X).ToArray(),
            BinFraction * extents.Min(box => box.Right - box.Left), token);
        if (rows == null || columns == null)
            return null;
        var rowCount = rows.Max() + 1;
        var columnCount = columns.Max() + 1;
        if (rowCount < 2 || columnCount < 2 || count < MinimumOccupancy * rowCount * (double)columnCount)
            return null;
        var cells = new HashSet<(int, int)>();
        for (var part = 0; part < count; part++)
            if (!cells.Add((rows[part], columns[part])))
                return null;

        token.ThrowIfCancellationRequested();
        int[] best = null;
        var bestLength = double.PositiveInfinity;
        foreach (var (lines, along) in new[] { (rows, columns), (columns, rows) })
            foreach (var reverseLines in new[] { false, true })
                foreach (var reverseFirst in new[] { false, true })
                {
                    var order = Serpentine(lines, along, reverseLines, reverseFirst);
                    var length = Length(order, centers, start);
                    if (length < bestLength)
                    {
                        best = order;
                        bestLength = length;
                    }
                }
        return best;
    }

    /// <summary>
    /// Bin index per value: sorted values open a new bin once they pass the bin's first value by
    /// more than <paramref name="tolerance"/>. Null when two bins are not separated by more than
    /// the tolerance, since the values then drift rather than line up.
    /// </summary>
    internal static int[] Bins(IReadOnlyList<double> values, double tolerance, CancellationToken token)
    {
        if (!double.IsFinite(tolerance) || tolerance <= Tolerance.Epsilon || values.Any(v => !double.IsFinite(v)))
            return null;
        var sorted = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToArray();
        var bins = new int[values.Count];
        var bin = 0;
        var first = values[sorted[0]];
        var previous = first;
        foreach (var index in sorted)
        {
            token.ThrowIfCancellationRequested();
            var value = values[index];
            if (value - first > tolerance)
            {
                if (value - previous <= tolerance)
                    return null;
                bin++;
                first = value;
            }
            bins[index] = bin;
            previous = value;
        }
        return bins;
    }

    // Lines in bin order (or reversed), each sorted along the other axis, alternating direction.
    private static int[] Serpentine(int[] lines, int[] along, bool reverseLines, bool reverseFirst)
    {
        var groups = Enumerable.Range(0, lines.Length).GroupBy(part => lines[part])
            .OrderBy(group => reverseLines ? -group.Key : group.Key).ToArray();
        var order = new List<int>(lines.Length);
        for (var line = 0; line < groups.Length; line++)
        {
            var forward = (line % 2 == 0) != reverseFirst;
            order.AddRange(forward ? groups[line].OrderBy(part => along[part])
                : groups[line].OrderByDescending(part => along[part]));
        }
        return order.ToArray();
    }

    private static double Length(int[] order, Vector[] centers, Vector start)
    {
        var length = start.DistanceTo(centers[order[0]]);
        for (var i = 1; i < order.Length; i++)
            length += centers[order[i - 1]].DistanceTo(centers[order[i]]);
        return length;
    }
}

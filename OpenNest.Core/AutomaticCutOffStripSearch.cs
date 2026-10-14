using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace OpenNest;

/// <summary>Exact layered search over sorted positions; each transition uses a sliding minimum.</summary>
internal static class AutomaticCutOffStripSearch
{
    internal static double[] FindPositions(double start, double end, int count, double spacing,
        double minimum, double step, Func<double, bool> allowed,
        Func<double, (double Length, int SegmentCount)> measure)
    {
        var rows = new List<double[]>(count);
        var nominal = new double[count];
        for (var line = 1; line <= count; line++)
        {
            var today = start + line * spacing;
            nominal[line - 1] = today;
            var low = System.Math.Max(start + line * minimum, end - (count - line + 1) * spacing);
            var high = System.Math.Min(today, end - AutomaticCutOffPlanner.GeometryTolerance);
            var positions = new List<double>();
            // Integer indices keep candidates on the origin-based lattice without cumulative drift.
            var first = (long)System.Math.Ceiling(low / step);
            var last = (long)System.Math.Floor(high / step);
            for (var index = first; index <= last; index++)
            {
                var position = index * step;
                if (position >= low && position <= high && allowed(position))
                    positions.Add(position);
            }
            if (allowed(today))
                positions.Add(today);
            positions.Sort();
            for (var index = positions.Count - 1; index > 0; index--)
                if (positions[index] == positions[index - 1])
                    positions.RemoveAt(index);
            if (positions.Count == 0)
                return null;
            rows.Add(positions.ToArray());
        }
        return Solve(start, rows, nominal, spacing, minimum, measure);
    }

    // Kept independent of geometry so tests can compare against exhaustive, non-greedy oracles.
    internal static double[] Solve(double start, IReadOnlyList<double[]> rows,
        IReadOnlyList<double> nominal, double spacing, double minimum,
        Func<double, (double Length, int SegmentCount)> measure)
    {
        var previous = new List<State> { new() { Position = start } };
        for (var line = 0; line < rows.Count; line++)
        {
            var current = new List<State>(rows[line].Length);
            var deque = new int[previous.Count];
            var head = 0;
            var tail = 0;
            var added = 0;
            foreach (var position in rows[line])
            {
                var upper = position - minimum + AutomaticCutOffPlanner.GeometryTolerance;
                var lower = position - spacing - AutomaticCutOffPlanner.GeometryTolerance;
                while (added < previous.Count && previous[added].Position <= upper)
                {
                    while (tail > head && Compare(previous[added], previous[deque[tail - 1]]) < 0)
                        tail--;
                    deque[tail++] = added++;
                }
                while (head < tail && previous[deque[head]].Position < lower)
                    head++;
                if (head == tail)
                    continue;
                var parent = previous[deque[head]];
                var cost = measure(position);
                current.Add(new State
                {
                    Position = position,
                    Length = parent.Length + RoundedMicroUnits(cost.Length),
                    Pierces = parent.Pierces + cost.SegmentCount,
                    Deviation = parent.Deviation + System.Math.Abs(nominal[line] - position),
                    Parent = parent,
                });
            }
            if (current.Count == 0)
                return null;
            AssignLexicographicRanks(current, previous.Count);
            previous = current;
        }
        State best = null;
        foreach (var state in previous)
            if (best == null || Compare(state, best) < 0)
                best = state;
        var result = new double[rows.Count];
        for (var line = result.Length - 1; line >= 0; line--)
        {
            result[line] = best.Position;
            best = best.Parent;
        }
        return result;
    }

    private static BigInteger RoundedMicroUnits(double length)
    {
        if (!double.IsFinite(length) || length < 0)
            throw new ArgumentException("Cut-off segment length must be finite and nonnegative.", nameof(length));
        var rounded = System.Math.Round(length, 6);
        // A shortest round-trip decimal may spell a different integer near 1e16
        // (for example 26833294515438512d prints as 26833294515438510).
        // An already-integral double is exact; never round it through text.
        if (rounded == System.Math.Truncate(rounded))
            return new BigInteger(rounded) * 1_000_000;
        // Compare sums of the specified decimal-rounded model units, not binary
        // approximations such as 0.1 + 0.2 versus 0.3. The round-trip string
        // retains micro-unit digits for fractional scores.
        var value = decimal.Parse(rounded.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        return new BigInteger(decimal.Round(value * 1_000_000m, 0));
    }

    private static int Compare(State left, State right)
    {
        var comparison = left.Length.CompareTo(right.Length);
        if (comparison == 0)
            comparison = left.Pierces.CompareTo(right.Pierces);
        if (comparison == 0)
            comparison = left.Deviation.CompareTo(right.Deviation);
        return comparison != 0 ? comparison : left.LexicographicRank.CompareTo(right.LexicographicRank);
    }

    private static void AssignLexicographicRanks(List<State> states, int parentCount)
    {
        // Stable counting sort by prefix rank. Positions are already sorted, so within a
        // prefix their order is lexicographic. Ranking and transitions are linear per row.
        var offsets = new int[parentCount];
        foreach (var state in states)
            offsets[state.Parent.LexicographicRank]++;
        var total = 0;
        for (var index = 0; index < offsets.Length; index++)
        {
            var count = offsets[index];
            offsets[index] = total;
            total += count;
        }
        foreach (var state in states)
            state.LexicographicRank = offsets[state.Parent.LexicographicRank]++;
    }

    private sealed class State
    {
        public double Position;
        public BigInteger Length;
        public int Pierces;
        public double Deviation;
        public State Parent;
        public int LexicographicRank;
    }
}

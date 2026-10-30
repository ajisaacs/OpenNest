#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.FlatBar;

/// <summary>
/// One part instance to cut from a bar. Length is its material length less its self-nesting
/// gain, plus one spacing; Gain is how far a copy slides into the one before it, which the
/// first copy of a run does not get back.
/// </summary>
internal sealed record BarItem(int Type, double Length, int Priority, double Gain);

/// <summary>One free run of a packed bar and the items assigned to it, in cutting order.</summary>
internal sealed class PackedSegment
{
    public PackedSegment(double capacity) => Capacity = capacity;

    public double Capacity { get; }
    public List<BarItem> Items { get; } = new();

    /// <summary>
    /// Items plus one gain per type: runs are cut longest first, so each type forms one run
    /// whose first copy has nothing of its own type to nest into.
    /// </summary>
    public double Used => System.Math.Round(
        Items.Sum(i => i.Length) + Items.GroupBy(i => i.Type).Sum(g => g.First().Gain), 8);

    public double Remaining => System.Math.Round(Capacity - Used, 8);

    public bool Fits(BarItem item) =>
        item.Length + (Items.Any(i => i.Type == item.Type) ? 0 : item.Gain) <= Remaining;
}

/// <summary>One packed bar: a segment per free run of the stock's length.</summary>
internal sealed class PackedBar
{
    public PackedBar(IEnumerable<PackedSegment> segments) => Segments = segments.ToList();

    public IReadOnlyList<PackedSegment> Segments { get; }
    public IEnumerable<BarItem> Items => Segments.SelectMany(s => s.Items);
    public int Count => Segments.Sum(s => s.Items.Count);
    public double Utilization => Segments.Sum(s => s.Used) / Segments.Sum(s => s.Capacity);
}

/// <summary>Bars for one stock, best-utilized first, and the items that did not fit.</summary>
internal sealed record BarPlan(IReadOnlyList<PackedBar> Bars, IReadOnlyList<BarItem> Unplaced);

/// <summary>
/// One-dimensional cut-list packer, ported from CutList.Core (the saw cut-list optimizer).
/// Lengths are "inflated": every item carries one part spacing and every segment's capacity
/// gets one spacing back, so a run of items needs spacing between neighbors but none after the
/// last, exactly like a saw kerf in CutList's <c>Bin</c>.
///
/// Small jobs (at most <see cref="ExhaustiveItemLimit"/> items, one free run per bar, unlimited
/// bars) use CutList's exhaustive search for the fewest bars, then least waste. Everything else
/// uses its AdvancedFit pipeline: first-fit decreasing bar by bar, then the swap optimization
/// that replaces a shorter group in a bar with unplaced items that fill it better. DuplicateBins
/// is omitted: it only acts on leftovers after the bar cap, when no bar can be added anyway.
///
/// OpenNest additions: items are ordered by priority before length, so a lower-number tier is
/// packed first and wins scarce bars, and the swap step never trades an item for a later tier.
/// The exhaustive search counts its nodes and stops at a fixed budget, keeping the best plan found.
/// </summary>
internal static class BarPacker
{
    /// <summary>Largest item count CutList solves exhaustively (about 84 ms worst case there).</summary>
    public const int ExhaustiveItemLimit = 25;

    /// <summary>Exhaustive search node budget; counted, so the result is deterministic.</summary>
    public const long ExhaustiveNodeBudget = 2_000_000;

    /// <summary>
    /// How far a part may overhang the work area. Kept just inside the layout check's
    /// <see cref="NestTolerances.WorkAreaSlack"/>, matching the Rectangles lane.
    /// </summary>
    public const double OverhangAllowance = NestTolerances.WorkAreaSlack * 0.9;

    public static BarPlan Pack(
        IReadOnlyList<BarItem> items, IReadOnlyList<double> capacities, int maxBars, CancellationToken token)
    {
        var advanced = AdvancedFit(items, capacities, maxBars, token);
        if (items.Count > ExhaustiveItemLimit || capacities.Count != 1 || maxBars < items.Count
            || advanced.Unplaced.Count > 0 || advanced.Bars.Count <= 1)
            return advanced;
        var exhaustive = Exhaustive(items, capacities[0], advanced.Bars.Count, token);
        return exhaustive != null && exhaustive.Bars.Count < advanced.Bars.Count ? exhaustive : advanced;
    }

    private static BarPlan AdvancedFit(
        IReadOnlyList<BarItem> items, IReadOnlyList<double> capacities, int maxBars, CancellationToken token)
    {
        var remaining = items
            .Select((item, order) => (item, order))
            .OrderBy(x => x.item.Priority)
            .ThenByDescending(x => x.item.Length)
            .ThenBy(x => x.item.Type)
            .ThenBy(x => x.order)
            .Select(x => x.item)
            .ToList();
        var oversized = remaining.Where(i => capacities.All(c => i.Length + i.Gain > c)).ToList();
        remaining.RemoveAll(oversized.Contains);

        // First fit decreasing, one bar at a time (CutList FirstFitDecreasingStep).
        var bars = new List<PackedBar>();
        while (remaining.Count > 0 && bars.Count < maxBars)
        {
            token.ThrowIfCancellationRequested();
            var bar = new PackedBar(capacities.Select(c => new PackedSegment(c)));
            foreach (var segment in bar.Segments)
                for (var i = 0; i < remaining.Count; i++)
                {
                    if (!segment.Fits(remaining[i]))
                        continue;
                    segment.Items.Add(remaining[i]);
                    remaining.RemoveAt(i);
                    i--;
                }
            if (bar.Count == 0)
                break;
            bars.Add(bar);
        }

        // Swap optimization (CutList OptimizationStep), per free run.
        foreach (var segment in bars.SelectMany(b => b.Segments))
            while (TryImprove(segment, remaining))
                token.ThrowIfCancellationRequested();

        return Finish(bars, remaining.Concat(oversized).ToList());
    }

    /// <summary>
    /// Takes out one item of a length group (never the longest group) and refills the freed
    /// space greedily from the unplaced items, keeping the swap only when the run ends fuller.
    /// Only items of the same or a lower-number priority may replace the removed one.
    /// </summary>
    private static bool TryImprove(PackedSegment segment, List<BarItem> remaining)
    {
        if (segment.Items.Count == 0 || remaining.Count < 2)
            return false;

        var groups = segment.Items
            .GroupBy(i => i.Length)
            .OrderByDescending(g => g.Key)
            .Skip(1)
            .Select(g => g.First())
            .ToList();
        var shortest = remaining.Min(i => i.Length);

        foreach (var removed in groups)
        {
            var before = segment.Remaining;
            segment.Items.Remove(removed);
            var free = segment.Remaining;

            for (var i = 0; i < remaining.Count; i++)
            {
                var first = remaining[i];
                // Charging the trial its own gains is conservative for types already in the run.
                var trial = new PackedSegment(free);
                if (!trial.Fits(first) || first.Priority > removed.Priority)
                    continue;
                trial.Items.Add(first);
                for (var j = i + 1; j < remaining.Count; j++)
                {
                    if (trial.Remaining < shortest)
                        break;
                    var next = remaining[j];
                    if (!trial.Fits(next) || next.Priority > removed.Priority)
                        continue;
                    trial.Items.Add(next);
                }

                // Judge the swap on the merged run: the trial charges gains again for types the
                // run already holds, so trading a copy for an identical one looks fuller and the
                // swap would repeat forever.
                var merged = new PackedSegment(segment.Capacity);
                merged.Items.AddRange(segment.Items);
                merged.Items.AddRange(trial.Items);
                if (merged.Remaining < before)
                {
                    remaining.Add(removed);
                    segment.Items.AddRange(trial.Items);
                    foreach (var item in trial.Items)
                        remaining.Remove(item);
                    return true;
                }
            }

            segment.Items.Add(removed);
        }
        return false;
    }

    /// <summary>
    /// Branch-and-bound for the fewest bars (CutList ExhaustiveFitEngine): items longest first,
    /// each tried in every open bar and then a new one, pruned by bar count and a volume bound,
    /// with identical items kept in non-decreasing bar order. Returns null when the budget
    /// ran out before any plan better than <paramref name="barsToBeat"/> was found.
    /// </summary>
    private static BarPlan? Exhaustive(
        IReadOnlyList<BarItem> items, double capacity, int barsToBeat, CancellationToken token)
    {
        var sorted = items
            .Select((item, order) => (item, order))
            .OrderByDescending(x => x.item.Length)
            .ThenBy(x => x.item.Type)
            .ThenBy(x => x.order)
            .Select(x => x.item)
            .ToList();
        var suffix = new double[sorted.Count + 1];
        for (var i = sorted.Count - 1; i >= 0; i--)
            suffix[i] = suffix[i + 1] + sorted[i].Length;

        var current = new List<List<BarItem>>();
        var used = new List<double>();
        var typeCount = items.Max(i => i.Type) + 1;
        var runs = new List<int[]>();
        List<List<BarItem>>? best = null;
        var bestCount = barsToBeat;
        long nodes = 0;
        var lastBar = 0;

        void Search(int index)
        {
            if (++nodes > ExhaustiveNodeBudget)
                return;
            if ((nodes & 0xFFF) == 0)
                token.ThrowIfCancellationRequested();
            if (index == sorted.Count)
            {
                // Equal bar counts hold the same items, so they waste the same length.
                if (current.Count < bestCount)
                {
                    bestCount = current.Count;
                    best = current.Select(b => b.ToList()).ToList();
                }
                return;
            }
            if (current.Count >= bestCount)
                return;
            var free = used.Sum(u => capacity - u);
            var overflow = suffix[index] - free;
            var more = overflow > 0 ? (int)System.Math.Ceiling(overflow / capacity) : 0;
            if (current.Count + more >= bestCount)
                return;

            var item = sorted[index];
            var minBar = index > 0 && sorted[index - 1] == item ? lastBar : 0;
            for (var b = minBar; b < current.Count; b++)
            {
                var need = item.Length + (runs[b][item.Type] == 0 ? item.Gain : 0);
                if (need > System.Math.Round(capacity - used[b], 8))
                    continue;
                current[b].Add(item);
                used[b] += need;
                runs[b][item.Type]++;
                var previous = lastBar;
                lastBar = b;
                Search(index + 1);
                lastBar = previous;
                runs[b][item.Type]--;
                used[b] -= need;
                current[b].RemoveAt(current[b].Count - 1);
            }

            if (current.Count + 1 < bestCount && item.Length + item.Gain <= capacity)
            {
                current.Add(new List<BarItem> { item });
                used.Add(item.Length + item.Gain);
                var run = new int[typeCount];
                run[item.Type] = 1;
                runs.Add(run);
                var previous = lastBar;
                lastBar = current.Count - 1;
                Search(index + 1);
                lastBar = previous;
                runs.RemoveAt(runs.Count - 1);
                used.RemoveAt(used.Count - 1);
                current.RemoveAt(current.Count - 1);
            }
        }

        Search(0);
        if (best == null)
            return null;

        var bars = best.Select(items =>
        {
            var segment = new PackedSegment(capacity);
            segment.Items.AddRange(items);
            return new PackedBar(new[] { segment });
        }).ToList();
        return Finish(bars, Array.Empty<BarItem>());
    }

    /// <summary>Best-fill search node budget per run; counted, so the result is deterministic.</summary>
    public const long BestFillNodeBudget = 200_000;

    /// <summary>
    /// Fills one bar as full as possible from the demand (the sequential heuristic of
    /// cutting-stock practice), run by run: a bounded subset-sum search over part types, longest
    /// first, with each type's gain charged once. The most urgent priority tier is filled first
    /// and later tiers only fill what it leaves. Returns null when nothing fits.
    /// </summary>
    public static PackedBar? BestFill(IReadOnlyList<BarItem> items, IReadOnlyList<double> capacities, CancellationToken token)
    {
        var pool = items
            .GroupBy(i => i.Type)
            .Select(g => (Item: g.First(), Count: g.Count()))
            .OrderBy(t => t.Item.Priority)
            .ThenByDescending(t => t.Item.Length)
            .ThenBy(t => t.Item.Type)
            .ToList();
        var counts = pool.Select(t => t.Count).ToArray();
        var tiers = pool.Select(t => t.Item.Priority).Distinct().ToList();
        var bar = new PackedBar(capacities.Select(c => new PackedSegment(c)));
        foreach (var segment in bar.Segments)
            foreach (var tier in tiers)
            {
                var members = Enumerable.Range(0, pool.Count).Where(i => pool[i].Item.Priority == tier).ToList();
                var chosen = FillRun(pool, counts, members, segment, token);
                for (var m = 0; m < members.Count; m++)
                {
                    var index = members[m];
                    for (var k = 0; k < chosen[m]; k++)
                        segment.Items.Add(pool[index].Item);
                    counts[index] -= chosen[m];
                }
            }
        if (bar.Count == 0)
            return null;
        return Finish(new List<PackedBar> { bar }, Array.Empty<BarItem>()).Bars[0];
    }

    /// <summary>Copies of each member type that use the most of the run's remaining length.</summary>
    private static int[] FillRun(List<(BarItem Item, int Count)> pool, int[] counts, List<int> members,
        PackedSegment segment, CancellationToken token)
    {
        var free = segment.Remaining;
        var n = members.Count;
        // A type already in the run (from an earlier tier) has already paid its gain.
        var gains = members.Select(i => segment.Items.Any(x => x.Type == pool[i].Item.Type) ? 0 : pool[i].Item.Gain).ToArray();
        // The objective is packed length; gains only consume room.
        var suffix = new double[n + 1];
        for (var m = n - 1; m >= 0; m--)
            suffix[m] = suffix[m + 1] + counts[members[m]] * pool[members[m]].Item.Length;
        var current = new int[n];
        var best = new int[n];
        var bestPacked = 0.0;
        long nodes = 0;

        void Search(int m, double used, double packed)
        {
            if (++nodes > BestFillNodeBudget || bestPacked >= free - 1e-9)
                return;
            if ((nodes & 0xFFF) == 0)
                token.ThrowIfCancellationRequested();
            if (packed > bestPacked)
            {
                bestPacked = packed;
                Array.Copy(current, best, n);
            }
            if (m == n || packed + System.Math.Min(suffix[m], free - used) <= bestPacked + 1e-9)
                return;
            var item = pool[members[m]].Item;
            var room = System.Math.Round(free - used, 8);
            var most = room < item.Length + gains[m] ? 0
                : System.Math.Min(counts[members[m]], (int)System.Math.Floor((room - gains[m]) / item.Length + 1e-9));
            for (var k = most; k >= 0; k--)
            {
                var need = k == 0 ? 0 : k * item.Length + gains[m];
                if (need > room)
                    continue;
                current[m] = k;
                Search(m + 1, used + need, packed + k * item.Length);
            }
            current[m] = 0;
        }

        Search(0, 0, 0);
        return best;
    }

    /// <summary>Sorts each run longest first (CutList SortBinItemsStep), then bars fullest first.</summary>
    private static BarPlan Finish(List<PackedBar> bars, IReadOnlyList<BarItem> unplaced)
    {
        foreach (var segment in bars.SelectMany(b => b.Segments))
        {
            var ordered = segment.Items
                .OrderByDescending(i => i.Length)
                .ThenBy(i => i.Priority)
                .ThenBy(i => i.Type)
                .ToList();
            segment.Items.Clear();
            segment.Items.AddRange(ordered);
        }
        var sorted = bars
            .Select((bar, order) => (bar, order))
            .OrderByDescending(x => x.bar.Utilization)
            .ThenBy(x => x.bar.Count)
            .ThenBy(x => x.order)
            .Select(x => x.bar)
            .ToList();
        return new BarPlan(sorted, unplaced);
    }
}

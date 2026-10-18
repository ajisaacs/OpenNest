#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.FlatBar;

/// <summary>
/// Flat-bar nesting engine: every stock is read as a bar (its long work-area axis is the length,
/// its short axis the width) and parts are cut from it in a single row, as a saw cut list cuts
/// lengths from stock. Built for parts about as wide as the bar, where nesting is a
/// one-dimensional problem; narrower parts still nest validly but waste the rest of the width.
///
/// Each part is reduced to its material length at the shortest rotation whose cross extent fits
/// the bar, less the length a following copy gains by nesting into it. Lengths are packed with
/// the CutList cut-list packer and a best-fill search (<see cref="BarPacker"/>), then the chosen
/// bar is laid out with a slide-left compaction pass that nests shaped ends together and refills
/// the length it frees (<see cref="BarLayout"/>).
///
/// Bar by bar, each available stock offers its cut-list plan's fullest bar and its best single
/// fill; each is laid out and the rest of the demand is priced at the cheapest plan any stock
/// offers for it. The bar with the best estimate wins: fewest unplaced parts of the most urgent
/// priority tier, then fewest unplaced parts, then lowest salvage-credited cost.
/// Deterministic: no clocks or randomness; the only stop besides completion is the host token.
/// </summary>
public sealed class FlatBarNestingEngine : INestingEngine
{
    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();

        var types = BarCatalog.Build(job);
        var bars = job.Plates.Select(stock => BarStock.Build(stock, types)).ToList();
        var remaining = types.Select(t => t.Part.Quantity).ToArray();
        // Parts with unreadable geometry or no rotation that fits any bar can never be placed.
        foreach (var t in types)
            if (!bars.Any(bar => Fits(bar, t.Index)))
                remaining[t.Index] = 0;

        var layout = new BarLayout(types);
        var used = job.Plates.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var result = new NestJobResultBuilder(job, progress);
        var penalty = NestJobCost.UnplacedPartPenalty(job);
        NestJobStopReason reason;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (remaining.All(r => r == 0))
            {
                reason = NestJobStopReason.NoPlacementFound; // Builder reports Completed when demand is met.
                break;
            }
            var sheets = job.Plates.Sum(result.SheetsUsed);
            if (job.Options.MaxPlates is int cap && sheets >= cap)
            {
                reason = NestJobStopReason.PlateLimitReached;
                break;
            }

            var trials = new List<(BarSheet Sheet, Estimate Estimate)>();
            foreach (var bar in bars)
            {
                token.ThrowIfCancellationRequested();
                var maxBars = MaxBars(job, bar.Stock, used, sheets, 0);
                if (maxBars == 0)
                    continue;
                progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, bar.Stock.Id, sheets, sheets, 0));
                var items = Items(bar, types, layout, remaining);
                if (items.Count == 0)
                    continue;
                var capacities = Capacities(bar);
                var plan = BarPacker.Pack(items, capacities, maxBars, token);
                // The cut-list plan's fullest bar, and the fullest single bar the demand allows.
                var candidates = new List<PackedBar>();
                if (plan.Bars.Count > 0)
                    candidates.Add(plan.Bars[0]);
                if (BarPacker.BestFill(items, capacities, token) is { } fill
                    && !candidates.Any(c => SameItems(c, fill)))
                    candidates.Add(fill);

                used[bar.Stock.Id]++;
                foreach (var candidate in candidates)
                {
                    var sheet = layout.Lay(bar, candidate, remaining, token);
                    if (sheet.Parts.Count == 0)
                        continue;
                    var rest = remaining.ToArray();
                    foreach (var (type, count) in sheet.Counts())
                        rest[type] -= count;
                    var restEstimate = bars
                        .Select(other => EstimatePlan(job, other, types, layout, rest, MaxBars(job, other.Stock, used, sheets, 1), penalty, token))
                        .Append(Estimate.Leaving(types, rest, penalty))
                        .Min()!;
                    trials.Add((sheet, restEstimate.Add(NetCost(job, sheet.Bar.Stock, sheet.Envelope))));
                }
                used[bar.Stock.Id]--;
            }

            if (trials.Count == 0)
            {
                var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                reason = exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound;
                break;
            }

            var chosen = trials
                .Select((t, order) => (t.Sheet, t.Estimate, order))
                .OrderBy(t => t.Estimate)
                .ThenByDescending(t => t.Sheet.Parts.Count)
                .ThenBy(t => t.order)
                .First()
                .Sheet;
            result.AddSheet(chosen.Bar.Stock, chosen.Poses()); // Builder owns fresh placements/instance indices.
            used[chosen.Bar.Stock.Id]++;
            foreach (var (type, count) in chosen.Counts())
                remaining[type] -= count;
        }

        return result.Build(reason);
    }

    private static bool Fits(BarStock bar, int type)
    {
        var length = bar.Lengths[type];
        return !double.IsNaN(length) && bar.Segments.Any(s => length <= s.Length + BarPacker.OverhangAllowance);
    }

    /// <summary>Bars this stock may still supply, after <paramref name="pending"/> uncommitted ones.</summary>
    private static int MaxBars(NestJob job, NestPlateStock stock, Dictionary<string, int> used, int sheets, int pending)
    {
        var max = int.MaxValue;
        if (stock.Quantity is int quantity)
            max = System.Math.Max(0, quantity - used[stock.Id]);
        if (job.Options.MaxPlates is int cap)
            max = System.Math.Min(max, System.Math.Max(0, cap - sheets - pending));
        return max;
    }

    private static List<BarItem> Items(BarStock bar, IReadOnlyList<BarType> types, BarLayout layout, int[] demand)
    {
        var items = new List<BarItem>();
        foreach (var type in types)
        {
            if (demand[type.Index] == 0 || !Fits(bar, type.Index))
                continue;
            var gain = layout.SelfGain(bar, type);
            for (var i = 0; i < demand[type.Index]; i++)
                items.Add(new BarItem(type.Index, bar.Lengths[type.Index] - gain + bar.Spacing, type.Priority, gain));
        }
        return items;
    }

    private static List<double> Capacities(BarStock bar) => bar.Segments.Select(s => s.Length + bar.Spacing).ToList();

    private static bool SameItems(PackedBar a, PackedBar b) =>
        a.Segments.Count == b.Segments.Count
        && a.Segments.Zip(b.Segments).All(p => p.First.Items.Select(i => i.Type)
            .SequenceEqual(p.Second.Items.Select(i => i.Type)));

    /// <summary>
    /// Prices the demand as one stock's plan, using its packed envelopes: the cheaper of the
    /// cut-list plan and repeated best fills, so the estimate does not favour bars that leave
    /// first-fit-decreasing an awkward remainder.
    /// </summary>
    private static Estimate EstimatePlan(NestJob job, BarStock bar, IReadOnlyList<BarType> types, BarLayout layout,
        int[] demand, int maxBars, double penalty, CancellationToken token)
    {
        if (demand.All(d => d == 0))
            return Estimate.Zero;
        if (maxBars == 0)
            return Estimate.Leaving(types, demand, penalty);
        var items = Items(bar, types, layout, demand);
        if (items.Count == 0)
            return Estimate.Leaving(types, demand, penalty);
        var capacities = Capacities(bar);
        var plan = BarPacker.Pack(items, capacities, maxBars, token);
        var estimate = Price(job, bar, types, demand, plan.Bars, penalty);

        var fills = new List<PackedBar>();
        var left = demand.ToArray();
        while (fills.Count < System.Math.Min(maxBars, plan.Bars.Count + 1))
        {
            var fill = BarPacker.BestFill(Items(bar, types, layout, left), capacities, token);
            if (fill == null)
                break;
            fills.Add(fill);
            foreach (var item in fill.Items)
                left[item.Type]--;
        }
        var filled = Price(job, bar, types, demand, fills, penalty);
        return filled.CompareTo(estimate) < 0 ? filled : estimate;
    }

    private static Estimate Price(NestJob job, BarStock bar, IReadOnlyList<BarType> types, int[] demand,
        IEnumerable<PackedBar> packed, double penalty)
    {
        var left = demand.ToArray();
        var cost = 0.0;
        foreach (var one in packed)
        {
            cost += NetCost(job, bar.Stock, PackedEnvelope(bar, types, one));
            foreach (var item in one.Items)
                left[item.Type]--;
        }
        return Estimate.Leaving(types, left, penalty).Add(cost);
    }

    private static Box? PackedEnvelope(BarStock bar, IReadOnlyList<BarType> types, PackedBar packed)
    {
        var parts = new List<BarPart>();
        for (var s = 0; s < packed.Segments.Count; s++)
        {
            var cursor = bar.Segments[s].Start;
            BarItem? previous = null;
            foreach (var item in packed.Segments[s].Items)
            {
                if (previous?.Type == item.Type)
                    cursor -= item.Gain;
                var orientation = bar.Fits[item.Type][0];
                var end = cursor + bar.Lengths[item.Type];
                parts.Add(new BarPart(types[item.Type], orientation, cursor, end));
                cursor = end + bar.Spacing;
                previous = item;
            }
        }
        return BarLayout.Envelope(bar, parts);
    }

    private static double NetCost(NestJob job, NestPlateStock stock, Box? envelope) =>
        envelope is { } box ? NestJobCost.NetSheetCost(job.Options, stock, box) : NestJobCost.GrossSheetCost(stock);

    /// <summary>
    /// Whole-job estimate, compared field by field: unplaced parts of the most urgent tier
    /// with unplaced demand, then all unplaced parts, then cost including their penalty.
    /// </summary>
    private readonly record struct Estimate(int Tier, int UrgentUnplaced, int Unplaced, double Cost)
        : IComparable<Estimate>
    {
        public static Estimate Zero => new(int.MaxValue, 0, 0, 0);

        public static Estimate Leaving(IReadOnlyList<BarType> types, int[] left, double penalty)
        {
            var open = types.Where(t => left[t.Index] > 0).ToList();
            if (open.Count == 0)
                return Zero;
            var tier = open.Min(t => t.Priority);
            var count = open.Sum(t => left[t.Index]);
            return new Estimate(tier, open.Where(t => t.Priority == tier).Sum(t => left[t.Index]), count, count * penalty);
        }

        public Estimate Add(double cost) => this with { Cost = NestJobCost.RequireFinite(Cost + cost) };

        public int CompareTo(Estimate other)
        {
            // A more urgent tier left unplaced is worse than any later tier left unplaced.
            if (Tier != other.Tier)
                return other.Tier.CompareTo(Tier);
            var c = UrgentUnplaced.CompareTo(other.UrgentUnplaced);
            if (c != 0)
                return c;
            c = Unplaced.CompareTo(other.Unplaced);
            return c != 0 ? c : Cost.CompareTo(other.Cost);
        }
    }
}

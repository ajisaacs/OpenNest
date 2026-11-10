#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Rectangles;

/// <summary>
/// Squeezes the sheet that finishes the job. The fit rules pack into the whole sheet, so when the
/// remaining demand leaves room they spread it out and rarely find the shortest strip that holds
/// it: on N26-2A5 (14 door stops and 14 lock plates on a 26 x 26 clamped sheet) every rule
/// stopped at 8.0 high while a 7.5 strip holds all 28. When some packing places all the remaining
/// demand, this re-packs it into a bin cut shorter (then, separately, narrower), running every rule
/// and mode at each probe, and returns the tightest complete layout per axis when it beats the
/// unsqueezed ones; the stock loop then picks by cost and edge drop as usual.
///
/// Whether a greedy packing fits is far from monotonic in the bin size (that job packs at 7.5 but
/// not at 7.62 or 7.74), and a tight layout ends where a stack of spaced part sides ends. So the
/// probes are those stack heights: sums of the parts' spaced sides along the axis, starting from
/// the bin edge or the far edge of a clamp keep-out the layout reaches. They walk down from the
/// best extent so far, each step spread over what is left of the range for the probes left, and a
/// failed probe never raises the floor. When there are too many sums to list, the walk steps
/// through the size range instead. A probe that places everything moves the walk down to that
/// layout's own extent, so the answer never keeps a probe's slack.
/// Deterministic, and bounded by a work budget over demand size: large jobs skip it entirely.
/// </summary>
internal static class SheetSqueeze
{
    /// <summary>Most probes per axis.</summary>
    internal const int MaxProbes = 12;

    /// <summary>Box placements the squeeze may spend: probes x variants x demand, both axes.</summary>
    internal const long PlacementBudget = 50_000;

    /// <summary>Most stack heights listed before the walk falls back to stepping the size range.</summary>
    private const int MaxStackHeights = 2048;

    /// <summary>A size-range walk stops once less than this fraction of the bin is left to try,
    /// and a squeezed layout must beat the whole-sheet ones by at least as much.</summary>
    private const double Resolution = 1e-3;

    /// <summary>
    /// Probes sit this far past a stack height. MaxRects already lets a box overrun by its Eps
    /// (1e-9), which covers a stack's float rounding; a margin past that Eps would hide the bin's
    /// top edge from the contact-point rule, which scores edge contact within Eps.
    /// </summary>
    private const double ProbeMargin = 1e-10;

    /// <param name="plans">The unsqueezed layouts of <paramref name="remaining"/> on this stock.</param>
    /// <param name="packAll">Every rule and mode packed under a bin limit (width, height).</param>
    public static IEnumerable<SheetPlan> Run(
        IReadOnlyList<BoxType> types, IReadOnlyList<int> remaining, NestPlateStock stock,
        IReadOnlyList<SheetPlan> plans, Func<double?, double?, IReadOnlyList<SheetPlan>> packAll,
        CancellationToken token)
    {
        var demand = remaining.Sum();
        bool Complete(SheetPlan p) => p.Parts.Count == demand && p.Envelope != null;
        var complete = plans.Where(Complete).ToList();
        if (demand == 0 || complete.Count == 0)
            yield break;

        var probes = (int)System.Math.Min(MaxProbes, PlacementBudget / (2L * demand * plans.Count));
        if (probes == 0)
            yield break;

        var work = stock.WorkArea;
        var s = stock.PartSpacing;
        var (binWidth, binHeight) = SheetPacker.BinSize(stock);

        foreach (var vertical in new[] { true, false })
        {
            double Extent(SheetPlan p) => vertical ? p.Envelope!.Top - work.Bottom : p.Envelope!.Right - work.Left;

            var bin = vertical ? binHeight : binWidth;
            var across = vertical ? binWidth : binHeight;
            var baseline = complete.Min(Extent);
            var hi = baseline + s;
            var lo = LowerBound(types, remaining, across, s, vertical);
            // How far across the squeezed axis the tightest whole-sheet layout reaches: only
            // clamps within it can hold a stack, so a zone the demand never reaches changes nothing.
            var widest = complete.Where(p => Extent(p) == baseline).Max(p => vertical
                ? p.Envelope!.Right - work.Left
                : p.Envelope!.Top - work.Bottom);
            var heights = StackHeights(types, remaining, stock, across, vertical, hi, widest);
            SheetPlan? tightest = null;
            // Probes walk down from the best extent so far, each spread over what is left of the
            // range for the probes left. A failed probe only moves the walk below it: a smaller bin
            // may still fit, so it never raises the floor.
            var cursor = hi;
            for (var probe = 0; probe < probes; probe++)
            {
                token.ThrowIfCancellationRequested();
                var target = cursor - (cursor - lo) / (probes - probe);
                double limit;
                if (heights != null)
                {
                    // The highest stack height at or under the target, else the next one down.
                    var next = heights.LastOrDefault(h => h > lo && h <= target + ProbeMargin);
                    if (next == 0)
                        next = heights.LastOrDefault(h => h > lo && h < cursor - ProbeMargin);
                    if (next == 0)
                        break;
                    limit = next + ProbeMargin;
                }
                else if (cursor - lo > Resolution * bin)
                    limit = target;
                else
                    break;

                var fits = packAll(vertical ? null : limit, vertical ? limit : null).Where(Complete).ToList();
                if (fits.Count == 0)
                {
                    cursor = limit - ProbeMargin;
                    continue;
                }
                // First of the tightest keeps enumeration order as the tie-break.
                var best = fits.Aggregate((a, b) => Extent(b) < Extent(a) ? b : a);
                tightest = best;
                cursor = System.Math.Min(limit, Extent(best) + s) - ProbeMargin;
            }

            if (tightest != null && Extent(tightest) < baseline - Resolution * bin)
                yield return tightest;
        }
    }

    /// <summary>
    /// A bin extent along the squeezed axis below which the demand cannot fit: every part needs its
    /// shortest fitting side plus spacing, and the boxes' spaced area must fit across the bin.
    /// Clamps only add to it, so ignoring them keeps the bound valid.
    /// </summary>
    private static double LowerBound(
        IReadOnlyList<BoxType> types, IReadOnlyList<int> remaining, double across, double s, bool vertical)
    {
        double fit = 0, area = 0;
        foreach (var t in types)
        {
            if (remaining[t.Index] == 0 || t.Orientations.Count == 0)
                continue;
            var sizes = Sizes(t, s, vertical).ToList();
            var fitting = sizes.Where(z => z.Across <= across + SheetPacker.OverhangAllowance).ToList();
            if (fitting.Count > 0)
                fit = System.Math.Max(fit, fitting.Min(z => z.Along));
            area += remaining[t.Index] * sizes.Min(z => z.Along * z.Across);
        }
        return System.Math.Max(fit, area / across);
    }

    /// <summary>
    /// Every bin extent below <paramref name="below"/> that a stack of spaced part sides reaches from
    /// the bin edge or the far edge of a clamp keep-out that starts within <paramref name="reach"/>
    /// across the axis, sorted; null when there are more than
    /// <see cref="MaxStackHeights"/>. Stacks ignore part counts, so some heights are unreachable;
    /// they cost a probe each, never a missed layout.
    /// </summary>
    private static List<double>? StackHeights(
        IReadOnlyList<BoxType> types, IReadOnlyList<int> remaining, NestPlateStock stock,
        double across, bool vertical, double below, double reach)
    {
        var s = stock.PartSpacing;
        var sides = types
            .Where(t => remaining[t.Index] > 0)
            .SelectMany(t => Sizes(t, s, vertical))
            .Where(z => z.Across <= across + SheetPacker.OverhangAllowance && z.Along > 0)
            .Select(z => z.Along)
            .Distinct()
            .ToArray();

        var work = stock.WorkArea;
        var bases = new List<double> { 0 };
        foreach (var zone in stock.ClampZones)
        {
            var edge = (vertical ? zone.Bounds.Top - work.Bottom : zone.Bounds.Right - work.Left)
                + stock.ClampClearance;
            var near = (vertical ? zone.Bounds.Left - work.Left : zone.Bounds.Bottom - work.Bottom)
                - stock.ClampClearance;
            if (edge > 0 && edge < below && near < reach)
                bases.Add(edge);
        }

        // Keys on a 1e-6 grid merge sums that differ only by float rounding; each keeps its
        // largest float value so a probe just past it holds every stack it stands for.
        var seen = new Dictionary<double, double>();
        var queue = new Queue<double>(bases);
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            foreach (var side in sides)
            {
                var w = v + side;
                if (w >= below - ProbeMargin)
                    continue;
                var key = System.Math.Round(w, 6);
                if (seen.TryGetValue(key, out var kept))
                {
                    if (w > kept)
                        seen[key] = w;
                    continue;
                }
                if (seen.Count >= MaxStackHeights)
                    return null;
                seen.Add(key, w);
                queue.Enqueue(w);
            }
        }
        return seen.Values.OrderBy(h => h).ToList();
    }

    private static IEnumerable<(double Along, double Across)> Sizes(BoxType type, double s, bool vertical) =>
        type.Orientations.Select(o => vertical
            ? (o.Height + s, o.Width + s)
            : (o.Width + s, o.Height + s));
}

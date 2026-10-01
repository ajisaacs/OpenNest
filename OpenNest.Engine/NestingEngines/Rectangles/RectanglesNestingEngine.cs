#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.RectanglePacking;

namespace OpenNest.Engine.NestingEngines.Rectangles;

/// <summary>
/// Rectangle-lane nesting engine: every part is nested as the axis-aligned box of its material at
/// its minimum-area rotations, packed with a maximal-rectangles free list (Jylänki 2010).
///
/// Built for jobs of plain and near-rectangular parts, where a part's box wastes almost nothing
/// and exact box packing beats contour-sliding engines on both speed and density. Irregular parts
/// are still placed validly, only as their bounding boxes; they are not nested into each other.
///
/// Sheet by sheet, each available stock is packed under several free-space scoring rules and
/// two pick modes (best-fitting box anywhere, or largest type first). The candidate sheet with
/// the lowest estimated whole-job cost wins: its salvage-credited net area (NestJobCost) plus the
/// remaining demand priced at the best net-area-per-part-area ratio seen among the candidates.
/// Deterministic: no clocks or randomness; the only stop besides completion is the host token.
/// </summary>
public sealed class RectanglesNestingEngine : INestingEngine
{
    private static readonly FitRule[] Rules =
    {
        FitRule.BestShortSide, FitRule.BestLongSide, FitRule.BestArea,
        FitRule.BottomLeft, FitRule.LeftBottom, FitRule.ContactPoint,
    };

    private static readonly PickMode[] Modes = { PickMode.Global, PickMode.Ordered };

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();

        var types = BoxCatalog.Build(job);
        var remaining = types.Select(t => t.Part.Quantity).ToArray();
        // Parts with unreadable geometry or no box that fits any offered sheet can never be placed.
        foreach (var t in types)
            if (t.Orientations.Count == 0 || !job.Plates.Any(stock => t.Orientations.Any(o => FitsStock(stock, o))))
                remaining[t.Index] = 0;

        var used = job.Plates.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var result = new NestJobResultBuilder(job, progress);
        NestJobStopReason reason;

        while (true)
        {
            if (remaining.All(r => r == 0))
            {
                reason = NestJobStopReason.NoPlacementFound; // Builder reports Completed when demand is met.
                break;
            }
            if (job.Options.MaxPlates is int cap && result.SheetsUsed(job) >= cap)
            {
                reason = NestJobStopReason.PlateLimitReached;
                break;
            }

            var trials = new List<(SheetPlan Plan, double Net)>();
            foreach (var stock in job.Plates)
            {
                token.ThrowIfCancellationRequested();
                if (stock.Quantity is int available && used[stock.Id] >= available)
                    continue;
                progress?.Report(new NestJobProgress(
                    NestJobStage.EvaluatingCandidate, stock.Id, result.SheetsUsed(job), result.SheetsUsed(job), 0));
                foreach (var mode in Modes)
                    foreach (var rule in Rules)
                    {
                        var plan = SheetPacker.Pack(types, remaining, stock, rule, mode, token);
                        if (plan.Parts.Count > 0)
                            trials.Add((plan, NetArea(job, plan)));
                    }
            }

            if (trials.Count == 0)
            {
                var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                reason = exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound;
                break;
            }

            var chosen = Choose(types, remaining, trials);
            result.AddSheet(chosen.Stock, chosen.Poses());
            used[chosen.Stock.Id]++;
            foreach (var p in chosen.Parts)
                remaining[p.Type.Index]--;
        }

        return result.Build(reason);
    }

    /// <summary>
    /// Picks the sheet with the lowest estimated whole-job cost. Remaining demand is priced at the
    /// best net-area-per-material ratio any candidate achieved, so a sheet that finishes the job
    /// competes fairly with a denser partial one. Ties: more material placed, then enumeration order.
    /// </summary>
    private static SheetPlan Choose(
        IReadOnlyList<BoxType> types, int[] remaining, List<(SheetPlan Plan, double Net)> trials)
    {
        var demandArea = types.Sum(t => remaining[t.Index] * t.MaterialArea);
        var bestRatio = trials.Min(t => t.Net / System.Math.Max(t.Plan.MaterialArea, 1e-12));
        return trials
            .Select((t, order) => (t.Plan, order,
                Estimate: t.Net + System.Math.Max(0, demandArea - t.Plan.MaterialArea) * bestRatio))
            .OrderBy(t => PriorityDebt(types, remaining, t.Plan))
            .ThenBy(t => t.Estimate)
            .ThenByDescending(t => t.Plan.MaterialArea)
            .ThenBy(t => t.order)
            .First()
            .Plan;
    }

    /// <summary>
    /// Priority guard: how many instances of the most urgent (lowest-number) tier with remaining
    /// demand this plan leaves unplaced. Plans are ranked on this before cost, so a cheaper sheet
    /// can never win by serving a later tier at the expense of an earlier one.
    /// </summary>
    private static int PriorityDebt(IReadOnlyList<BoxType> types, int[] remaining, SheetPlan plan)
    {
        var active = types.Where(t => remaining[t.Index] > 0).ToList();
        if (active.Count == 0)
            return 0;
        var top = active.Min(t => t.Priority);
        var placed = plan.Parts.Count(p => p.Type.Priority == top);
        return active.Where(t => t.Priority == top).Sum(t => remaining[t.Index]) - placed;
    }

    private static double NetArea(NestJob job, SheetPlan plan) =>
        plan.Envelope is { } envelope
            ? NestJobCost.NetSheetArea(job.Options, plan.Stock, envelope)
            : plan.Stock.Area;

    private static bool FitsStock(NestPlateStock stock, BoxOrientation o)
    {
        var work = stock.WorkArea;
        return o.Width <= work.Right - work.Left + MaxRectsSheet.Eps
            && o.Height <= work.Top - work.Bottom + MaxRectsSheet.Eps;
    }
}

internal static class ResultBuilderExtensions
{
    public static int SheetsUsed(this NestJobResultBuilder builder, NestJob job) =>
        job.Plates.Sum(builder.SheetsUsed);
}

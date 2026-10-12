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
/// the lowest estimated whole-job cost wins: its salvage-credited net cost (NestJobCost) plus the
/// remaining demand priced at the best net-cost-per-part-area ratio seen among the candidates.
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

    // Instance-owned diagnostics; no process-global counters or mutable solver state.
    internal Action<PickMode>? SheetPacked { get; init; }
    internal Action<RepeatBatch>? BatchSelected { get; init; }

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
        var parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var result = new NestJobResultBuilder(job, progress);
        NestJobStopReason reason;

        while (true)
        {
            token.ThrowIfCancellationRequested();
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
                        SheetPacked?.Invoke(mode);
                        if (plan.Parts.Count > 0)
                            trials.Add((plan, NetCost(job, plan)));
                    }
            }

            if (trials.Count == 0)
            {
                var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                reason = exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound;
                break;
            }

            var chosen = Choose(types, remaining, trials);
            var poses = chosen.Poses().ToArray();
            var demand = types.ToDictionary(t => t.Id, t => remaining[t.Index], StringComparer.Ordinal);
            var counts = chosen.Parts.GroupBy(p => p.Type.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var availableEntries = job.Plates.Count(s => s.Quantity == null || used[s.Id] < s.Quantity);
            // Both private MaxRects modes return only after their normal demand/no-fit search;
            // neither returns partial/budget-limited output. This is not a geometric impossibility proof.
            var copies = CompletedLayoutRepeat.Limit(demand, counts, searchCompleted: true, availableEntries,
                chosen.Stock.Quantity - used[chosen.Stock.Id], job.Options.MaxPlates - result.SheetsUsed(job));
            if (copies > 1)
            {
                try
                {
                    NestJobValidator.ValidateCandidate(new PlateCandidate(poses.Select(p =>
                        new NestJobPlacement(p.PartId, 0, p.X, p.Y, p.Rotation))), chosen.Stock, demand, parts);
                }
                catch (InvalidOperationException)
                {
                    // Keep the original one-sheet path if the candidate validator cannot certify
                    // reuse (including its stricter work-area tolerance). Final job validation still applies.
                    copies = 1;
                }
            }
            BatchSelected?.Invoke(new RepeatBatch(chosen.Stock.Id, copies, availableEntries, result.SheetsUsed(job), false));
            for (var copy = 0; copy < copies; copy++)
            {
                token.ThrowIfCancellationRequested();
                result.AddSheet(chosen.Stock, poses); // Builder owns fresh placements/instance indices.
            }
            used[chosen.Stock.Id] = checked(used[chosen.Stock.Id] + copies);
            foreach (var type in types)
                if (counts.TryGetValue(type.Id, out var count))
                    remaining[type.Index] = checked(remaining[type.Index] - checked(count * copies));
        }

        return result.Build(reason);
    }

    /// <summary>
    /// Picks the sheet with the lowest estimated whole-job cost. Remaining demand is priced at the
    /// best net-cost-per-material ratio any candidate achieved, so a sheet that finishes the job
    /// competes fairly with a denser partial one. Ties: more material placed, then enumeration order.
    /// </summary>
    private static SheetPlan Choose(
        IReadOnlyList<BoxType> types, int[] remaining, List<(SheetPlan Plan, double Net)> trials)
    {
        var demandArea = types.Sum(t => remaining[t.Index] * t.MaterialArea);
        var bestRatio = trials.Min(t => t.Net / System.Math.Max(t.Plan.MaterialArea, 1e-12));
        return trials
            .Select((t, order) => (t.Plan, order,
                Estimate: NestJobCost.RequireFinite(t.Net + System.Math.Max(0, demandArea - t.Plan.MaterialArea) * bestRatio)))
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

    private static double NetCost(NestJob job, SheetPlan plan) =>
        plan.Envelope is { } envelope
            ? NestJobCost.NetSheetCost(job.Options, plan.Stock, envelope)
            : NestJobCost.GrossSheetCost(plan.Stock);

    private static bool FitsStock(NestPlateStock stock, BoxOrientation o)
    {
        var work = stock.WorkArea;
        return o.Width <= work.Right - work.Left + SheetPacker.OverhangAllowance
            && o.Height <= work.Top - work.Bottom + SheetPacker.OverhangAllowance;
    }
}

internal static class ResultBuilderExtensions
{
    public static int SheetsUsed(this NestJobResultBuilder builder, NestJob job) =>
        job.Plates.Sum(builder.SheetsUsed);
}

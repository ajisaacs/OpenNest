#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines;

/// <summary>A requested part type as the stock loop sees it; <see cref="Index"/> addresses the
/// remaining-demand array.</summary>
internal sealed record SheetDemand(int Index, string Id, int Priority, double MaterialArea);

/// <summary>One proposed single-sheet layout from an engine's packer.</summary>
/// <param name="MaterialArea">Material placed, summed per type in type order so equal layouts get
/// bit-identical areas (placement-order rounding differs across platform math libraries).</param>
/// <param name="Envelope">Bounds of the placed material, or null to price the gross sheet.</param>
internal sealed record SheetLayout(
    NestPlateStock Stock,
    IReadOnlyList<(string PartId, double X, double Y, double Rotation)> Poses,
    double MaterialArea,
    Box? Envelope);

/// <summary>
/// Sheet-by-sheet stock selection shared by engines that propose whole single-sheet layouts.
/// Each round asks the engine for layouts of the remaining demand on every available stock and
/// commits the one with the lowest estimated whole-job cost: its salvage-credited net cost
/// (<see cref="NestJobCost"/>) plus the remaining demand priced at the best net-cost-per-material
/// ratio seen among the candidates. A completed layout repeats while demand and stock allow it.
/// Deterministic: no clocks or randomness; the only stop besides completion is the host token.
/// </summary>
internal static class SheetByStock
{
    /// <param name="remaining">Demand per type index; updated as sheets commit. Zero a type the
    /// engine can never place before calling.</param>
    /// <param name="pack">Layouts of the given remaining demand on one stock. Each must be the
    /// packer's completed search, not budget-limited output, so a repeat is safe.</param>
    /// <param name="preferred">When set, only this stock is tried while it is available and
    /// takes a layout; the others are tried only once it cannot.</param>
    public static NestJobResult Run(
        NestJob job,
        IReadOnlyList<SheetDemand> types,
        int[] remaining,
        Func<NestPlateStock, IReadOnlyList<int>, IEnumerable<SheetLayout>> pack,
        IProgress<NestJobProgress>? progress,
        CancellationToken token,
        Action<RepeatBatch>? batchSelected = null,
        NestPlateStock? preferred = null)
    {
        var used = job.Plates.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var priority = types.ToDictionary(t => t.Id, t => t.Priority, StringComparer.Ordinal);
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
            if (job.Options.MaxPlates is int cap && SheetsUsed(result, job) >= cap)
            {
                reason = NestJobStopReason.PlateLimitReached;
                break;
            }

            var trials = new List<(SheetLayout Layout, double Net, double Offcut)>();
            if (preferred != null)
                Try(preferred, trials);
            if (trials.Count == 0)
                foreach (var stock in job.Plates)
                    if (!ReferenceEquals(stock, preferred))
                        Try(stock, trials);

            if (trials.Count == 0)
            {
                var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                reason = exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound;
                break;
            }

            var chosen = Choose(types, remaining, priority, trials);
            var poses = chosen.Poses;
            var demand = types.ToDictionary(t => t.Id, t => remaining[t.Index], StringComparer.Ordinal);
            var counts = poses.GroupBy(p => p.PartId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var availableEntries = job.Plates.Count(s => s.Quantity == null || used[s.Id] < s.Quantity);
            var copies = CompletedLayoutRepeat.Limit(demand, counts, searchCompleted: true, availableEntries,
                chosen.Stock.Quantity - used[chosen.Stock.Id], job.Options.MaxPlates - SheetsUsed(result, job));
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
            batchSelected?.Invoke(new RepeatBatch(chosen.Stock.Id, copies, availableEntries, SheetsUsed(result, job), false));
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

        void Try(NestPlateStock stock, List<(SheetLayout Layout, double Net, double Offcut)> trials)
        {
            token.ThrowIfCancellationRequested();
            if (stock.Quantity is int available && used[stock.Id] >= available)
                return;
            progress?.Report(new NestJobProgress(
                NestJobStage.EvaluatingCandidate, stock.Id, SheetsUsed(result, job), SheetsUsed(result, job), 0));
            foreach (var layout in pack(stock, remaining))
                if (layout.Poses.Count > 0)
                    trials.Add((layout, NetCost(job, layout), Offcut(job, layout)));
        }
    }

    public static int SheetsUsed(NestJobResultBuilder builder, NestJob job) =>
        job.Plates.Sum(builder.SheetsUsed);

    /// <summary>
    /// Picks the layout with the lowest estimated whole-job cost. Remaining demand is priced at the
    /// best net-cost-per-material ratio any candidate achieved, so a sheet that finishes the job
    /// competes fairly with a denser partial one. Ties: more material placed, then the larger
    /// compactness offcut (without salvage credit every layout on one stock costs the same), then
    /// enumeration order.
    /// </summary>
    private static SheetLayout Choose(IReadOnlyList<SheetDemand> types, int[] remaining,
        IReadOnlyDictionary<string, int> priority, List<(SheetLayout Layout, double Net, double Offcut)> trials)
    {
        var demandArea = types.Sum(t => remaining[t.Index] * t.MaterialArea);
        var bestRatio = trials.Min(t => t.Net / System.Math.Max(t.Layout.MaterialArea, 1e-12));
        return trials
            .Select((t, order) => (t.Layout, t.Offcut, order,
                Estimate: NestJobCost.RequireFinite(t.Net + System.Math.Max(0, demandArea - t.Layout.MaterialArea) * bestRatio)))
            .OrderBy(t => PriorityDebt(types, remaining, priority, t.Layout))
            .ThenBy(t => t.Estimate)
            .ThenByDescending(t => t.Layout.MaterialArea)
            .ThenByDescending(t => t.Offcut)
            .ThenBy(t => t.order)
            .First()
            .Layout;
    }

    /// <summary>
    /// Priority guard: how many instances of the most urgent (lowest-number) tier with remaining
    /// demand this layout leaves unplaced. Layouts are ranked on this before cost, so a cheaper sheet
    /// can never win by serving a later tier at the expense of an earlier one.
    /// </summary>
    private static int PriorityDebt(IReadOnlyList<SheetDemand> types, int[] remaining,
        IReadOnlyDictionary<string, int> priority, SheetLayout layout)
    {
        var active = types.Where(t => remaining[t.Index] > 0).ToList();
        if (active.Count == 0)
            return 0;
        var top = active.Min(t => t.Priority);
        var placed = layout.Poses.Count(p => priority[p.PartId] == top);
        return active.Where(t => t.Priority == top).Sum(t => remaining[t.Index]) - placed;
    }

    private static double NetCost(NestJob job, SheetLayout layout) =>
        layout.Envelope is { } envelope
            ? NestJobCost.NetSheetCost(job.Options, layout.Stock, envelope)
            : NestJobCost.GrossSheetCost(layout.Stock);

    private static double Offcut(NestJob job, SheetLayout layout) =>
        layout.Envelope is { } envelope
            ? NestJobCost.TieBreakKey(NestJobCost.CompactnessOffcut(job.Options, layout.Stock, envelope))
            : 0;
}

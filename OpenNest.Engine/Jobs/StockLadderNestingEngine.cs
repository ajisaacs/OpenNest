using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
namespace OpenNest.Engine.Jobs;

/// <summary>
/// Caller-stock-only allocation followed by bounded adjacent-sheet repacking. All replacements
/// must reproduce exactly the removed demand and reduce net sheet area; inventory is transactional.
/// This is a deterministic heuristic, not an optimality or geometric impossibility proof.
/// </summary>
public sealed class StockLadderNestingEngine : INestingEngine
{
    private readonly Func<IPlateNester> factory;

    public StockLadderNestingEngine()
        : this(() => new OrderedPlateNester()) { }

    public StockLadderNestingEngine(Func<IPlateNester> factory) =>
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        NestJobValidator.Validate(job);
        var nester = factory() ?? throw new InvalidOperationException("Null plate nester.");
        var parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var remaining = job.Parts.ToDictionary(p => p.Id, p => p.Quantity, StringComparer.Ordinal);
        var used = job.Plates.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var areas = job.Parts.ToDictionary(p => p.Id, p => DrawingJobMapper.CreateDrawing(p).Area);
        var sheets = new List<NestJobPlateResult>();
        var feasible = job.Parts.ToDictionary(p => p.Id, _ => new HashSet<string>());

        // Probe actual validated single-part placements, not bounding-box fit assertions.
        foreach (var part in job.Parts)
        foreach (var stock in job.Plates.Where(s => s.Quantity != 0))
        {
            var probe = Trial(stock, new[] { WithQuantity(part, 1) });
            if (probe.Placements.Count != 0)
                feasible[part.Id].Add(stock.Id);
        }
        var ordered = job
            .Parts.OrderBy(p => p.Priority)
            .ThenBy(p => feasible[p.Id].Count)
            .ThenByDescending(p => areas[p.Id])
            .ToList();
        var reason = NestJobStopReason.Completed;
        while (remaining.Values.Any(n => n > 0))
        {
            token.ThrowIfCancellationRequested();
            if (job.Options.MaxPlates <= sheets.Count)
            {
                if (Consolidate())
                    continue;
                reason = NestJobStopReason.PlateLimitReached;
                break;
            }
            var available = job
                .Plates.Where(s => s.Quantity == null || used[s.Id] < s.Quantity)
                .ToList();
            if (available.Count == 0)
            {
                if (Consolidate())
                    continue;
                reason = NestJobStopReason.StockExhausted;
                break;
            }
            var anchor = ordered.FirstOrDefault(p =>
                remaining[p.Id] > 0 && available.Any(s => feasible[p.Id].Contains(s.Id))
            );
            if (anchor == null)
            {
                reason = NestJobStopReason.NoPlacementFound;
                break;
            }
            NestJobPlateResult winner = null;
            var score = double.PositiveInfinity;
            foreach (var stock in available.Where(s => feasible[anchor.Id].Contains(s.Id)))
            {
                // Pin the constrained anchor before fillers, including quantity-one requirements.
                var requests = new[] { anchor }
                    .Concat(ordered.Where(p => p.Id != anchor.Id))
                    .Where(p => remaining[p.Id] > 0)
                    .Select(p => WithQuantity(p, remaining[p.Id]));
                var candidate = Trial(stock, requests);
                if (!candidate.Placements.Any(p => p.PartId == anchor.Id))
                    continue;
                var sheet = new NestJobPlateResult(sheets.Count, stock, candidate.Placements);
                // Initial construction only: material area, never raw part counts. Repacking below
                // compares EXACTLY equivalent demand, and never replaces a sheet by a partial fill.
                var value =
                    NestJobCost.NetSheetArea(job, sheet) / candidate.Placements.Sum(p => areas[p.PartId]);
                if (value < score - 1e-9)
                {
                    winner = sheet;
                    score = value;
                }
            }
            if (winner == null)
            {
                reason = NestJobStopReason.NoPlacementFound;
                break;
            }
            sheets.Add(winner);
            used[winner.StockId]++;
            foreach (var pose in winner.Placements)
                remaining[pose.PartId]--;
            progress?.Report(
                new NestJobProgress(
                    NestJobStage.PlateCommitted,
                    winner.StockId,
                    sheets.Count - 1,
                    sheets.Count,
                    sheets.Sum(s => s.Placements.Count)
                )
            );
        }
        Consolidate();
        token.ThrowIfCancellationRequested();
        var placed = job.Parts.ToDictionary(p => p.Id, _ => 0);
        var final = sheets
            .Select(
                (sheet, index) =>
                    new NestJobPlateResult(
                        index,
                        sheet.Stock,
                        sheet
                            .Placements.Select(p => p with { InstanceIndex = placed[p.PartId]++ })
                            .ToList()
                    )
            )
            .ToList();
        return new NestJobResult(
            reason == NestJobStopReason.Completed
                ? NestJobStatus.Complete
                : NestJobStatus.Incomplete,
            reason,
            final,
            job.Parts.Select(p => new PartFulfillment(
                p.Id,
                p.Quantity,
                placed[p.Id],
                remaining[p.Id]
            )),
            job.Plates.Select(s => new StockUsage(s.Id, used[s.Id], s.Quantity - used[s.Id]))
        );

        PlateCandidate Trial(NestPlateStock stock, IEnumerable<NestJobPart> requirements)
        {
            token.ThrowIfCancellationRequested();
            var request = new PlatePlacementRequest(stock, requirements);
            progress?.Report(
                new NestJobProgress(
                    NestJobStage.EvaluatingCandidate,
                    stock.Id,
                    sheets.Count,
                    sheets.Count,
                    sheets.Sum(s => s.Placements.Count)
                )
            );
            var candidate = nester.Place(request, null, token);
            token.ThrowIfCancellationRequested();
            NestJobValidator.ValidateCandidate(
                candidate,
                stock,
                request.Parts.ToDictionary(p => p.Id, p => p.Quantity),
                parts
            );
            return candidate;
        }

        bool Consolidate()
        {
            var changed = false;
            // Single downgrade and adjacent pair merge only: bounded local search, no combinatorial tree.
            for (var index = 0; index < sheets.Count; index++)
            for (var count = System.Math.Min(2, sheets.Count - index); count >= 1; count--)
            {
                var old = sheets.Skip(index).Take(count).ToList();
                var demand = old.SelectMany(s => s.Placements)
                    .GroupBy(p => p.PartId)
                    .ToDictionary(g => g.Key, g => g.Count());
                var baseline = old.Sum(s => NestJobCost.NetSheetArea(job, s));
                NestJobPlateResult replacement = null;
                foreach (var stock in job.Plates)
                {
                    token.ThrowIfCancellationRequested();
                    var returned = old.Count(s => s.StockId == stock.Id);
                    if (stock.Quantity is int limit && used[stock.Id] - returned >= limit)
                        continue;
                    // Even the maximum possible salvage credit cannot beat the incumbent.
                    var lowerBound =
                        stock.Size.Width * stock.Size.Length * (1 - job.Options.SalvageRate);
                    if (lowerBound >= baseline - 1e-9)
                        continue;
                    if (demand.Keys.Any(id => !feasible[id].Contains(stock.Id)))
                        continue;
                    var candidate = Trial(
                        stock,
                        ordered
                            .Where(p => demand.ContainsKey(p.Id))
                            .Select(p => WithQuantity(p, demand[p.Id]))
                    );
                    var actual = candidate
                        .Placements.GroupBy(p => p.PartId)
                        .ToDictionary(g => g.Key, g => g.Count());
                    if (demand.Any(kv => !actual.TryGetValue(kv.Key, out var n) || n != kv.Value))
                        continue;
                    var trial = new NestJobPlateResult(index, stock, candidate.Placements);
                    var cost = NestJobCost.NetSheetArea(job, trial);
                    if (cost >= baseline - 1e-9)
                        continue;
                    baseline = cost;
                    replacement = trial;
                }
                if (replacement == null)
                    continue;
                // No accounting changes until the entire equivalent-demand candidate is valid.
                foreach (var sheet in old)
                    used[sheet.StockId]--;
                used[replacement.StockId]++;
                sheets.RemoveRange(index, count);
                sheets.Insert(index, replacement);
                changed = true;
            }
            return changed;
        }
    }

    private static NestJobPart WithQuantity(NestJobPart part, int quantity) =>
        new(part.Id, part.Geometry, quantity, part.Priority, part.Rotation);

    /// <summary>Full physical sheet area minus a conservative offcut estimate. Credits only ONE
    /// empty full-span edge rectangle outside every placed bounding box plus part clearance, within
    /// the usable work area, and meeting the caller's minimum in both dimensions. Not a certified
    /// remnant: no cut-off toolpath, kerf, handling, or future-demand valuation is modelled.</summary>
    [Obsolete("Use NestJobCost.NetSheetArea instead.")]
    public static double EstimateNetArea(NestJob job, NestJobPlateResult sheet) =>
        NestJobCost.NetSheetArea(job, sheet);
}

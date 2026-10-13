using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// D8/D9 last-resort clamp removal for the whole-job pipeline. After a normal clamped solve
/// leaves demand unmet, finds parts that cannot fit anywhere with every clamp in place (C10),
/// computes the fewest clamps whose removal lets each one fit (C11), and re-solves the
/// residual demand on stock variants with those clamps taken off (C12). Never changes the
/// result when nothing is blocked (C10's bit-identical guarantee) and never trades a removal
/// for material savings - it only runs after the ordinary solve could not place a part.
/// </summary>
internal static class ClampRemovalRecovery
{
    /// <summary>A result that replaces the caller's raw solve, and the expanded job it was
    /// validated against (the caller's own stock plus the reduced-clamp variants used).</summary>
    internal sealed record Outcome(NestJob Job, NestJobResult Result);

    /// <summary>
    /// Most engine solves one recovery may spend on feasibility probes, independent of job,
    /// stock and rail size. Running out abandons the recovery and keeps the ordinary result
    /// (no clamp is removed on an unfinished search).
    /// </summary>
    internal const int MaxProbeSolves = 1000;

    /// <summary>
    /// Null when there is nothing to recover: the job is already complete, no offered stock
    /// has any clamp, no remaining part is genuinely clamp-blocked, or removing clamps did
    /// not let anything new fit. Callers keep their original job/result unchanged in every
    /// one of those cases (C10).
    /// </summary>
    internal static Outcome TryRecover(
        NestJob job,
        NestJobResult raw,
        INestingEngine engine,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(engine);

        // Recovery is optional: any failure inside it (a throwing probe or residual solve)
        // keeps the caller's ordinary result instead of discarding it. Cancellation still
        // propagates.
        try
        {
            return Recover(job, raw, engine, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static Outcome Recover(NestJob job, NestJobResult raw, INestingEngine engine, CancellationToken token)
    {
        if (raw.Status == NestJobStatus.Complete)
            return null;

        var availableStock = job.Plates.Where(s => s.Quantity != 0).ToList();
        if (!availableStock.Any(CanRemoveClamps))
            return null;

        var partsById = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var remaining = raw.Fulfillment
            .Where(f => f.Unplaced > 0)
            .Select(f => (Part: partsById[f.PartId], Unplaced: f.Unplaced))
            .ToList();
        if (remaining.Count == 0)
            return null;

        token.ThrowIfCancellationRequested();

        // C10: only a part that fits nowhere with every clamp in place is "blocked". One
        // lane's failure is not proof: a part any built-in lane can place clamped is not.
        var lanes = FeasibilityLanes(engine);
        var budget = new ProbeBudget(MaxProbeSolves);
        var blocked = remaining
            .Where(x => !lanes.Any(lane => FitsAnywhere(x.Part, availableStock, job.Options, lane, budget, token)))
            .Select(x => x.Part)
            .ToList();
        if (blocked.Count == 0)
            return null;

        // Phase 2 state. Each kept sheet comes out of its base stock's remaining physical
        // pool and the plate cap (C12: a finite pool is never exceeded).
        var unmet = remaining.ToDictionary(x => x.Part.Id, x => x.Unplaced, StringComparer.Ordinal);
        var blockedIds = new HashSet<string>(blocked.Select(p => p.Id), StringComparer.Ordinal);
        var pool = availableStock.ToDictionary(
            s => s.Id,
            s => s.Quantity is int q ? q - raw.StockUsage.Where(u => u.StockId == s.Id).Sum(u => u.Used) : (int?)null,
            StringComparer.Ordinal);
        int? platesLeft = job.Options.MaxPlates is int cap ? cap - raw.Plates.Count : null;
        var tried = blocked.ToDictionary(p => p.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var recovered = new List<(NestPlateStock Variant, string BaseId, List<NestJobPlateResult> Sheets)>();

        // Rounds: each blocked part with demand left takes its best removal among the offered
        // stocks it has not tried that still have sheets, so a part whose preferred stock runs
        // out moves on to another stock (with that stock's own minimal removal).
        for (var round = 0; round < availableStock.Count && platesLeft is not <= 0; round++)
        {
            // C11: per blocked part, the fewest clamps (smallest contiguous run, nearest the
            // origin on ties) whose removal frees an empty sheet of one of those stocks.
            // Parts needing the same removal on the same stock share one variant.
            var groups = new List<RemovalGroup>();
            foreach (var part in blocked)
            {
                token.ThrowIfCancellationRequested();
                if (unmet[part.Id] <= 0)
                    continue;
                var usable = availableStock
                    .Where(s => pool[s.Id] is not <= 0 && !tried[part.Id].Contains(s.Id))
                    .ToList();
                if (usable.Count == 0)
                    continue;
                var best = BestRemoval(part, usable, job.Options, lanes, budget, token);
                if (best == null)
                {
                    // Unfit on all of them even with every clamp removed: stays unplaced.
                    tried[part.Id].UnionWith(usable.Select(s => s.Id));
                    continue;
                }
                tried[part.Id].Add(best.BaseStockId);
                var group = groups.FirstOrDefault(g => g.Removal.Stock.Id == best.Stock.Id);
                if (group == null)
                    groups.Add(group = new RemovalGroup(best));
                group.BlockedIds.Add(part.Id);
            }
            if (groups.Count == 0)
                break;

            // One removal variant at a time, fewest removed clamps first: each variant takes
            // its own blocked parts plus any other unmet demand that is not blocked (freed
            // space may take other parts, C12). A sheet is kept only when it holds a part
            // that needed exactly this removal (C10/C11 per sheet).
            foreach (var group in groups.OrderBy(g => g.Removal.RemovedIds.Count))
            {
                token.ThrowIfCancellationRequested();
                var baseId = group.Removal.BaseStockId;
                var available = pool[baseId];
                if (available <= 0 || platesLeft <= 0)
                    continue;
                int? sheetCap = (available, platesLeft) switch
                {
                    (int a, int p) => System.Math.Min(a, p),
                    (int a, null) => a,
                    (null, int p) => p,
                    _ => null,
                };

                var parts = remaining
                    .Where(x => unmet[x.Part.Id] > 0
                        && (group.BlockedIds.Contains(x.Part.Id) || !blockedIds.Contains(x.Part.Id)))
                    .Select(x => new NestJobPart(x.Part.Id, x.Part.Geometry, unmet[x.Part.Id], x.Part.Priority, x.Part.Rotation))
                    .ToList();
                if (!parts.Any(p => group.BlockedIds.Contains(p.Id)))
                    continue;

                var variant = Clone(group.Removal.Stock, group.Removal.Stock.Id, sheetCap, null);
                var options = new NestJobOptions(job.Options.PlacementStrategy, platesLeft,
                    job.Options.SalvageRate, job.Options.MinimumSalvageDimension);
                var groupJob = new NestJob(parts, new[] { variant }, options);

                List<NestJobPlateResult> kept = null;
                foreach (var lane in lanes)
                {
                    token.ThrowIfCancellationRequested();
                    var solved = lane.Solve(groupJob, null, token);
                    token.ThrowIfCancellationRequested();
                    kept = solved?.Plates
                        .Where(sheet => sheet.Placements.Any(p => group.BlockedIds.Contains(p.PartId)))
                        .Take(sheetCap ?? int.MaxValue)
                        .ToList();
                    if (kept is { Count: > 0 })
                        break;
                }
                if (kept is not { Count: > 0 })
                    continue;

                foreach (var placement in kept.SelectMany(sheet => sheet.Placements))
                    unmet[placement.PartId]--;
                if (pool[baseId] is int left)
                    pool[baseId] = left - kept.Count;
                if (platesLeft is int plates)
                    platesLeft = plates - kept.Count;
                recovered.Add((group.Removal.Stock, baseId, kept));
            }
        }
        if (recovered.Count == 0)
            return null;

        return Merge(job, raw, recovered);
    }

    private sealed class RemovalGroup(Removal removal)
    {
        public Removal Removal { get; } = removal;
        public HashSet<string> BlockedIds { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Engines whose placement of a lone part proves it fits (C10): the caller's own engine,
    /// plus the built-in Irregular (NFP, true material) and Rectangles lanes. Default already
    /// runs both, so it is enough on its own. Phase 2 tries them in this order too.
    /// </summary>
    private static IReadOnlyList<INestingEngine> FeasibilityLanes(INestingEngine engine)
    {
        if (engine is NestingEngines.Default.DefaultNestingEngine)
            return new[] { engine };
        var lanes = new List<INestingEngine> { engine };
        if (engine is not NestingEngines.Irregular.IrregularNestingEngine)
            lanes.Add(new NestingEngines.Irregular.IrregularNestingEngine());
        if (engine is not NestingEngines.Rectangles.RectanglesNestingEngine)
            lanes.Add(new NestingEngines.Rectangles.RectanglesNestingEngine());
        return lanes;
    }

    private sealed record Removal(NestPlateStock Stock, string BaseStockId, IReadOnlyList<string> RemovedIds);

    /// <summary>
    /// The (stock, removal) with the fewest removed clamps across every offered rail-backed
    /// stock; ties prefer the stock listed earliest, then the run nearest the origin. One
    /// all-clamps-removed probe per stock first dismisses stock the part cannot use at all
    /// and picks the lanes worth asking; the run search then goes size by size across the
    /// remaining stock and stops at the first size that fits anywhere.
    /// </summary>
    private static Removal BestRemoval(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        IReadOnlyList<INestingEngine> lanes,
        ProbeBudget budget,
        CancellationToken token
    )
    {
        var candidates = new List<(NestPlateStock Stock, List<ClampZone> Zones, List<INestingEngine> Lanes)>();
        foreach (var stock in stocks)
        {
            if (!CanRemoveClamps(stock))
                continue;
            token.ThrowIfCancellationRequested();
            // Placement-frame X is signed from the origin, so |centre X| is the distance
            // along the edge in every quadrant.
            var zones = stock.ClampZones
                .OrderBy(z => System.Math.Abs(z.Bounds.X + z.Bounds.Length / 2))
                .ToList();
            var allRemoved = Clone(stock, stock.Id, null, zones.Select(z => z.Id).ToList());
            var fitting = lanes.Where(lane => FitsAlone(part, allRemoved, options, lane, budget, token)).ToList();
            if (fitting.Count > 0)
                candidates.Add((stock, zones, fitting));
        }

        var maxRun = candidates.Count == 0 ? 0 : candidates.Max(c => c.Zones.Count);
        for (var run = 1; run <= maxRun; run++)
        {
            foreach (var (stock, zones, fitting) in candidates)
            {
                for (var start = 0; start + run <= zones.Count; start++)
                {
                    token.ThrowIfCancellationRequested();
                    var removedIds = zones.Skip(start).Take(run).Select(z => z.Id).ToList();
                    var candidate = Clone(stock, Suffixed(stock.Id, removedIds), null, removedIds);
                    // Removing every clamp was already probed.
                    if (run == zones.Count || fitting.Any(lane => FitsAlone(part, candidate, options, lane, budget, token)))
                        return new Removal(candidate, stock.Id, removedIds);
                }
            }
        }
        return null;
    }

    /// <summary>Only clamps resolved from a machine rail can be removed (see
    /// <see cref="NestPlateStock.ClampRail"/>): explicit zones have no clamp to name.</summary>
    private static bool CanRemoveClamps(NestPlateStock stock) =>
        stock.ClampRail != null && stock.ClampZones.Count > 0;

    private static bool FitsAlone(
        NestJobPart part,
        NestPlateStock stock,
        NestJobOptions options,
        INestingEngine engine,
        ProbeBudget budget,
        CancellationToken token
    ) => Probe(part, new[] { stock }, options, engine, budget, token);

    private static bool FitsAnywhere(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        INestingEngine engine,
        ProbeBudget budget,
        CancellationToken token
    ) => Probe(part, stocks, options, engine, budget, token);

    /// <summary>Whether this part, alone and at quantity one, places on an empty sheet of any
    /// of the given stock entries with the given engine. Spends one probe from the budget.</summary>
    private static bool Probe(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        INestingEngine engine,
        ProbeBudget budget,
        CancellationToken token
    )
    {
        budget.Spend();
        var probePart = new NestJobPart(part.Id, part.Geometry, 1, part.Priority, part.Rotation);
        var probeStock = stocks.Select(s => Unlimited(s)).ToList();
        var probeOptions = new NestJobOptions(options.PlacementStrategy, 1, 0, 0);
        var probeJob = new NestJob(new[] { probePart }, probeStock, probeOptions);
        token.ThrowIfCancellationRequested();
        var result = engine.Solve(probeJob, null, token);
        return result != null && result.Status == NestJobStatus.Complete;
    }

    private sealed class ProbeBudget(int solves)
    {
        private int left = solves;

        /// <summary>Throws <see cref="ProbeBudgetExhaustedException"/> once spent; recovery
        /// then keeps the ordinary result.</summary>
        public void Spend()
        {
            if (left-- <= 0)
                throw new ProbeBudgetExhaustedException();
        }
    }

    private sealed class ProbeBudgetExhaustedException()
        : Exception("Clamp removal recovery ran out of feasibility probes.");

    /// <summary>Same stock with unlimited quantity (feasibility probes never consume real
    /// inventory); id/zones unchanged.</summary>
    private static NestPlateStock Unlimited(NestPlateStock stock) =>
        stock.Quantity == null ? stock : Clone(stock, stock.Id, null, null);

    /// <summary>
    /// A copy of <paramref name="stock"/> with a new id/quantity and, when
    /// <paramref name="removedIds"/> is given, those clamp zones dropped. Built from the
    /// stock's own sheet-frame zones, so the kept zones are bit-identical to the original's.
    /// </summary>
    private static NestPlateStock Clone(
        NestPlateStock stock,
        string id,
        int? quantity,
        IReadOnlyCollection<string> removedIds
    )
    {
        var sheetZones = stock.SheetClampZones
            .Where(z => removedIds == null || !removedIds.Contains(z.Id))
            .ToList();
        return new NestPlateStock(
            id,
            stock.Size,
            quantity,
            stock.PartSpacing,
            stock.EdgeSpacing,
            stock.Quadrant,
            stock.Cost,
            sheetZones
        )
        { ClampRail = stock.ClampRail };
    }

    private static string Suffixed(string stockId, IReadOnlyList<string> removedIds) =>
        $"{stockId}::removed:{string.Join(",", removedIds)}";

    /// <summary>
    /// Combines the ordinary solve with the kept recovery sheets into one result, and the
    /// expanded job it validates against: the caller's stock with each finite base reduced
    /// by the sheets its variants took, plus each variant with exactly the sheets it used
    /// (unlimited when its base is), so the expanded inventory equals the physical one.
    /// Recovery placements get fresh instance indices continuing on from phase 1's.
    /// </summary>
    private static Outcome Merge(
        NestJob job,
        NestJobResult raw,
        IReadOnlyList<(NestPlateStock Variant, string BaseId, List<NestJobPlateResult> Sheets)> recovered
    )
    {
        // A variant used in more than one round is one stock entry with all its sheets.
        recovered = recovered
            .GroupBy(r => r.Variant.Id, StringComparer.Ordinal)
            .Select(g => (g.First().Variant, g.First().BaseId, g.SelectMany(r => r.Sheets).ToList()))
            .ToList();
        var placedSoFar = raw.Fulfillment.ToDictionary(f => f.PartId, f => f.Placed, StringComparer.Ordinal);
        var nextPlateIndex = raw.Plates.Count;
        var reindexed = new List<NestJobPlateResult>();
        var variantStock = new List<NestPlateStock>();
        var takenFromBase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (variant, baseId, sheets) in recovered)
        {
            var baseStock = job.Plates.First(s => s.Id == baseId);
            var used = Clone(variant, variant.Id, baseStock.Quantity == null ? null : sheets.Count, null);
            variantStock.Add(used);
            takenFromBase[baseStock.Id] = takenFromBase.GetValueOrDefault(baseStock.Id) + sheets.Count;
            foreach (var sheet in sheets)
            {
                var placements = new List<NestJobPlacement>(sheet.Placements.Count);
                foreach (var pose in sheet.Placements)
                {
                    var index = placedSoFar.GetValueOrDefault(pose.PartId);
                    placedSoFar[pose.PartId] = index + 1;
                    placements.Add(pose with { InstanceIndex = index });
                }
                reindexed.Add(new NestJobPlateResult(nextPlateIndex++, used, placements));
            }
        }

        var allPlates = raw.Plates.Concat(reindexed).ToList();
        var fulfillment = job.Parts
            .Select(p =>
            {
                var placed = placedSoFar.GetValueOrDefault(p.Id);
                return new PartFulfillment(p.Id, p.Quantity, placed, p.Quantity - placed);
            })
            .ToList();

        var used1 = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in raw.StockUsage)
            used1[u.StockId] = used1.GetValueOrDefault(u.StockId) + u.Used;

        var expandedStock = job.Plates
            .Select(s => s.Quantity is int q && takenFromBase.TryGetValue(s.Id, out var taken)
                ? Clone(s, s.Id, q - taken, null)
                : s)
            .Concat(variantStock)
            .ToList();
        var stockUsage = expandedStock
            .Select(s =>
            {
                var usedCount = used1.GetValueOrDefault(s.Id) + reindexed.Count(p => p.StockId == s.Id);
                return new StockUsage(s.Id, usedCount, s.Quantity is int q ? q - usedCount : null);
            })
            .ToList();

        var complete = fulfillment.All(f => f.Unplaced == 0);
        var status = complete ? NestJobStatus.Complete : raw.Status;
        var stopReason = complete ? NestJobStopReason.Completed : raw.StopReason;

        var expandedJob = new NestJob(job.Parts, expandedStock, job.Options);
        var result = new NestJobResult(status, stopReason, allPlates, fulfillment, stockUsage);
        return new Outcome(expandedJob, result);
    }
}

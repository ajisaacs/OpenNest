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

        if (raw.Status == NestJobStatus.Complete)
            return null;

        var availableStock = job.Plates.Where(s => s.Quantity != 0).ToList();
        if (!availableStock.Any(s => s.ClampZones.Count > 0))
            return null;

        var partsById = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var remaining = raw.Fulfillment
            .Where(f => f.Unplaced > 0)
            .Select(f => (Part: partsById[f.PartId], Unplaced: f.Unplaced))
            .ToList();
        if (remaining.Count == 0)
            return null;

        token.ThrowIfCancellationRequested();

        // C10: only a part that fits nowhere with every clamp in place is "blocked".
        var blocked = remaining
            .Where(x => !FitsAnywhere(x.Part, availableStock, job.Options, engine, token))
            .Select(x => x.Part)
            .ToList();
        if (blocked.Count == 0)
            return null;

        // C11: per blocked part, the fewest clamps (smallest contiguous run, nearest the
        // origin on ties) whose removal frees an empty sheet of some offered stock.
        var recoveryVariants = new Dictionary<string, NestPlateStock>(StringComparer.Ordinal);
        var baseIdsNeedingRemoval = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in blocked)
        {
            token.ThrowIfCancellationRequested();
            var best = BestRemoval(part, availableStock, job.Options, engine, token);
            if (best == null)
                continue; // Unfit even with every clamp removed: stays unplaced, as today.
            recoveryVariants[best.Stock.Id] = best.Stock;
            baseIdsNeedingRemoval.Add(best.BaseStockId);
        }
        if (recoveryVariants.Count == 0)
            return null;

        // Phase 2: solve the whole residual demand (not only the blocked parts - freed space
        // may also take other unmet parts, C12) on the untouched stock plus the reduced-clamp
        // variants. A base stock that needed removal is not also offered full-clamp here, so
        // a finite physical pool is never double-counted across both forms.
        var usedByStock = raw.StockUsage.ToDictionary(u => u.StockId, u => u.Used, StringComparer.Ordinal);
        var carryForward = availableStock
            .Where(s => !baseIdsNeedingRemoval.Contains(s.Id))
            .Select(s => ResidualCopy(s, usedByStock))
            .Where(s => s.Quantity != 0);
        var residualStock = carryForward.Concat(recoveryVariants.Values).ToList();

        int? residualMaxPlates = job.Options.MaxPlates is int cap
            ? System.Math.Max(0, cap - raw.Plates.Count)
            : null;
        if (residualMaxPlates == 0)
            return null;

        var residualParts = remaining
            .Select(x => new NestJobPart(x.Part.Id, x.Part.Geometry, x.Unplaced, x.Part.Priority, x.Part.Rotation))
            .ToList();
        var residualOptions = new NestJobOptions(
            job.Options.PlacementStrategy,
            residualMaxPlates,
            job.Options.SalvageRate,
            job.Options.MinimumSalvageDimension
        );
        var residualJob = new NestJob(residualParts, residualStock, residualOptions);

        token.ThrowIfCancellationRequested();
        var raw2 = engine.Solve(residualJob, null, token);
        token.ThrowIfCancellationRequested();
        if (raw2 == null || raw2.Plates.Count == 0)
            return null;

        var merged = Merge(job, raw, recoveryVariants.Values, raw2);
        return merged;
    }

    /// <summary>Same stock, quantity reduced by what phase 1 already used (unlimited stays
    /// unlimited); id/zones unchanged. Used for stock phase 2 may still draw from.</summary>
    private static NestPlateStock ResidualCopy(NestPlateStock stock, IReadOnlyDictionary<string, int> usedByStock) =>
        stock.Quantity is int quantity
            ? Clone(stock, stock.Id, System.Math.Max(0, quantity - usedByStock.GetValueOrDefault(stock.Id)), null)
            : stock;

    private sealed record Removal(NestPlateStock Stock, string BaseStockId, IReadOnlyList<string> RemovedIds);

    /// <summary>The (stock, removal) with the fewest removed clamps across every offered
    /// stock that has any; ties prefer the stock listed earliest.</summary>
    private static Removal BestRemoval(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        INestingEngine engine,
        CancellationToken token
    )
    {
        Removal best = null;
        foreach (var stock in stocks)
        {
            if (stock.ClampZones.Count == 0)
                continue;
            token.ThrowIfCancellationRequested();
            var found = MinimalRemovalForStock(part, stock, options, engine, token);
            if (found == null)
                continue;
            if (best == null || found.RemovedIds.Count < best.RemovedIds.Count)
                best = found;
        }
        return best;
    }

    /// <summary>
    /// Ascending run size, then ascending start index: the first contiguous run of the
    /// stock's own clamp zones (already ordered by rail position) whose removal lets the
    /// part fit alone is both the fewest clamps and, among ties, the run nearest the origin.
    /// </summary>
    private static Removal MinimalRemovalForStock(
        NestJobPart part,
        NestPlateStock stock,
        NestJobOptions options,
        INestingEngine engine,
        CancellationToken token
    )
    {
        var zones = stock.ClampZones;
        for (var run = 1; run <= zones.Count; run++)
        {
            for (var start = 0; start + run <= zones.Count; start++)
            {
                token.ThrowIfCancellationRequested();
                var removedIds = zones.Skip(start).Take(run).Select(z => z.Id).ToList();
                var candidate = Clone(stock, Suffixed(stock.Id, removedIds), null, removedIds);
                if (FitsAlone(part, candidate, options, engine, token))
                    return new Removal(candidate, stock.Id, removedIds);
            }
        }
        return null;
    }

    private static bool FitsAlone(
        NestJobPart part,
        NestPlateStock stock,
        NestJobOptions options,
        INestingEngine engine,
        CancellationToken token
    ) => Probe(part, new[] { stock }, options, engine, token);

    private static bool FitsAnywhere(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        INestingEngine engine,
        CancellationToken token
    ) => Probe(part, stocks, options, engine, token);

    /// <summary>Whether this part, alone and at quantity one, places on an empty sheet of any
    /// of the given stock entries. Uses the caller's own selected engine, so the answer
    /// matches whatever lane/rotation handling Auto Nest would actually use.</summary>
    private static bool Probe(
        NestJobPart part,
        IReadOnlyList<NestPlateStock> stocks,
        NestJobOptions options,
        INestingEngine engine,
        CancellationToken token
    )
    {
        var probePart = new NestJobPart(part.Id, part.Geometry, 1, part.Priority, part.Rotation);
        var probeStock = stocks.Select(s => Unlimited(s)).ToList();
        var probeOptions = new NestJobOptions(options.PlacementStrategy, 1, 0, 0);
        var probeJob = new NestJob(new[] { probePart }, probeStock, probeOptions);
        token.ThrowIfCancellationRequested();
        var result = engine.Solve(probeJob, null, token);
        return result != null && result.Status == NestJobStatus.Complete;
    }

    /// <summary>Same stock with unlimited quantity (feasibility probes never consume real
    /// inventory); id/zones unchanged.</summary>
    private static NestPlateStock Unlimited(NestPlateStock stock) =>
        stock.Quantity == null ? stock : Clone(stock, stock.Id, null, null);

    /// <summary>
    /// A copy of <paramref name="stock"/> with a new id/quantity and, when
    /// <paramref name="removedIds"/> is given, those clamp zones dropped. Stock carries its
    /// zones already translated into the placement frame; they are translated back to plate
    /// coordinates and re-translated forward so the result matches what
    /// <see cref="NestPlateStock"/>'s own constructor would produce from a plate.
    /// </summary>
    private static NestPlateStock Clone(
        NestPlateStock stock,
        string id,
        int? quantity,
        IReadOnlyCollection<string> removedIds
    )
    {
        var left = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var bottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        var plateFrameZones = stock.ClampZones
            .Where(z => removedIds == null || !removedIds.Contains(z.Id))
            .Select(z => new ClampZone(z.Id, z.Bounds.Translate(-left, -bottom)))
            .ToList();
        return new NestPlateStock(
            id,
            stock.Size,
            quantity,
            stock.PartSpacing,
            stock.EdgeSpacing,
            stock.Quadrant,
            stock.Cost,
            plateFrameZones
        );
    }

    private static string Suffixed(string stockId, IReadOnlyList<string> removedIds) =>
        $"{stockId}::removed:{string.Join(",", removedIds)}";

    /// <summary>
    /// Combines the ordinary solve with the residual recovery solve into one result, and the
    /// expanded job both were validated against (the caller's stock plus the recovery
    /// variants actually used). Phase 2 placements get fresh instance indices continuing on
    /// from phase 1's, so no requirement's instances collide across the two solves.
    /// </summary>
    private static Outcome Merge(
        NestJob job,
        NestJobResult raw,
        IEnumerable<NestPlateStock> recoveryVariants,
        NestJobResult raw2
    )
    {
        var placedSoFar = raw.Fulfillment.ToDictionary(f => f.PartId, f => f.Placed, StringComparer.Ordinal);
        var nextPlateIndex = raw.Plates.Count;
        var reindexed = new List<NestJobPlateResult>(raw2.Plates.Count);
        foreach (var sheet in raw2.Plates)
        {
            var placements = new List<NestJobPlacement>(sheet.Placements.Count);
            foreach (var pose in sheet.Placements)
            {
                var index = placedSoFar.GetValueOrDefault(pose.PartId);
                placedSoFar[pose.PartId] = index + 1;
                placements.Add(pose with { InstanceIndex = index });
            }
            reindexed.Add(new NestJobPlateResult(nextPlateIndex++, sheet.Stock, placements));
        }

        var allPlates = raw.Plates.Concat(reindexed).ToList();
        var fulfillment = job.Parts
            .Select(p =>
            {
                var placed = placedSoFar.GetValueOrDefault(p.Id);
                return new PartFulfillment(p.Id, p.Quantity, placed, p.Quantity - placed);
            })
            .ToList();

        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in raw.StockUsage)
            used[u.StockId] = u.Used;
        foreach (var sheet in reindexed)
            used[sheet.StockId] = used.GetValueOrDefault(sheet.StockId) + 1;

        var expandedStock = job.Plates.Concat(recoveryVariants).ToList();
        var stockUsage = expandedStock
            .Select(s => new StockUsage(s.Id, used.GetValueOrDefault(s.Id),
                s.Quantity is int q ? q - used.GetValueOrDefault(s.Id) : null))
            .ToList();

        var complete = fulfillment.All(f => f.Unplaced == 0);
        var status = complete ? NestJobStatus.Complete : raw.Status;
        var stopReason = complete ? NestJobStopReason.Completed : raw.StopReason;

        var expandedJob = new NestJob(job.Parts, expandedStock, job.Options);
        var result = new NestJobResult(status, stopReason, allPlates, fulfillment, stockUsage);
        return new Outcome(expandedJob, result);
    }
}

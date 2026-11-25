#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Raster;

/// <summary>
/// Raster nesting engine for a fast first plan of irregular parts. Every part rotation is a
/// bit-packed mask of its material grown by half the part spacing (<see cref="RasterMask"/>);
/// coarse raster no-fit maps propose positions over the whole sheet and a finer grid settles each
/// one (<see cref="RasterSheet"/>). Masks keep the spacing by construction, so every layout passes
/// the layout check without a repair step.
///
/// The job is decoded greedily in 48 variants (largest area first, longest first and six seeded
/// shuffles of the area order; quarter turns, quarter turns plus hull-aligned turns, or eighth
/// turns as well; leftmost or behind-the-frontier positions), each sheet by sheet through
/// <see cref="SheetByStock"/>, plus one run that offers every variant's sheet in each round. The
/// best result by <see cref="CandidateScore"/> is reported as the one whole-job plan. Packing
/// tighter and opening the gaps with Fix Spacing was tried and gained under 0.1% for up to 25
/// times the time.
/// Deterministic: seeded orders, no clocks; the only stop besides completion is the host token.
/// </summary>
public sealed class RasterNestingEngine : INestingEngine
{
    /// <summary>Seeded shuffles of the area order per rotation set and policy.</summary>
    private const int Shuffles = 6;

    private const int Seed = 12345;

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();

        var types = job.Parts.Select((p, i) =>
        {
            var geometry = JobPartGeometry.TryRead(p.Geometry);
            return new RasterType(i, p, geometry, geometry?.MaterialArea ?? 0);
        }).ToList();
        // Rasters fill rectangles only; an outlined sheet is left to Irregular.
        var stocks = job.Plates.Where(s => s.Outline == null).ToList();
        var usable = types.Select(t => t.Geometry != null && t.Part.Quantity > 0).ToArray();
        var spacings = stocks.Select(s => s.PartSpacing).Distinct().Order().ToList();
        var grids = spacings.ToDictionary(s => s,
            s => RasterGrid.Create(s, stocks.Where(x => x.PartSpacing == s).ToList(), types, usable, token));
        var remaining = types.Select(t => usable[t.Index]
            && stocks.Any(s => grids[s.PartSpacing].Masks[t.Index].Any(m => Fits(s, m)))
                ? t.Part.Quantity : 0).ToArray();
        var demand = types.Select(t => new SheetDemand(t.Index, t.Id, t.Priority, t.MaterialArea)).ToArray();
        var variants = Variants(types, remaining);
        var results = new NestJobResult[variants.Count + 1];
        // Only the first run reports its stock trials, so the host hears from one thread; commits
        // belong to the result finally chosen.
        var forward = progress == null ? null : new CandidateProgress(progress);
        try
        {
            Parallel.For(0, results.Length, new ParallelOptions { CancellationToken = token }, i =>
            {
                results[i] = i < variants.Count
                    ? SheetByStock.Run(job, demand, remaining.ToArray(),
                        (stock, left) => new[] { Fill(stock, variants[i], left) }, i == 0 ? forward : null, token)
                    : SheetByStock.Run(job, demand, remaining.ToArray(),
                        (stock, left) => variants.Select(v => Fill(stock, v, left)), null, token);
            });
        }
        catch (AggregateException e) when (token.IsCancellationRequested
            && e.Flatten().InnerExceptions.All(x => x is OperationCanceledException))
        {
            throw new OperationCanceledException(token);
        }

        // Rank unchecked (masks keep the spacing), then confirm with the layout check, best first.
        var ranked = results.Select((r, i) => Score(r, i)).ToList();
        ranked.Sort((a, b) => a.IsBetterThan(b) ? -1 : b.IsBetterThan(a) ? 1 : 0);
        var best = ranked[0];
        foreach (var candidate in ranked)
        {
            token.ThrowIfCancellationRequested();
            if (NestLayoutCheck.Violations(job, candidate.Result).Count == 0)
            {
                best = candidate;
                break;
            }
        }
        CandidateProgress.ReportPlan(best.Result, progress);
        CandidateProgress.ReportCommits(best.Result, progress, token);
        return best.Result;

        SheetLayout Fill(NestPlateStock stock, RasterVariant variant, IReadOnlyList<int> left) =>
            RasterSheet.Fill(grids[stock.PartSpacing], types, stock, variant, left, token);

        CandidateScore Score(NestJobResult r, int at)
        {
            var unplaced = System.Math.Max(0, job.Parts.Sum(p => p.Quantity) - r.Plates.Sum(p => p.Placements.Count));
            return new CandidateScore(r, true, unplaced, NestJobCost.Evaluate(job, r),
                NestJobCost.CompactnessOffcut(job, r), at, NestJobCost.UsesExplicitCosts(job));
        }
    }

    private static bool Fits(NestPlateStock stock, RasterMask m) =>
        m.Length <= stock.WorkArea.Length + Rectangles.SheetPacker.OverhangAllowance
        && m.Width <= stock.WorkArea.Width + Rectangles.SheetPacker.OverhangAllowance;

    /// <summary>Orderings x rotation sets x policies. Every order serves higher priority numbers
    /// first; the plain area order with quarter turns, leftmost, comes first.</summary>
    private static List<RasterVariant> Variants(IReadOnlyList<RasterType> types, int[] remaining)
    {
        var live = types.Where(t => remaining[t.Index] > 0).ToList();
        var longest = live.ToDictionary(t => t.Index, t => System.Math.Max(t.Geometry!.Bounds.Length, t.Geometry.Bounds.Width));
        var orders = new List<IReadOnlyList<int>>
        {
            Order(t => t.MaterialArea),
            Order(t => longest[t.Index]),
        };
        var random = new Random(Seed);
        for (var i = 0; i < Shuffles; i++)
        {
            // Pure arithmetic weights: no transcendental calls, so orders match across platforms.
            var weight = live.ToDictionary(t => t.Index, t => t.MaterialArea * (0.5 + random.NextDouble()));
            orders.Add(Order(t => weight[t.Index]));
        }

        var variants = new List<RasterVariant>();
        foreach (var kinds in new[]
                 {
                     RotationKinds.QuarterTurns,
                     RotationKinds.QuarterTurns | RotationKinds.HullAligned,
                     RotationKinds.All,
                 })
            foreach (var policy in new[] { RasterPolicy.Leftmost, RasterPolicy.Frontier })
                foreach (var order in orders)
                    variants.Add(new RasterVariant(order, kinds, policy));
        return variants;

        IReadOnlyList<int> Order(Func<RasterType, double> size) =>
            live.OrderByDescending(t => t.Priority).ThenByDescending(size).ThenBy(t => t.Index)
                .Select(t => t.Index).ToArray();
    }
}

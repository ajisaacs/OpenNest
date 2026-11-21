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
/// Sheet by sheet (<see cref="SheetByStock"/>), each available stock is packed under several
/// free-space scoring rules and two pick modes (best-fitting box anywhere, or largest type first),
/// and the candidate with the lowest estimated whole-job cost wins. The finished job is reported as
/// its one whole-job plan (<see cref="NestJobStage.PlanImproved"/>) before its plate commits.
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
    /// <summary>Tests turn the large-first runs off to compare against the cost-first run alone.</summary>
    internal bool LargeFirstRuns { get; init; } = true;

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

        var demand = types.Select(t => new SheetDemand(t.Index, t.Id, t.Priority, t.MaterialArea)).ToArray();
        // One run makes one whole-job plan: report it, then its commits, as the other engines do.
        var forward = progress == null ? null : new CandidateProgress(progress);
        var result = SheetByStock.Run(job, demand, remaining.ToArray(), Pack, forward, token, BatchSelected);
        // Sheet by sheet, cost picks the densest sheet, which on a job mixing large plates with
        // small parts packs the small parts onto early sheets of their own and leaves the large
        // plates' sheets nothing to fill their gaps. Large-first runs nest the large parts alone,
        // fill each of their sheets' gaps with the small parts, and only then start new sheets for
        // what is left. The cheapest whole job wins; ties keep the cost-first run.
        var best = Score(result, 0);
        var order = 1;
        foreach (var split in LargeFirstRuns ? LargeSplits(types, remaining) : Enumerable.Empty<double>())
        {
            token.ThrowIfCancellationRequested();
            var scored = Score(LargeFirst(split), order++);
            if (scored.IsBetterThan(best))
                best = scored;
        }
        result = best.Result;
        CandidateProgress.ReportPlan(result, progress);
        CandidateProgress.ReportCommits(result, progress, token);
        return result;

        IEnumerable<SheetLayout> Pack(NestPlateStock stock, IReadOnlyList<int> left)
        {
            // Both private MaxRects modes return only after their normal demand/no-fit search;
            // neither returns partial/budget-limited output. This is not a geometric impossibility proof.
            var plans = PackAll(stock, left, null, null);
            foreach (var plan in plans)
                yield return Layout(plan);
            // A sheet that finishes the job is also tried squeezed into a shorter or narrower bin.
            foreach (var plan in SheetSqueeze.Run(types, left, stock, plans,
                         (width, height) => PackAll(stock, left, width, height), token))
                yield return Layout(plan);
        }

        IReadOnlyList<SheetPlan> PackAll(NestPlateStock stock, IReadOnlyList<int> left, double? width, double? height)
        {
            var plans = new List<SheetPlan>(Modes.Length * Rules.Length);
            foreach (var mode in Modes)
                foreach (var rule in Rules)
                {
                    plans.Add(SheetPacker.Pack(types, left, stock, rule, mode, token, width, height));
                    SheetPacked?.Invoke(mode);
                }
            return plans;
        }

        NestJobResult LargeFirst(double split)
        {
            var large = types.Select(t => t.BoxArea >= split).ToArray();
            var small = remaining.Select((r, i) => large[i] ? 0 : r).ToArray();
            var sheets = SheetByStock.Run(job, demand, remaining.Select((r, i) => large[i] ? r : 0).ToArray(),
                Pack, forward, token);
            var stocks = job.Plates.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var index = types.ToDictionary(t => t.Id, t => t.Index, StringComparer.Ordinal);
            var filled = new List<SheetLayout>(sheets.Plates.Count);
            foreach (var sheet in sheets.Plates)
            {
                token.ThrowIfCancellationRequested();
                var stock = stocks[sheet.StockId];
                var own = new int[types.Count];
                foreach (var p in sheet.Placements)
                    own[index[p.PartId]]++;
                var poses = sheet.Placements.Select(p => (p.PartId, p.X, p.Y, p.Rotation)).ToArray();
                var layout = new SheetLayout(stock, poses, types.Sum(t => own[t.Index] * t.MaterialArea), null);
                if (small.Any(r => r > 0))
                {
                    // Repack the sheet's own large parts with every small part still waiting;
                    // a plan counts only if it keeps all of the sheet's large parts.
                    var wanted = own.Select((n, i) => n + small[i]).ToArray();
                    SheetPlan? fill = null;
                    foreach (var plan in PackAll(stock, wanted, null, null))
                    {
                        var counts = new int[types.Count];
                        foreach (var p in plan.Parts)
                            counts[p.Type.Index]++;
                        if (types.Any(t => large[t.Index] && counts[t.Index] < own[t.Index]))
                            continue;
                        if (fill == null || plan.MaterialArea > fill.MaterialArea)
                            fill = plan;
                    }
                    if (fill != null && fill.MaterialArea > layout.MaterialArea)
                    {
                        layout = Layout(fill);
                        foreach (var p in fill.Parts)
                            if (!large[p.Type.Index])
                                small[p.Type.Index]--;
                    }
                }
                filled.Add(layout);
            }
            return SheetByStock.Run(job, demand, small, Pack, forward, token, prefix: filled);
        }

        // Both runs come from the same validated box packer, so they rank on demand met, cost and
        // offcut without a layout check.
        CandidateScore Score(NestJobResult r, int at)
        {
            var unplaced = System.Math.Max(0, job.Parts.Sum(p => p.Quantity) - r.Plates.Sum(p => p.Placements.Count));
            return new CandidateScore(r, true, unplaced, NestJobCost.Evaluate(job, r),
                NestJobCost.CompactnessOffcut(job, r), at, NestJobCost.UsesExplicitCosts(job));
        }

        static SheetLayout Layout(SheetPlan plan) =>
            new(plan.Stock, plan.Poses().ToArray(), plan.MaterialArea, plan.Envelope);
    }

    /// <summary>Box-area thresholds splitting the job's types into large and small: below each of
    /// the widest drops in box area (to half or less of the next larger type), at most
    /// <see cref="MaxLargeSplits"/>, widest first.</summary>
    private static IEnumerable<double> LargeSplits(IReadOnlyList<BoxType> types, int[] remaining)
    {
        var areas = types.Where(t => remaining[t.Index] > 0).Select(t => t.BoxArea)
            .Distinct().OrderByDescending(a => a).ToArray();
        return Enumerable.Range(1, System.Math.Max(0, areas.Length - 1))
            .Where(i => areas[i] <= areas[i - 1] * 0.5)
            .OrderBy(i => areas[i] / areas[i - 1]).ThenBy(i => i)
            .Take(MaxLargeSplits)
            .Select(i => areas[i - 1]);
    }

    private const int MaxLargeSplits = 3;

    private static bool FitsStock(NestPlateStock stock, BoxOrientation o)
    {
        if (stock.Outline != null)
            return false; // Boxes pack rectangles only.
        var work = stock.WorkArea;
        return o.Width <= work.Right - work.Left + SheetPacker.OverhangAllowance
            && o.Height <= work.Top - work.Bottom + SheetPacker.OverhangAllowance;
    }
}

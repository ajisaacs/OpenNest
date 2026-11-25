#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using OpenNest.CutOffs;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Frontier-advance NFP packer with look-ahead stock selection.
///
/// Per sheet, <see cref="FrontierPacker"/> keeps the exact free region of every
/// (part type, orientation) as inner-fit rectangle minus no-fit polygons, and repeatedly places
/// either the largest part that fills a gap behind the packing front, or the part that advances
/// the front least per unit of area covered. Across sheets, every available stock size is
/// trial-packed and the one with the lowest estimated whole-job cost (its own net cost plus the
/// remaining demand at the best efficiency seen) is committed. A handful of deterministic
/// strategy variants (front direction, area exponent) run whole-job, and the cheapest wins.
///
/// With <see cref="NestJobOptions.SearchUntilCancelled"/>, a cancellable token and a progress
/// listener, the search then keeps decoding the variants with seeded per-type weights on the
/// placement rule, reporting each better plan, until cancelled (not for one-type jobs, where
/// the weights change nothing). Such a solve first reports a quick plan, the first variant
/// without Lattice blocks, metered apart so the search after it is unchanged.
///
/// Fully deterministic: no clocks or randomness influence any decision. The weights come from
/// fixed seeds 1, 2, 3, ..., so an extended search depends only on how far it got.
///
/// This file holds the engine's settings and the order of its passes (<see cref="Search"/>);
/// the Solver beside it packs sheets and builds plans, and its other half improves a plan.
/// </summary>
public sealed partial class IrregularNestingEngine : INestingEngine
{
    /// <summary>Strategy variants, tried in order: (front direction, area exponent beta).
    /// The cutout packer proposal tries the same variants on a cutout.</summary>
    internal static readonly (PackAxis Axis, double Beta)[] Variants =
    {
        (PackAxis.X, 1.0),
        (PackAxis.Y, 1.0),
        (PackAxis.X, 0.5),
        (PackAxis.Y, 0.5),
        (PackAxis.X, 1.5),
        (PackAxis.Y, 1.5),
    };

    /// <summary>
    /// Front directions of the grouped variants, tried after <see cref="Variants"/>: along the
    /// length of the stock, and across it too when some stock is taller than it is long.
    /// </summary>
    internal static IEnumerable<PackAxis> GroupedAxes(NestJob job)
    {
        yield return PackAxis.X;
        if (job.Plates.Any(s => s.WorkArea.Width > s.WorkArea.Length))
            yield return PackAxis.Y;
    }

    /// <summary>Whether the grouped variants run; off only in tests of the ungrouped search.</summary>
    internal bool GroupLikeParts { get; init; } = true;

    /// <summary>Whether the banded variant runs; off only in tests of the search before it.</summary>
    internal bool BandLikeParts { get; init; } = true;

    /// <summary>Work budgets after which the banded variant does not start.</summary>
    internal const int BandedWorkLimit = 3;

    /// <summary>Whether the large-first variants run; off only in tests of the search before them.</summary>
    internal bool LargeFirst { get; init; } = true;

    /// <summary>Most large/small splits the large-first variants try, widest drop in box area first.</summary>
    internal const int MaxLargeSplits = 3;

    /// <summary>
    /// Per-type ranks for each large-first split of the job, widest split first: 0 for the large
    /// types, 1 for the rest. A split falls below each drop in minimum box area (over the type's
    /// orientations) to half or less of the next larger type with demand, as Rectangles splits.
    /// Ranks serve after priority, so a split is skipped when no priority holds both a large and a
    /// small type with demand: its decodes would repeat the strategy variants.
    /// </summary>
    internal static IEnumerable<int[]> LargeFirstRanks(IReadOnlyList<PartType> types, int[] demand)
    {
        var box = types.Select(t => t.Orientations.Count == 0 ? 0
            : t.Orientations.Min(o => (o.MaxX - o.MinX) * (o.MaxY - o.MinY))).ToArray();
        var areas = types.Where(t => demand[t.Index] > 0).Select(t => box[t.Index])
            .Distinct().OrderByDescending(a => a).ToArray();
        return Enumerable.Range(1, System.Math.Max(0, areas.Length - 1))
            .Where(i => areas[i] <= areas[i - 1] * 0.5)
            .OrderBy(i => areas[i] / areas[i - 1]).ThenBy(i => i)
            .Take(MaxLargeSplits)
            .Select(i => types.Select(t => box[t.Index] >= areas[i - 1] ? 0 : 1).ToArray())
            .Where(ranks => types.Where(t => demand[t.Index] > 0).GroupBy(t => t.Part.Priority)
                .Any(level => level.Select(t => ranks[t.Index]).Distinct().Count() > 1));
    }

    /// <summary>Whether the last sheet is packed again as bands; off only in tests of the search before it.</summary>
    internal bool BandLastSheet { get; init; } = true;

    /// <summary>Whether the sheet-reduction pass runs; off only in tests of the search before it.</summary>
    internal bool ReduceSheets { get; init; } = true;

    /// <summary>Most parts a plan may hold for the sheet-reduction pass to try it.</summary>
    internal const int MaxReduceParts = 40;

    /// <summary>Whether the last sheet is squeezed; off only in tests of the search before it.</summary>
    internal bool SqueezeLastSheet { get; init; } = true;

    /// <summary>
    /// Step, in radians (30 degrees), of the extra orientations the squeeze offers a part that
    /// may turn freely, beside the catalog's.
    /// </summary>
    internal const double SqueezeAngleStep = System.Math.PI / 6;

    /// <summary>
    /// Axes the last sheet is squeezed along: the one the job's offcut must come off, else along
    /// the length of the stock, and across it too when the stock is taller than it is long.
    /// </summary>
    internal static IEnumerable<PackAxis> SqueezeAxes(NestJob job, NestPlateStock stock)
    {
        if (job.Options.OffcutCut != CutOffAxis.Horizontal)
            yield return PackAxis.X;
        if (job.Options.OffcutCut == CutOffAxis.Horizontal
            || (job.Options.OffcutCut == null && stock.WorkArea.Width > stock.WorkArea.Length))
            yield return PackAxis.Y;
    }

    /// <summary>
    /// Deterministic work budget, in free-region subtractions, after which no further variant
    /// starts. Keeps big jobs well inside benchmark timeouts without consulting a clock.
    /// </summary>
    internal long WorkBudget { get; init; } = 1_500_000;

    /// <summary>
    /// Whether a tail re-plan's decode skips stock trials that can neither be chosen nor change the
    /// choice; off only in tests that compare against packing every trial.
    /// </summary>
    internal bool PruneStockTrials { get; init; } = true;

    /// <summary>Spread of the seeded type weights: each is e^(±WeightSpread) at most.</summary>
    internal const double WeightSpread = 0.4;

    /// <summary>The placement-rule weights for one seed, one per part type.</summary>
    internal static double[] SeedWeights(int seed, int typeCount)
    {
        var random = new Random(seed);
        var weights = new double[typeCount];
        for (var i = 0; i < weights.Length; i++)
            weights[i] = System.Math.Exp(WeightSpread * (2 * random.NextDouble() - 1));
        return weights;
    }

    /// <summary>
    /// One private drawing per part geometry, shared by the best-fit pair and Fill block
    /// catalogs and by every solve of this instance (a pipeline's cutout trial and its ordinary
    /// solve see the same geometry), so each part's best-fit search runs once. Entries live as
    /// long as the job geometry they describe.
    /// </summary>
    private readonly ConditionalWeakTable<PartGeometrySnapshot, Drawing> drawings = new();

    private Drawing DrawingFor(NestJobPart part) =>
        drawings.GetValue(part.Geometry, _ => DrawingJobMapper.CreateDrawing(part));

    // Instance-owned diagnostics; no process-global counters or mutable solver state.
    internal Action? SheetPacked { get; init; }
    internal Action<RepeatBatch>? BatchSelected { get; init; }
    internal Action<int>? TailReplaced { get; init; }
    /// <summary>A tail re-plan starts, with the work it may still spend.</summary>
    internal Action<long>? TailReplanning { get; init; }

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        var types = PartCatalog.Build(job);
        using var solver = new Solver(this, job, types, progress, token);
        var demand = solver.PlaceableDemand();
        var search = new Search(this, job, types, solver, demand, progress, token);

        // More searching can only end in cancellation, so it needs a listener for its plans, and
        // it needs demand and two or more types, or every weighted decode repeats a variant.
        var untilCancelled = job.Options.SearchUntilCancelled && token.CanBeCanceled && progress != null
            && types.Count > 1 && demand.Any(d => d > 0);

        // A solve that ends only at its limit reports a quick plan first: the first variant without
        // Lattice blocks, whose preparation takes most of the first decode's time on big jobs. It is
        // metered apart, so the search below runs as without it. The search's plans are reported as
        // before: only the deadline wrapper sets SearchUntilCancelled, and it keeps the best reported
        // plan that passes the layout check, so a quick plan that fails the check hides none of them.
        // Without a type of three or more copies no block is ever prepared, and the quick plan would
        // only repeat the first variant.
        if (untilCancelled && demand.Any(d => d > 2))
            search.QuickPlan();

        search.StrategyVariants();

        // Then the large parts first: each sheet packs the large types before any small one, so the
        // small parts fill the large parts' gaps instead of spending the first, densest sheets on
        // their own and leaving the large parts sheets with nothing beside them. Metered apart and
        // preparing no Lattice blocks, so the passes after it run as without it; it only changes
        // them by beating the best plan, which they must then beat.
        if (LargeFirst)
            search.LargeFirstVariants();

        // Like parts grouped, the groups' stragglers filling the gaps after them. These run last, on
        // their own budget, so the search above is the same with or without them; a grouped plan
        // replaces its result only by beating it. Like the variants, none starts once the work
        // budget is spent.
        var groups = GroupLikeParts && GroupedFill.Applies(types, demand);
        if (groups)
            search.GroupedVariants();

        // Then full-height strips: each like-part group as a band of its Lattice pattern, or strip by
        // strip with the largest part first and the rest in its gaps; per sheet the better wins.
        if (BandLikeParts && groups)
            search.BandedVariant();

        // Then try to drop a sheet: split the parts over one sheet fewer and pack each sheet edge
        // first, the key part turned by its contacts. Replaces the result only by beating it.
        if (ReduceSheets)
            search.ReduceSheets();

        // Last, shorten the last sheet: its parts packed again in a given order, largest first,
        // inside ever shorter strips. Replaces the result only by beating it.
        if (SqueezeLastSheet)
            search.SqueezeLastSheet();

        if (untilCancelled)
            search.SeededVariants(groups);
        return BuildResult(job, types, search.Result, progress, token);
    }

    /// <summary>
    /// One solve's passes, in the order <see cref="Solve"/> runs them. Each keeps the best plan so
    /// far and reports what it finds; reporting never changes the search.
    /// </summary>
    private sealed class Search(
        IrregularNestingEngine engine,
        NestJob job,
        IReadOnlyList<PartType> types,
        Solver solver,
        int[] demand,
        IProgress<NestJobProgress>? progress,
        CancellationToken token
    )
    {
        private Plan? best;

        /// <summary>
        /// The best plan reported so far: <see cref="Best"/>, or one of the search's best plans
        /// with its last sheet banded (<see cref="BandLastSheet"/>). The search never reads it, so
        /// it runs as without the banding.
        /// </summary>
        private Plan? shown;

        /// <summary>The best plan so far; the strategy variants find the first.</summary>
        public Plan Best => best ?? throw new InvalidOperationException("The strategy variants have not run.");

        /// <summary>The plan to return, which is the last one reported as the best: the search's
        /// best, or a banded plan it has not beaten.</summary>
        public Plan Result => shown ?? Best;

        private long WorkBudget => engine.WorkBudget;

        /// <summary>Reports the first variant decoded without Lattice blocks; it never becomes the best.</summary>
        public void QuickPlan()
        {
            var quick = solver.QuickPlan(demand);
            if (quick.Sheets.Count > 0)
                Report(quick);
        }

        /// <summary>
        /// Decodes <see cref="Variants"/> until the work budget is spent, keeps the best and re-plans
        /// its tail: the last sheets hold the leftovers, which is where waste concentrates.
        /// </summary>
        public void StrategyVariants()
        {
            Plan? leader = null;
            foreach (var (axis, beta) in Variants)
            {
                token.ThrowIfCancellationRequested();
                if (leader != null && solver.Work.Value >= WorkBudget)
                    break;
                var plan = solver.Plan(demand, new Strategy(axis, beta));
                ReportAlternatives();
                if (leader == null || plan.IsBetterThan(leader))
                {
                    leader = plan;
                    ReportBest(leader);
                }
                else
                    ReportAlternative(plan);
                if (leader.Unplaced == 0 && leader.Sheets.Count == 0)
                    break;
            }

            best = leader!;
            BandLastSheet();
            Replace(solver.ImproveTail(leader!, WorkBudget * 2));
            BandLastSheet();
        }

        /// <summary>Decodes the grouped variants, none starting once the work budget is spent.</summary>
        public void GroupedVariants()
        {
            foreach (var axis in GroupedAxes(job))
            {
                token.ThrowIfCancellationRequested();
                if (solver.Work.Value >= WorkBudget)
                    break;
                Offer(solver.Plan(demand, Strategy.Grouped(axis)));
            }
        }

        /// <summary>
        /// Decodes the banded variant. Strips ignore priority, so only for jobs of one priority
        /// band; it starts while the work spent stays under the ungrouped and grouped budgets
        /// together.
        /// </summary>
        public void BandedVariant()
        {
            if (job.Parts.Select(p => p.Priority).Distinct().Count() != 1
                || solver.Work.Value >= BandedWorkLimit * WorkBudget)
                return;
            token.ThrowIfCancellationRequested();
            Offer(solver.Plan(demand, Strategy.Banded), banded: true);
        }

        /// <summary>
        /// Decodes <see cref="Variants"/> with the large types ranked first, for each large/small
        /// split, on a work meter of its own (<see cref="Solver.Apart"/>). Each split gets one work
        /// budget; its first variant always runs.
        /// </summary>
        public void LargeFirstVariants()
        {
            var splits = LargeFirstRanks(types, demand).ToList();
            if (splits.Count > 0)
                solver.Apart(() => LargeFirstSplits(splits));
        }

        private bool LargeFirstSplits(IReadOnlyList<int[]> splits)
        {
            foreach (var ranks in splits)
            {
                var limit = solver.Work.Value + WorkBudget;
                var first = true;
                foreach (var (axis, beta) in Variants)
                {
                    token.ThrowIfCancellationRequested();
                    if (!first && solver.Work.Value >= limit)
                        break;
                    first = false;
                    Offer(solver.Plan(demand, new Strategy(axis, beta, Ranks: ranks)));
                }
            }
            return true;
        }

        /// <summary>
        /// Packs the best plan's last sheet again as bands, each time a plan becomes the best: the
        /// like parts that end a job, which the frontier packer set down one at a time behind the
        /// others, each as its Lattice pattern. The banded plan is reported when it beats every
        /// plan reported and returned when it beats the search's result; it never becomes
        /// <see cref="Best"/>. Bands ignore priority, so only for jobs of one priority.
        /// </summary>
        private void BandLastSheet()
        {
            if (!engine.BandLastSheet || job.Parts.Select(p => p.Priority).Distinct().Count() != 1)
                return;
            token.ThrowIfCancellationRequested();
            var banded = solver.BandLastSheet(Best);
            if (!ReferenceEquals(banded, Best))
                ReportBest(banded);
        }

        public void ReduceSheets()
        {
            var before = Best;
            Replace(solver.ReduceSheets(before));
            if (!ReferenceEquals(Best, before))
                BandLastSheet();
        }

        public void SqueezeLastSheet() => Replace(solver.Squeeze(Best));

        /// <summary>
        /// Decodes seed after seed, cycling the variants and, when the job groups, the grouped
        /// variants among them, and re-plans the tail of each plan that beats the best so far;
        /// ends only when the token is cancelled.
        /// </summary>
        public void SeededVariants(bool groups)
        {
            var grouped = groups ? GroupedAxes(job).ToArray() : Array.Empty<PackAxis>();
            for (var seed = 1; ; seed = seed == int.MaxValue ? 1 : seed + 1)
            {
                token.ThrowIfCancellationRequested();
                var pick = seed % (Variants.Length + grouped.Length);
                var weights = SeedWeights(seed, types.Count);
                var plan = solver.Plan(demand, pick < Variants.Length
                    ? new Strategy(Variants[pick].Axis, Variants[pick].Beta, weights)
                    : Strategy.Grouped(grouped[pick - Variants.Length], weights));
                if (plan.IsBetterThan(Best))
                    Adopt(plan);
            }
        }

        /// <summary>
        /// Takes a variant's plan: it becomes the best by beating it, and is otherwise reported as
        /// another layout, like the complete plans its decode passed over.
        /// </summary>
        /// <param name="banded">The plan's sheets are banded already.</param>
        private void Offer(Plan plan, bool banded = false)
        {
            ReportAlternatives();
            if (plan.IsBetterThan(Best))
                Adopt(plan, banded);
            else
                ReportAlternative(plan);
        }

        /// <summary>Makes a plan that beat the best the new best and re-plans its tail on a fresh
        /// budget, banding the last sheet before and after: a tail re-plan can take a minute, and
        /// the plan shown meanwhile is the one a person stopping the search gets. A plan whose
        /// sheets are <paramref name="banded"/> already is banded only when its tail changes.</summary>
        private void Adopt(Plan plan, bool banded = false)
        {
            // Report before the tail re-plan, which the limit may cut short.
            ReportBest(plan);
            best = plan;
            if (!banded)
                BandLastSheet();
            Replace(solver.ImproveTail(plan, solver.Work.Value + WorkBudget * 2));
            if (!banded || !ReferenceEquals(best, plan))
                BandLastSheet();
        }

        /// <summary>Takes a pass's result, which is the plan it was given unless it found a better one.</summary>
        private void Replace(Plan result)
        {
            if (ReferenceEquals(result, best))
                return;
            best = result;
            ReportBest(result);
        }

        /// <summary>
        /// Reports a plan the search made its best, or its banded form, unless a plan already
        /// reported is as good: a banded plan can stay ahead of the search's next best.
        /// </summary>
        private void ReportBest(Plan plan)
        {
            if (shown != null && !plan.IsBetterThan(shown))
                return;
            shown = plan;
            Report(plan);
        }

        /// <summary>Reports a whole-job plan as the best so far.</summary>
        private void Report(Plan plan)
        {
            if (progress != null)
                CandidateProgress.ReportPlan(BuildResult(job, types, plan, null, token), progress);
        }

        /// <summary>Reports a whole-job plan that lost to the best so far, as another layout a
        /// front end may offer.</summary>
        private void ReportAlternative(Plan plan)
        {
            if (progress != null && plan.Sheets.Count > 0)
                CandidateProgress.ReportAlternative(BuildResult(job, types, plan, null, token), progress);
        }

        /// <summary>Reports the complete plans the last decode found beside the one it returned.</summary>
        private void ReportAlternatives()
        {
            foreach (var plan in solver.Alternatives)
                ReportAlternative(plan);
        }
    }

    private static NestJobResult BuildResult(NestJob job, IReadOnlyList<PartType> types,
        Plan plan, IProgress<NestJobProgress>? progress, CancellationToken token)
    {
        var builder = new NestJobResultBuilder(job, progress);
        foreach (var sheet in plan.Sheets)
        {
            token.ThrowIfCancellationRequested();
            builder.AddSheet(sheet.Stock, sheet.Parts.Select(p =>
                (types[p.Orientation.TypeIndex].Part.Id, p.X, p.Y, p.Orientation.Rotation)));
        }
        token.ThrowIfCancellationRequested();
        return builder.Build(plan.Reason);
    }

    /// <summary>Exercises the production physical-tail lifecycle from a test-owned incumbent.
    /// This does not alter normal Solve or expose a public packing API.</summary>
    internal NestJobResult ImproveTailForTesting(NestJob job, IReadOnlyList<SheetFill> physicalPlan) =>
        ForTesting(job, (solver, _) =>
        {
            var unplaced = job.Parts.Sum(p => p.Quantity) - physicalPlan.Sum(s => s.Parts.Count);
            var plan = PlanOf(job, physicalPlan, physicalPlan.Sum(s => NetCost(job.Options, s)), unplaced,
                unplaced == 0 ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound);
            return solver.ImproveTail(plan, WorkBudget * 2);
        });

    /// <summary>Runs the sheet-reduction pass on a test-owned complete plan.</summary>
    internal NestJobResult ReduceSheetsForTesting(NestJob job, IReadOnlyList<SheetFill> physicalPlan) =>
        ForTesting(job, (solver, _) => solver.ReduceSheets(PlanOf(job, physicalPlan,
            physicalPlan.Sum(s => NetCost(job.Options, s)), 0, NestJobStopReason.Completed)));

    /// <summary>Decodes one variant with the given type weights, whole demand placeable; for
    /// tests of the weighted placement rule only.</summary>
    internal NestJobResult PlanForTesting(NestJob job, int variant, IReadOnlyList<double>? weights) =>
        ForTesting(job, (solver, demand) =>
            solver.Plan(demand, new Strategy(Variants[variant].Axis, Variants[variant].Beta, weights)));

    /// <summary>The quick plan a time-limited solve reports first, whole demand placeable; for tests.</summary>
    internal NestJobResult QuickPlanForTesting(NestJob job) =>
        ForTesting(job, (solver, demand) => solver.QuickPlan(demand));

    /// <summary>The result of a plan made on a solver of this engine's, given the whole demand.</summary>
    private NestJobResult ForTesting(NestJob job, Func<Solver, int[], Plan> plan)
    {
        var types = PartCatalog.Build(job);
        using var solver = new Solver(this, job, types, null, default);
        return BuildResult(job, types, plan(solver, types.Select(t => t.Part.Quantity).ToArray()), null, default);
    }
}

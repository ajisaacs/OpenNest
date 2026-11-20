#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

public sealed partial class IrregularNestingEngine
{
    /// <summary>
    /// Shared state for one solve: job, catalog, NFP caches, effort meter. This half decodes a
    /// strategy into a plan, sheet by sheet; the other improves a plan it is given.
    /// </summary>
    private sealed partial class Solver(
        IrregularNestingEngine engine,
        NestJob job,
        IReadOnlyList<PartType> types,
        IProgress<NestJobProgress>? progress,
        CancellationToken token
    ) : IDisposable
    {
        /// <summary>Most plans one <see cref="Plan"/> call keeps beside the one it returns.</summary>
        private const int MaxAlternatives = 6;

        private readonly long workBudget = engine.WorkBudget;
        private readonly Dictionary<double, NoFitCache> caches = new();
        private readonly Dictionary<NestPlateStock, ClampObstacles> clamps = new();
        private readonly Dictionary<NestPlateStock, PackBoundary?> boundaries = new();
        private readonly Dictionary<double, IReadOnlyDictionary<int, IReadOnlyList<PairPose>>> pairs = new();
        private readonly Dictionary<double, BlockCatalog> blocks = new();
        private readonly IReadOnlyDictionary<string, NestJobPart> parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);

        /// <summary>Work at which the current grouped plan stops starting new fills.</summary>
        private long groupedBudget;

        /// <summary>Whether the decode under way is <see cref="QuickPlan"/>'s, which offers no blocks.</summary>
        private bool quick;

        public WorkCounter Work { get; private set; } = new();

        /// <summary>
        /// The complete plans the last <see cref="Plan"/> call found beside the one it returned,
        /// best first: the job finished on another stock at some sheet, or the decode's own plan
        /// when one of those beat it.
        /// </summary>
        public IReadOnlyList<Plan> Alternatives { get; private set; } = Array.Empty<Plan>();

        public void Dispose()
        {
            foreach (var catalog in blocks.Values)
                catalog.Dispose();
        }

        /// <summary>
        /// Each type's demand, or zero for a type no stock can hold. Pair-only orientations may fit
        /// stock even when the sampled single poses do not; demand that neither a single nor a pair
        /// can fit is reported unplaced.
        /// </summary>
        public int[] PlaceableDemand()
        {
            var demand = new int[types.Count];
            foreach (var type in types)
            {
                var placeable = job.Plates.Any(stock =>
                    stock.Quantity != 0
                    && type.Orientations.Any(o => stock.Fits(o.Width, o.Height))
                );
                demand[type.Index] = placeable || PairFits(type)
                    || (type.Part.Quantity > 2 && type.Orientations.Count > 0) ? type.Part.Quantity : 0;
            }
            return demand;
        }

        private bool PairFits(PartType type)
        {
            if (type.Part.Quantity < 2 || type.Orientations.Count == 0)
                return false;
            return job.Plates.Any(stock => stock.Quantity != 0
                && PairsFor(stock).TryGetValue(type.Index, out var candidates)
                && candidates.Any(pair => stock.Fits(pair.Width, pair.Height)));
        }

        /// <summary>
        /// The first variant decoded without Lattice blocks, on a work meter of its own, so the
        /// search after it runs as without it: what it adds to the shared caches (no-fit polygons,
        /// pairs, clamps, boundaries) depends only on the job, never on when it was built.
        /// </summary>
        public Plan QuickPlan(int[] demand)
        {
            var meter = Work;
            Work = new WorkCounter();
            quick = true;
            try
            {
                return Plan(demand, new Strategy(Variants[0].Axis, Variants[0].Beta));
            }
            finally
            {
                Work = meter;
                quick = false;
                Alternatives = Array.Empty<Plan>();
            }
        }

        /// <summary>
        /// Decodes the whole demand under <paramref name="strategy"/> and returns the best plan
        /// found, leaving the others in <see cref="Alternatives"/>.
        /// </summary>
        public Plan Plan(int[] demand, Strategy strategy)
        {
            groupedBudget = Work.Value + workBudget;
            var finishers = new List<Run>();
            var run = Decode(demand, strategy, new Dictionary<string, int>(StringComparer.Ordinal), job.Options.MaxPlates, null,
                finishers);
            // The decode picks each sheet's stock on an estimate of what the leftovers will cost,
            // so a stock that finished the job outright can lose the pick to a plan that ends up
            // dearer. Each such finished plan competes with the decode's own; a tie keeps the decode.
            var decoded = ToPlan(run);
            var best = decoded;
            var plans = new List<Plan> { decoded };
            foreach (var finisher in finishers)
            {
                var plan = ToPlan(finisher);
                plans.Add(plan);
                if (plan.IsBetterThan(best))
                    best = plan;
            }
            plans.Remove(best);
            plans.Sort((a, b) => a.IsBetterThan(b) ? -1 : b.IsBetterThan(a) ? 1 : 0);
            Alternatives = plans.Take(MaxAlternatives).ToList();
            return best;
        }

        private Plan ToPlan(Run run)
        {
            var unplaced = types.Sum(t => t.Part.Quantity) - run.Sheets.Sum(s => s.Parts.Count);
            var reason = run.Reason;
            if (unplaced > 0 && reason == NestJobStopReason.Completed)
                reason = NestJobStopReason.NoPlacementFound; // Demand no stock can hold.
            var penalty = NestJobCost.UnplacedPartPenalty(job);
            return PlanOf(job, run.Sheets, NestJobCost.RequireFinite(run.Net + unplaced * penalty),
                unplaced, reason);
        }

        /// <summary>
        /// Greedy sheet-by-sheet decode. <paramref name="usedBefore"/> seeds finite-stock
        /// accounting, <paramref name="sheetCap"/> bounds the sheets this run may add, and
        /// <paramref name="first"/>, when set, forces the stock of the first sheet.
        /// <paramref name="finishers"/>, when set, receives every complete run the decode passed
        /// over: the sheets chosen so far plus a trial sheet that took all the remaining parts
        /// but was not the sheet chosen. Collecting them never changes the decode.
        /// </summary>
        private Run Decode(
            int[] demand,
            Strategy strategy,
            IReadOnlyDictionary<string, int> usedBefore,
            int? sheetCap,
            NestPlateStock? first,
            List<Run>? finishers = null
        )
        {
            var remaining = (int[])demand.Clone();
            var used = job.Plates.ToDictionary(s => s.Id, s => usedBefore.GetValueOrDefault(s.Id), StringComparer.Ordinal);
            var sheets = new List<SheetFill>();
            var net = 0.0;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (remaining.All(r => r == 0))
                    return new Run(sheets, net, NestJobStopReason.Completed);
                if (sheetCap is int cap && sheets.Count >= cap)
                    return new Run(sheets, net, NestJobStopReason.PlateLimitReached);

                var forced = sheets.Count == 0 ? first : null;
                var trials = TrySheets(strategy, remaining, used, sheets.Count, forced, prune: finishers == null);
                if (trials.Count == 0)
                {
                    var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                    return new Run(sheets, net,
                        exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound);
                }

                var chosen = Choose(trials, remaining);
                finishers?.AddRange(PassedOver(trials, chosen, remaining, sheets, net));

                var (fill, fillNet) = trials[chosen];
                var counts = fill.Parts.GroupBy(p => types[p.Orientation.TypeIndex].Part.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                // Count all stock still available, not just the forced-first trial subset.
                var availableEntries = job.Plates.Count(s => s.Quantity == null || used[s.Id] < s.Quantity);
                var copies = Copies(fill, counts, remaining, availableEntries, fill.Stock.Quantity - used[fill.Stock.Id],
                    sheetCap - sheets.Count);
                engine.BatchSelected?.Invoke(new RepeatBatch(fill.Stock.Id, copies, availableEntries, sheets.Count,
                    forced != null));
                for (var copy = 0; copy < copies; copy++)
                {
                    token.ThrowIfCancellationRequested();
                    // Poses are immutable records over the read-only catalog; each physical sheet
                    // owns a separate list so tail replanning can remove/replace individual copies.
                    sheets.Add(fill with { Parts = fill.Parts.ToList() });
                    net = NestJobCost.RequireFinite(net + fillNet);
                }
                used[fill.Stock.Id] = checked(used[fill.Stock.Id] + copies);
                foreach (var type in types)
                    if (counts.TryGetValue(type.Part.Id, out var count))
                        remaining[type.Index] = checked(remaining[type.Index] - checked(count * copies));
            }
        }

        /// <summary>
        /// Packs the remaining demand on one sheet of every stock still on offer (only
        /// <paramref name="only"/> when set) and returns the trials that placed a part, in stock
        /// order.
        /// </summary>
        private List<Trial> TrySheets(Strategy strategy, int[] remaining, Dictionary<string, int> used, int sheetIndex,
            NestPlateStock? only, bool prune)
        {
            var trials = new List<Trial>();
            var areaLeft = AreaOf(remaining);
            foreach (var stock in job.Plates)
            {
                token.ThrowIfCancellationRequested();
                if (only != null && !ReferenceEquals(stock, only))
                    continue;
                if (stock.Quantity is int available && used[stock.Id] >= available)
                    continue;
                // Only once the block budget is spent: before that a skipped trial would leave
                // block attempts and work unspent that a later trial could then use.
                if (engine.PruneStockTrials && prune && Work.Value >= workBudget && CannotWin(stock, trials, areaLeft))
                    continue;
                progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stock.Id, sheetIndex, 0, 0));
                var fill = FillSheet(stock, strategy, remaining);
                engine.SheetPacked?.Invoke();
                if (fill.Parts.Count > 0)
                    trials.Add(new Trial(fill, NetCost(job.Options, fill)));
            }
            return trials;
        }

        /// <summary>One sheet of <paramref name="stock"/> filled as the strategy's rule says.</summary>
        private SheetFill FillSheet(NestPlateStock stock, Strategy strategy, int[] remaining)
        {
            // Grouped fills pack groups from singles and pairs: Fill blocks cost far more per fill
            // there and gave no denser groups. A banded fill's bands are Fill patterns already; its
            // gap fill takes singles and pairs.
            var offersBlocks = strategy.Rule == SheetRule.Frontier && !quick;
            FrontierPacker Packer(IReadOnlyList<int>? ranks, double? reach) => new(types, CacheFor(stock),
                PairsFor(stock), stock, strategy.Axis, strategy.Beta, Work, offersBlocks ? BlocksFor(stock) : null,
                ClampsFor(stock), BoundaryFor(stock), strategy.Weights, ranks, reach);

            return strategy.Rule switch
            {
                SheetRule.Banded => Banded(stock, remaining, strategy.Weights, Packer),
                SheetRule.Grouped when GroupedFill.Applies(types, remaining) =>
                    new GroupedFill(types, Packer, strategy.Axis, stock.Outline == null, CompareSheets, Work, groupedBudget)
                        .Fill(remaining, token),
                _ => Packer(null, null).Fill(remaining, token),
            };
        }

        /// <summary>
        /// One banded sheet: like parts as full-height bands side by side, or strip by strip with
        /// the largest part's band or column and the others in its gaps; the better sheet wins, a
        /// tie keeping the bands. Falls back to the ordinary fill when nothing bands.
        /// </summary>
        private SheetFill Banded(NestPlateStock stock, int[] remaining, IReadOnlyList<double>? weights,
            Func<IReadOnlyList<int>?, double?, FrontierPacker> packer)
        {
            FrontierPacker Ordinary() => packer(null, null);
            FrontierPacker Within(OpenNest.Geometry.Box box) => new(types, CacheFor(stock), PairsFor(stock), stock,
                PackAxis.Y, 1.0, Work, null, ClampsFor(stock), null, weights, null, null, box);
            BandedFill Bands(long budget) => new(types, BlocksFor(stock), stock, ClampsFor(stock), Ordinary,
                CompareSheets, Work, budget);

            // A sheet that cannot take all the remaining parts fills to its end anyway: it keeps
            // whichever places more, the ordinary fill or strips largest part first (one partial
            // sheet carried), which leave the small parts for the gaps of the sheets after it.
            var work = stock.WorkArea;
            if (AreaOf(remaining) > work.Length * work.Width)
            {
                var plain = Ordinary().Fill(remaining, token);
                var full = Bands(Work.Value + workBudget).FillStrips(remaining, Within, token, 1);
                return full != null && CompareSheets(full, plain) < 0 ? full : plain;
            }
            var bands = Bands(groupedBudget).Fill(remaining, token);
            var strips = Bands(Work.Value + workBudget).FillStrips(remaining, Within, token);
            if (bands == null)
                return strips ?? Ordinary().Fill(remaining, token);
            return strips != null && CompareSheets(strips, bands) < 0 ? strips : bands;
        }

        /// <summary>
        /// One sheet against another for the same demand: more part area placed first, then lower net
        /// cost, then the larger compactness offcut; negative when <paramref name="a"/> is better.
        /// </summary>
        private int CompareSheets(SheetFill a, SheetFill b)
        {
            static int Compare(double x, double y) =>
                System.Math.Abs(x - y) <= CostTie * System.Math.Max(1, System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)))
                    ? 0 : x.CompareTo(y);
            var area = Compare(b.PartArea, a.PartArea);
            if (area != 0)
                return area;
            var cost = Compare(NetCost(job.Options, a), NetCost(job.Options, b));
            return cost != 0 ? cost : Compare(Offcut(job.Options, b), Offcut(job.Options, a));
        }

        /// <summary>
        /// Whether a trial of <paramref name="stock"/> could neither be chosen nor change the
        /// choice, so the decode may skip it: its least possible net cost (the whole sheet, less
        /// full salvage credit) already exceeds the best estimate so far, and over the remaining
        /// part area it cannot have a better cost ratio than the best so far. Later trials only
        /// lower the ratio and the estimates, so the decode picks the same sheet without it.
        /// </summary>
        private bool CannotWin(NestPlateStock stock, List<Trial> trials, double areaLeft)
        {
            if (trials.Count == 0 || stock.Cost < 0 || areaLeft <= 0)
                return false;
            var rate = job.Options.SalvageRate != 0 && job.Options.MinimumSalvageDimension > 0 ? job.Options.SalvageRate : 0;
            if (rate is < 0 or > 1)
                return false;
            var least = (stock.Cost ?? stock.Area) * (1 - rate);
            var ratio = BestRatio(trials);
            var estimate = trials.Min(t => Estimate(t, areaLeft, ratio));
            const double Margin = 1 + 1e-9;
            return least > estimate * Margin && least >= ratio * areaLeft * Margin;
        }

        /// <summary>
        /// The trial to commit, by look-ahead: whatever a trial leaves behind is charged at the best
        /// efficiency any trial achieved, so a sheet that finishes the job competes fairly with a
        /// denser partial one. Ties go to more parts, then the larger offcut, then stock order.
        /// </summary>
        private int Choose(List<Trial> trials, int[] remaining)
        {
            var areaLeft = AreaOf(remaining);
            var ratio = BestRatio(trials);
            return trials
                .Select((t, order) => (t.Fill, order, Estimate: NestJobCost.RequireFinite(Estimate(t, areaLeft, ratio))))
                .OrderBy(t => t.Estimate)
                .ThenByDescending(t => t.Fill.Parts.Count)
                .ThenByDescending(t => NestJobCost.TieBreakKey(Offcut(job.Options, t.Fill)))
                .ThenBy(t => t.order)
                .First()
                .order;
        }

        /// <summary>The lowest net cost per unit of part area among the trials.</summary>
        private static double BestRatio(List<Trial> trials) =>
            trials.Min(t => t.Net / System.Math.Max(t.Fill.PartArea, 1e-12));

        /// <summary>A trial's whole-job cost estimate: its own net cost, and the part area it
        /// leaves at <paramref name="ratio"/>.</summary>
        private static double Estimate(Trial trial, double areaLeft, double ratio) =>
            trial.Net + System.Math.Max(0, areaLeft - trial.Fill.PartArea) * ratio;

        /// <summary>
        /// The complete runs a choice passed over: the sheets so far plus each trial, other than
        /// the chosen one, that took all the remaining parts.
        /// </summary>
        private static IEnumerable<Run> PassedOver(List<Trial> trials, int chosen, int[] remaining,
            List<SheetFill> sheets, double net)
        {
            var left = remaining.Sum();
            for (var i = 0; i < trials.Count; i++)
            {
                if (i == chosen || trials[i].Fill.Parts.Count != left)
                    continue;
                // Each run owns its sheets' part lists, as the chosen copies do.
                var finished = sheets.Select(s => s with { Parts = s.Parts.ToList() }).Append(trials[i].Fill).ToList();
                yield return new Run(finished, NestJobCost.RequireFinite(net + trials[i].Net), NestJobStopReason.Completed);
            }
        }

        /// <summary>
        /// How many sheets in a row take the chosen layout: as many as the demand, the stock and
        /// the sheet cap allow, but one unless the runner's candidate validation certifies it.
        /// </summary>
        private int Copies(SheetFill fill, Dictionary<string, int> counts, int[] remaining, int availableEntries,
            int? stockLeft, int? sheetsLeft)
        {
            var outstanding = types.ToDictionary(t => t.Part.Id, t => remaining[t.Index], StringComparer.Ordinal);
            // Fill has no partial/budget exit: it returns after states empty or Choose finds
            // no supported placement. WorkBudget only controls later variants/block preparation.
            var copies = CompletedLayoutRepeat.Limit(outstanding, counts, searchCompleted: true, availableEntries,
                stockLeft, sheetsLeft);
            if (copies <= 1)
                return copies;
            try
            {
                NestJobValidator.ValidateCandidate(new PlateCandidate(fill.Parts.Select(p =>
                    new NestJobPlacement(types[p.Orientation.TypeIndex].Part.Id, 0, p.X, p.Y, p.Orientation.Rotation))),
                    fill.Stock, outstanding, parts);
                return copies;
            }
            catch (InvalidOperationException)
            {
                return 1; // Uncertified candidates retain ordinary allocation/final validation.
            }
        }

        /// <summary>The part area of <paramref name="counts"/> copies of each type.</summary>
        private double AreaOf(int[] counts) => types.Sum(t => counts[t.Index] * t.Area);

        private static double Clearance(NestPlateStock stock) => System.Math.Max(0, stock.PartSpacing);

        private NoFitCache CacheFor(NestPlateStock stock)
        {
            var clearance = Clearance(stock);
            if (!caches.TryGetValue(clearance, out var cache))
                caches[clearance] = cache = new NoFitCache(clearance);
            return cache;
        }

        /// <summary>An outlined sheet's usable material as the packer's boundary, built once per
        /// stock per solve; null for a rectangular sheet, which keeps the work-area path.</summary>
        private PackBoundary? BoundaryFor(NestPlateStock stock)
        {
            if (stock.Outline == null)
                return null;
            if (!boundaries.TryGetValue(stock, out var found))
                boundaries[stock] = found = new PackBoundary(stock.Outline.Material(stock.PlacementOrigin),
                    stock.OutlineInset, stock.WorkArea, CacheFor(stock));
            return found;
        }

        private ClampObstacles? ClampsFor(NestPlateStock stock)
        {
            if (stock.ClampZones.Count == 0)
                return null;
            if (!clamps.TryGetValue(stock, out var found))
                clamps[stock] = found = new ClampObstacles(stock);
            return found;
        }

        /// <summary>Best-fit pairs for this stock's spacing, built once per solve.</summary>
        private IReadOnlyDictionary<int, IReadOnlyList<PairPose>> PairsFor(NestPlateStock stock)
        {
            var clearance = Clearance(stock);
            if (!pairs.TryGetValue(clearance, out var found))
            {
                // The best-fit plate filter only needs the largest sheet at this spacing;
                // each packer still checks the pair against its own work area.
                var sheets = job.Plates.Where(p => Clearance(p) == clearance).ToList();
                pairs[clearance] = found = PairCatalog.Build(types, clearance,
                    sheets.Max(p => p.Size.Length), sheets.Max(p => p.Size.Width), token, engine.DrawingFor);
            }
            return found;
        }

        /// <summary>Lattice blocks for this stock's spacing, built once per solve.</summary>
        private BlockCatalog BlocksFor(NestPlateStock stock)
        {
            var spacing = Clearance(stock);
            if (!blocks.TryGetValue(spacing, out var found))
                blocks[spacing] = found = new BlockCatalog(spacing, types, PairsFor(stock), Work, workBudget, engine.DrawingFor);
            return found;
        }
    }
}

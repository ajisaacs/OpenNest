using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Defines the <see cref="PairFillResult" />
    /// </summary>
    public class PairFillResult
    {
        /// <summary>
        /// Gets or sets the Parts
        /// </summary>
        public List<Part> Parts { get; set; } = new List<Part>();

        /// <summary>
        /// Gets or sets the BestFits
        /// </summary>
        public List<BestFitResult> BestFits { get; set; }
    }

    /// <summary>
    /// Fills a work area using interlocking part pairs from BestFitCache
    /// </summary>
    public class PairFiller
    {
        /// <summary>
        /// Defines the EarlyExitMinTried
        /// </summary>
        private const int EarlyExitMinTried = 10;

        /// <summary>
        /// Defines the EarlyExitStaleLimit
        /// </summary>
        private const int EarlyExitStaleLimit = 10;

        /// <summary>
        /// Candidates evaluated together, against the best count and work area from before the
        /// batch. Fixed rather than scaled to the processor count, because it decides which
        /// candidates are tried and so which layout wins: every machine must nest alike.
        /// </summary>
        private const int CandidateBatchSize = 8;

        /// <summary>
        /// Defines the plate
        /// </summary>
        private readonly Plate plate;

        /// <summary>
        /// Defines the plateSize
        /// </summary>
        private readonly Size plateSize;

        /// <summary>
        /// Defines the partSpacing
        /// </summary>
        private readonly double partSpacing;

        /// <summary>
        /// Defines the comparer
        /// </summary>
        private readonly IFillComparer comparer;

        /// <summary>
        /// Defines the dedup
        /// </summary>
        private readonly GridDedup dedup;

        /// <summary>
        /// Defines the candidateSelector
        /// </summary>
        private readonly PairCandidateSelector candidateSelector;

        /// <summary>
        /// Defines the remnantFiller
        /// </summary>
        private readonly PairRemnantFiller remnantFiller;

        /// <summary>
        /// Initializes a new instance of the <see cref="PairFiller"/> class.
        /// </summary>
        /// <param name="plate">The plate<see cref="Plate"/></param>
        /// <param name="comparer">The comparer<see cref="IFillComparer"/></param>
        /// <param name="dedup">The dedup<see cref="GridDedup"/></param>
        public PairFiller(Plate plate, IFillComparer comparer = null, GridDedup dedup = null)
        {
            this.plate = plate;
            this.plateSize = plate.Size;
            this.partSpacing = plate.PartSpacing;
            this.comparer = comparer ?? new DefaultFillComparer();
            this.dedup = dedup ?? new GridDedup();
            this.candidateSelector = new PairCandidateSelector(plateSize, partSpacing);
            this.remnantFiller = new PairRemnantFiller(plate, this.comparer);
        }

        /// <summary>
        /// The fill pipeline's stripe convergences, for the stripe fills of the remnant strips.
        /// </summary>
        internal StripeConvergenceCache StripeConvergence
        {
            get => remnantFiller.StripeConvergence;
            init => remnantFiller.StripeConvergence = value;
        }

        /// <summary>
        /// When true (the default), each pair grid is also tiled with whole pairs only, after the
        /// others, and the strip it leaves refilled; that layout wins only by placing more parts.
        /// </summary>
        public bool WholePairGrids { get; init; } = true;

        /// <summary>
        /// The Fill
        /// </summary>
        /// <param name="item">The item<see cref="NestItem"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <param name="token">The token<see cref="CancellationToken"/></param>
        /// <param name="reportProgress">The reportProgress<see cref="Action{List{Part}, string}"/></param>
        /// <returns>The <see cref="PairFillResult"/></returns>
        public PairFillResult Fill(
            NestItem item,
            Box workArea,
            CancellationToken token = default,
            Action<List<Part>, string> reportProgress = null
        )
        {
            // The first computation for a drawing can take a minute; say so rather than look stuck.
            if (!BestFitCache.IsCached(item.Drawing, plateSize.Length, plateSize.Width, partSpacing))
                reportProgress?.Invoke(null, "Computing best-fit pairs (may take a minute)...");

            List<BestFitResult> bestFits;
            try
            {
                bestFits = BestFitCache.GetOrCompute(
                    item.Drawing,
                    plateSize.Length,
                    plateSize.Width,
                    partSpacing,
                    token
                );
            }
            catch (OperationCanceledException)
            {
                // The pairs finish computing in the background and are cached for the next fill.
                return new PairFillResult();
            }

            var candidates = candidateSelector.Select(bestFits, workArea);
            Debug.WriteLine(
                $"[PairFiller] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}"
            );
            Debug.WriteLine(
                $"[PairFiller] Plate: {plateSize.Length:F2}x{plateSize.Width:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}"
            );

            var targetCount = item.Quantity > 0 ? item.Quantity : 0;
            var parts = EvaluateCandidates(
                candidates,
                item.Drawing,
                workArea,
                targetCount,
                bestFits,
                token,
                reportProgress
            );

            parts = EvaluateHerringbones(
                item.Drawing,
                workArea,
                targetCount,
                parts,
                candidates,
                bestFits,
                token,
                reportProgress
            );

            return new PairFillResult { Parts = parts, BestFits = bestFits };
        }

        /// <summary>
        /// Tiles the drawing's herringbone pairs (<see cref="HerringbonePatterns"/>), one group
        /// per part rotation, with the same grid and remnant fill as the best-fit pairs, and
        /// returns the better of them and <paramref name="best"/> by the comparer; a tie keeps
        /// <paramref name="best"/>.
        /// </summary>
        private List<Part> EvaluateHerringbones(
            Drawing drawing,
            Box workArea,
            int targetCount,
            List<Part> best,
            List<BestFitResult> candidates,
            List<BestFitResult> bestFits,
            CancellationToken token,
            Action<List<Part>, string> reportProgress
        )
        {
            try
            {
                var groups = HerringbonePatterns.Build(drawing, partSpacing, token);
                if (groups.Count == 0)
                    return best;

                var maxUtilization = candidates.Count > 0 ? candidates.Max(c => c.Utilization) : 1.0;
                var partBox = drawing.Program.BoundingBox();
                var partArea = System.Math.Max(partBox.Width * partBox.Length, 1);
                var minCountToBeat = best?.Count ?? 0;

                var results = new List<Part>[groups.Count];
                Parallel.For(
                    0,
                    groups.Count,
                    new ParallelOptions { CancellationToken = token },
                    i =>
                    {
                        results[i] = EvaluateCandidate(
                            groups[i],
                            drawing,
                            workArea,
                            minCountToBeat,
                            targetCount,
                            maxUtilization,
                            partArea,
                            bestFits,
                            token
                        );
                    }
                );

                // In rotation order, so the same layout wins however the workers finish.
                foreach (var result in results)
                {
                    if (comparer.IsBetter(result, best, workArea))
                    {
                        best = result;
                        reportProgress?.Invoke(best, $"Pairs: herringbone, best = {best.Count} parts");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Canceled during herringbone pairs, using results so far");
            }

            return best ?? new List<Part>();
        }

        /// <summary>
        /// The EvaluateCandidates
        /// </summary>
        /// <param name="candidates">The candidates<see cref="List{BestFitResult}"/></param>
        /// <param name="drawing">The drawing<see cref="Drawing"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <param name="targetCount">The targetCount<see cref="int"/></param>
        /// <param name="token">The token<see cref="CancellationToken"/></param>
        /// <param name="reportProgress">The reportProgress<see cref="Action{List{Part}, string}"/></param>
        /// <returns>The <see cref="List{Part}"/></returns>
        private List<Part> EvaluateCandidates(
            List<BestFitResult> candidates,
            Drawing drawing,
            Box workArea,
            int targetCount,
            List<BestFitResult> bestFits,
            CancellationToken token,
            Action<List<Part>, string> reportProgress
        )
        {
            List<Part> best = null;
            var sinceImproved = 0;
            var effectiveWorkArea = workArea;

            var maxUtilization = candidates.Count > 0 ? candidates.Max(c => c.Utilization) : 1.0;
            // The remnant bound packs parts at the best pair utilization, which is measured against
            // the part's true area: its bounding box would undercount parts that interlock, such
            // as gussets, and prune candidates whose remnant could win.
            var partBox = drawing.Program.BoundingBox();
            var partArea = drawing.Area > 0
                ? drawing.Area
                : System.Math.Max(partBox.Width * partBox.Length, 1);

            // Evaluates the batches of a candidate list; with early exit, stops once that many
            // candidates in a row failed to improve and returns where it stopped. With requireMore,
            // a candidate replaces the best only with more parts, so a tie keeps the layout.
            int RunBatches(List<BestFitResult> list, bool earlyExit, bool requireMore)
            {
                for (var batchStart = 0; batchStart < list.Count; batchStart += CandidateBatchSize)
                {
                    token.ThrowIfCancellationRequested();

                    var batchEnd = System.Math.Min(batchStart + CandidateBatchSize, list.Count);
                    var results = EvaluateBatch(
                        list,
                        drawing,
                        effectiveWorkArea,
                        batchStart,
                        batchEnd,
                        best?.Count ?? 0,
                        targetCount,
                        maxUtilization,
                        partArea,
                        bestFits,
                        token
                    );

                    (best, effectiveWorkArea, sinceImproved) = ProcessBatchResults(
                        results,
                        best,
                        sinceImproved,
                        workArea,
                        effectiveWorkArea,
                        targetCount,
                        list.Count,
                        batchStart,
                        reportProgress,
                        requireMore
                    );

                    if (earlyExit && batchEnd >= EarlyExitMinTried && sinceImproved >= EarlyExitStaleLimit)
                    {
                        Debug.WriteLine(
                            $"[PairFiller] Early exit at {batchEnd}/{list.Count} — no improvement in last {sinceImproved} candidates"
                        );
                        return batchEnd;
                    }
                }

                return list.Count;
            }

            try
            {
                var stoppedAt = RunBatches(candidates, earlyExit: true, requireMore: false);

                // The candidates are ranked by their grids alone, so the early exit can skip a
                // narrower grid whose leftover strip holds more parts than a fuller grid gains:
                // a column of gusset pairs slid to leave a strip for gussets turned 45 degrees.
                // Those whose grid and remnant estimate could still win are tried anyway.
                if (stoppedAt < candidates.Count && best != null
                    && (targetCount <= 0 || best.Count < targetCount))
                {
                    var minWidth = RotationAnalysis.MinimumWidth(drawing);
                    var promising = candidates
                        .Skip(stoppedAt)
                        .Where(c => candidateSelector.EstimateWithRemnant(
                            c, workArea, minWidth, maxUtilization, partArea) > best.Count)
                        .ToList();
                    if (promising.Count > 0)
                    {
                        Debug.WriteLine(
                            $"[PairFiller] Trying {promising.Count} skipped candidates whose remnant could win"
                        );
                        RunBatches(promising, earlyExit: false, requireMore: true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Canceled mid-phase, using results so far");
            }

            Debug.WriteLine($"[PairFiller] Best pair result: {best?.Count ?? 0} parts");
            return best ?? new List<Part>();
        }

        private List<Part>[] EvaluateBatch(
            List<BestFitResult> candidates,
            Drawing drawing,
            Box workArea,
            int batchStart,
            int batchEnd,
            int minCountToBeat,
            int targetCount,
            double maxUtilization,
            double partArea,
            List<BestFitResult> bestFits,
            CancellationToken token
        )
        {
            var batchCount = batchEnd - batchStart;

            // Claimed in candidate order before the parallel loop: when two candidates share a
            // grid, the earlier one must get it however the threads are scheduled.
            var claims = new List<(Pattern Pattern, NestDirection Dir)>[batchCount];
            for (var j = 0; j < batchCount; j++)
            {
                token.ThrowIfCancellationRequested();
                claims[j] = ClaimGrids(candidates[batchStart + j], drawing, workArea);
            }

            var results = new List<Part>[batchCount];
            Parallel.For(
                0,
                batchCount,
                new ParallelOptions { CancellationToken = token },
                j =>
                {
                    results[j] = EvaluateCandidate(
                        claims[j],
                        drawing,
                        workArea,
                        minCountToBeat,
                        targetCount,
                        maxUtilization,
                        partArea,
                        bestFits,
                        token
                    );
                }
            );
            return results;
        }

        private (List<Part> Best, Box EffectiveWorkArea, int SinceImproved) ProcessBatchResults(
            List<Part>[] results,
            List<Part> best,
            int sinceImproved,
            Box workArea,
            Box effectiveWorkArea,
            int targetCount,
            int totalCandidates,
            int batchStart,
            Action<List<Part>, string> reportProgress,
            bool requireMore = false
        )
        {
            for (var j = 0; j < results.Length; j++)
            {
                var placesMore = !requireMore || best == null
                    || (results[j]?.Count ?? 0) > best.Count;
                if (placesMore && comparer.IsBetter(results[j], best, effectiveWorkArea))
                {
                    best = results[j];
                    sinceImproved = 0;
                    effectiveWorkArea = TryReduceWorkArea(best, targetCount, workArea, effectiveWorkArea);
                }
                else
                {
                    sinceImproved++;
                }

                reportProgress?.Invoke(
                    best,
                    $"Pairs: {batchStart + j + 1}/{totalCandidates} candidates, best = {best?.Count ?? 0} parts"
                );
            }

            return (best, effectiveWorkArea, sinceImproved);
        }

        /// <summary>
        /// The TryReduceWorkArea
        /// </summary>
        /// <param name="parts">The parts<see cref="List{Part}"/></param>
        /// <param name="targetCount">The targetCount<see cref="int"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <param name="effectiveWorkArea">The effectiveWorkArea<see cref="Box"/></param>
        /// <returns>The <see cref="Box"/></returns>
        private static Box TryReduceWorkArea(
            List<Part> parts,
            int targetCount,
            Box workArea,
            Box effectiveWorkArea
        )
        {
            if (targetCount <= 0 || parts.Count <= targetCount)
                return effectiveWorkArea;

            var reduced = ReduceWorkArea(parts, targetCount, workArea);
            if (reduced.Area() >= effectiveWorkArea.Area())
                return effectiveWorkArea;

            Debug.WriteLine(
                $"[PairFiller] Reduced work area to {reduced.Width:F2}x{reduced.Length:F2} (trimmed to {targetCount + 1} parts)"
            );
            return reduced;
        }

        /// <summary>
        /// Given parts that exceed targetCount, sorts by BoundingBox.Top descending,
        /// removes parts from the top until exactly targetCount remain, then returns
        /// the Top of the remaining parts as the new work area height to beat
        /// </summary>
        /// <param name="parts">The parts<see cref="List{Part}"/></param>
        /// <param name="targetCount">The targetCount<see cref="int"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <returns>The <see cref="Box"/></returns>
        private static Box ReduceWorkArea(List<Part> parts, int targetCount, Box workArea)
        {
            if (parts.Count <= targetCount)
                return workArea;

            var sorted = parts.OrderByDescending(p => p.BoundingBox.Top).ToList();

            var trimCount = sorted.Count - targetCount;
            var remaining = sorted.Skip(trimCount).ToList();

            var newTop = remaining.Max(p => p.BoundingBox.Top);

            return new Box(
                workArea.X,
                workArea.Y,
                workArea.Length,
                System.Math.Min(newTop - workArea.Y, workArea.Width)
            );
        }

        /// <summary>
        /// Builds the candidate's tiling patterns and claims the grids no earlier candidate took.
        /// </summary>
        /// <param name="candidate">The candidate<see cref="BestFitResult"/></param>
        /// <param name="drawing">The drawing<see cref="Drawing"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <returns>The patterns and directions left for this candidate to fill</returns>
        private List<(Pattern Pattern, NestDirection Dir)> ClaimGrids(
            BestFitResult candidate,
            Drawing drawing,
            Box workArea
        )
        {
            var claimed = new List<(Pattern Pattern, NestDirection Dir)>();
            var pairParts = candidate.BuildParts(drawing);
            foreach (var angle in BuildTilingAngles(candidate))
            {
                var pattern = FillHelpers.BuildRotatedPattern(pairParts, angle);
                if (pattern.Parts.Count == 0)
                    continue;

                foreach (var dir in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                {
                    if (dedup.TryAdd(pattern.BoundingBox, workArea, dir))
                        claimed.Add((pattern, dir));
                }
            }

            return claimed;
        }

        /// <summary>
        /// The EvaluateCandidate
        /// </summary>
        /// <param name="claimed">The grids this candidate claimed</param>
        /// <param name="drawing">The drawing<see cref="Drawing"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <param name="minCountToBeat">The minCountToBeat<see cref="int"/></param>
        /// <param name="targetCount">The quantity the fill needs, or zero for as many as fit</param>
        /// <param name="maxUtilization">The maxUtilization<see cref="double"/></param>
        /// <param name="partArea">The partArea<see cref="double"/></param>
        /// <param name="bestFits">The pairs the remnant strips are also filled with</param>
        /// <param name="token">The token<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="List{Part}"/></returns>
        private List<Part> EvaluateCandidate(
            List<(Pattern Pattern, NestDirection Dir)> claimed,
            Drawing drawing,
            Box workArea,
            int minCountToBeat,
            int targetCount,
            double maxUtilization,
            double partArea,
            List<BestFitResult> bestFits,
            CancellationToken token
        )
        {
            // Phase 1: tile all grids (fast). Each grid's final overlap check, the bulk of the
            // work, waits until the ranking or Phase 2 needs its outcome (see CompareByCount).
            var grids = new List<FillLinear.DeferredGrid>();
            var engine = new FillLinear(workArea, partSpacing) { Label = "Pairs" };

            // A grid tops its last column or row up with the halves of pairs that do not fit
            // whole, which can take a strip that would hold more parts: J-shaped bar pairs end a
            // 48 x 96 sheet with a column of single halves where four more pairs fit. The pair
            // turned 180 degrees would leave that strip, but it has the same bounding box, so the
            // grid dedup never tiles it. The grid of whole pairs is tried too, after the others,
            // leaving the strip to the remnant fill. Only a fill without a quantity tries it: a
            // capped fill (Auto Nest's) ranks its layouts by more than count, and a whole-pair grid
            // that wins there by one part can end its sheet later (N26-2ER).
            var wholeGrids = new List<List<Part>>();
            var wholeEngine = new FillLinear(workArea, partSpacing) { Label = "Pairs", PartialCopies = false };

            foreach (var (pattern, dir) in claimed)
            {
                token.ThrowIfCancellationRequested();

                var grid = engine.FillDeferred(pattern, dir);
                if (grid.IsPending && (grid.Count == 0 || grid.FallbackCount == 0))
                    grid.Resolve();
                if (!grid.IsPending && grid.Count == 0)
                    continue;

                grids.Add(grid);

                if (WholePairGrids && targetCount <= 0 && pattern.Parts.Count > 1)
                {
                    // Against the grid's tiled count: a whole-pair grid as large holds no
                    // partial copies, so it is that grid, already tried.
                    var wholeParts = wholeEngine.Fill(pattern, dir);
                    if (wholeParts != null && wholeParts.Count > 0 && wholeParts.Count < grid.Count)
                        wholeGrids.Add(wholeParts);
                }
            }

            if (grids.Count == 0)
                return null;

            // Sort by count descending so we try the best grids first
            SortByCount(grids);

            int Bound(List<Part> gridParts) =>
                gridParts.Count + remnantFiller.EstimateUpperBound(gridParts, workArea, maxUtilization, partArea);

            List<Part> WithRemnant(List<Part> gridParts)
            {
                var remnantParts = remnantFiller.Fill(gridParts, drawing, workArea, token, bestFits);
                if (remnantParts == null || remnantParts.Count == 0)
                    return gridParts;

                var total = new List<Part>(gridParts.Count + remnantParts.Count);
                total.AddRange(gridParts);
                total.AddRange(remnantParts);
                return total;
            }

            List<Part> best = null;

            // Early abort: if the best grid + optimistic remnant can't beat the global best, skip Phase 2
            var topBound = minCountToBeat > 0 ? Bound(grids[0].Resolve()) : int.MaxValue;
            if (topBound <= minCountToBeat)
            {
                Debug.WriteLine(
                    $"[PairFiller] Skipping candidate grids: best grid + estimate {topBound} <= best {minCountToBeat}"
                );
            }
            else
            {
                // Phase 2: try remnant for each grid, skip if grid is too far behind
                foreach (var grid in grids)
                {
                    token.ThrowIfCancellationRequested();

                    var gridParts = grid.Resolve();

                    // If this grid + max possible remnant can't beat current best, skip
                    if (best != null && Bound(gridParts) <= best.Count)
                        break; // sorted descending, so remaining are even smaller

                    var total = WithRemnant(gridParts);
                    if (comparer.IsBetter(total, best, workArea))
                        best = total;
                }
            }

            // A whole-pair grid replaces the best only by placing more parts than it and the
            // global best: it exists to free a strip, and its bound is not monotone in its size.
            foreach (var wholeParts in wholeGrids)
            {
                token.ThrowIfCancellationRequested();

                var toBeat = System.Math.Max(best?.Count ?? 0, minCountToBeat);
                if (Bound(wholeParts) <= toBeat)
                    continue;

                var total = WithRemnant(wholeParts);
                if (total.Count > toBeat && comparer.IsBetter(total, best, workArea))
                    best = total;
            }

            return best;
        }

        /// <summary>
        /// Sorts grids with <see cref="CompareByCount"/>. A check that throws during the sort
        /// surfaces as itself, as it did when every grid was checked before sorting, rather than
        /// wrapped by <see cref="List{T}.Sort(Comparison{T})"/>.
        /// </summary>
        internal static void SortByCount(List<FillLinear.DeferredGrid> grids)
        {
            ExceptionDispatchInfo failure = null;
            grids.Sort((a, b) =>
            {
                if (failure != null)
                    return 0;

                try
                {
                    return CompareByCount(a, b);
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                    return 0;
                }
            });
            failure?.Throw();
        }

        /// <summary>
        /// Orders grids by part count, descending, as <c>b.Count.CompareTo(a.Count)</c> on their
        /// checked parts. A grid whose check is pending may keep its tiling or fall back to its
        /// bounding-box tiling; when those counts could order the pair differently, the check
        /// runs. Every answer is the one the checked counts give, so the sort makes the same
        /// moves, ties included, as on grids checked up front, and the checks it skips are of
        /// grids it never needed to tell apart.
        /// </summary>
        internal static int CompareByCount(FillLinear.DeferredGrid a, FillLinear.DeferredGrid b)
        {
            if (ReferenceEquals(a, b))
                return 0;

            while (true)
            {
                if (TryCompareByCount(a, b, out var order))
                    return order;

                if (a.IsPending)
                    a.Resolve();
                else
                    b.Resolve();
            }
        }

        /// <summary>
        /// The order of <paramref name="a"/> and <paramref name="b"/> when every count either
        /// grid can still have gives the same one.
        /// </summary>
        private static bool TryCompareByCount(
            FillLinear.DeferredGrid a,
            FillLinear.DeferredGrid b,
            out int order
        )
        {
            order = b.Count.CompareTo(a.Count);
            return (!a.IsPending || b.Count.CompareTo(a.FallbackCount) == order)
                && (!b.IsPending || b.FallbackCount.CompareTo(a.Count) == order)
                && (!a.IsPending || !b.IsPending || b.FallbackCount.CompareTo(a.FallbackCount) == order);
        }

        /// <summary>
        /// The BuildTilingAngles
        /// </summary>
        /// <param name="candidate">The candidate<see cref="BestFitResult"/></param>
        /// <returns>The <see cref="List{double}"/></returns>
        internal static List<double> BuildTilingAngles(BestFitResult candidate)
        {
            var angles = new List<double>(candidate.HullAngles);
            var optAngle = -candidate.OptimalRotation;

            if (!angles.Any(a => a.IsEqualTo(optAngle)))
                angles.Add(optAngle);

            var optAngle90 = Angle.NormalizeRad(optAngle + Angle.HalfPI);
            if (!angles.Any(a => a.IsEqualTo(optAngle90)))
                angles.Add(optAngle90);

            return angles;
        }
    }
}

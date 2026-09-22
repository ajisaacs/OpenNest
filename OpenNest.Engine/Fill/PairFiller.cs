using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            this.remnantFiller = new PairRemnantFiller(partSpacing, this.comparer);
        }

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
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing,
                plateSize.Length,
                plateSize.Width,
                partSpacing
            );

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
                token,
                reportProgress
            );

            return new PairFillResult { Parts = parts, BestFits = bestFits };
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
            CancellationToken token,
            Action<List<Part>, string> reportProgress
        )
        {
            List<Part> best = null;
            var sinceImproved = 0;
            var effectiveWorkArea = workArea;
            var batchSize = System.Math.Max(2, Environment.ProcessorCount);

            var maxUtilization = candidates.Count > 0 ? candidates.Max(c => c.Utilization) : 1.0;
            var partBox = drawing.Program.BoundingBox();
            var partArea = System.Math.Max(partBox.Width * partBox.Length, 1);

            try
            {
                for (var batchStart = 0; batchStart < candidates.Count; batchStart += batchSize)
                {
                    token.ThrowIfCancellationRequested();

                    var batchEnd = System.Math.Min(batchStart + batchSize, candidates.Count);
                    var results = EvaluateBatch(
                        candidates,
                        drawing,
                        effectiveWorkArea,
                        batchStart,
                        batchEnd,
                        best?.Count ?? 0,
                        maxUtilization,
                        partArea,
                        token
                    );

                    (best, effectiveWorkArea, sinceImproved) = ProcessBatchResults(
                        results,
                        best,
                        sinceImproved,
                        workArea,
                        effectiveWorkArea,
                        targetCount,
                        candidates.Count,
                        batchStart,
                        reportProgress
                    );

                    if (batchEnd >= EarlyExitMinTried && sinceImproved >= EarlyExitStaleLimit)
                    {
                        Debug.WriteLine(
                            $"[PairFiller] Early exit at {batchEnd}/{candidates.Count} — no improvement in last {sinceImproved} candidates"
                        );
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Cancelled mid-phase, using results so far");
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
            double maxUtilization,
            double partArea,
            CancellationToken token
        )
        {
            var batchCount = batchEnd - batchStart;
            var results = new List<Part>[batchCount];
            Parallel.For(
                0,
                batchCount,
                new ParallelOptions { CancellationToken = token },
                j =>
                {
                    results[j] = EvaluateCandidate(
                        candidates[batchStart + j],
                        drawing,
                        workArea,
                        minCountToBeat,
                        maxUtilization,
                        partArea,
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
            Action<List<Part>, string> reportProgress
        )
        {
            for (var j = 0; j < results.Length; j++)
            {
                if (comparer.IsBetter(results[j], best, effectiveWorkArea))
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
        /// The EvaluateCandidate
        /// </summary>
        /// <param name="candidate">The candidate<see cref="BestFitResult"/></param>
        /// <param name="drawing">The drawing<see cref="Drawing"/></param>
        /// <param name="workArea">The workArea<see cref="Box"/></param>
        /// <param name="minCountToBeat">The minCountToBeat<see cref="int"/></param>
        /// <param name="maxUtilization">The maxUtilization<see cref="double"/></param>
        /// <param name="partArea">The partArea<see cref="double"/></param>
        /// <param name="token">The token<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="List{Part}"/></returns>
        private List<Part> EvaluateCandidate(
            BestFitResult candidate,
            Drawing drawing,
            Box workArea,
            int minCountToBeat,
            double maxUtilization,
            double partArea,
            CancellationToken token
        )
        {
            var pairParts = candidate.BuildParts(drawing);
            var angles = BuildTilingAngles(candidate);

            // Phase 1: evaluate all grids (fast)
            var grids = new List<(List<Part> Parts, NestDirection Dir)>();
            foreach (var angle in angles)
            {
                token.ThrowIfCancellationRequested();
                var pattern = FillHelpers.BuildRotatedPattern(pairParts, angle);
                if (pattern.Parts.Count == 0)
                    continue;

                var engine = new FillLinear(workArea, partSpacing) { Label = "Pairs" };
                foreach (var dir in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                {
                    if (!dedup.TryAdd(pattern.BoundingBox, workArea, dir))
                        continue;

                    var gridParts = engine.Fill(pattern, dir);
                    if (gridParts != null && gridParts.Count > 0)
                        grids.Add((gridParts, dir));
                }
            }

            if (grids.Count == 0)
                return null;

            // Sort by count descending so we try the best grids first
            grids.Sort((a, b) => b.Parts.Count.CompareTo(a.Parts.Count));

            // Early abort: if the best grid + optimistic remnant can't beat the global best, skip Phase 2
            if (minCountToBeat > 0)
            {
                var topCount = grids[0].Parts.Count;
                var optimisticRemnant = remnantFiller.EstimateUpperBound(
                    grids[0].Parts,
                    workArea,
                    maxUtilization,
                    partArea
                );
                if (topCount + optimisticRemnant <= minCountToBeat)
                {
                    Debug.WriteLine(
                        $"[PairFiller] Skipping candidate: grid {topCount} + estimate {optimisticRemnant} <= best {minCountToBeat}"
                    );
                    return null;
                }
            }

            // Phase 2: try remnant for each grid, skip if grid is too far behind
            List<Part> best = null;

            foreach (var (gridParts, dir) in grids)
            {
                token.ThrowIfCancellationRequested();

                // If this grid + max possible remnant can't beat current best, skip
                if (best != null)
                {
                    var remnantBound = remnantFiller.EstimateUpperBound(
                        gridParts,
                        workArea,
                        maxUtilization,
                        partArea
                    );
                    if (gridParts.Count + remnantBound <= best.Count)
                        break; // sorted descending, so remaining are even smaller
                }

                var remnantParts = remnantFiller.Fill(gridParts, drawing, workArea, token);
                List<Part> total;
                if (remnantParts != null && remnantParts.Count > 0)
                {
                    total = new List<Part>(gridParts.Count + remnantParts.Count);
                    total.AddRange(gridParts);
                    total.AddRange(remnantParts);
                }
                else
                {
                    total = gridParts;
                }

                if (comparer.IsBetter(total, best, workArea))
                    best = total;
            }

            return best;
        }

        /// <summary>
        /// The BuildTilingAngles
        /// </summary>
        /// <param name="candidate">The candidate<see cref="BestFitResult"/></param>
        /// <returns>The <see cref="List{double}"/></returns>
        private static List<double> BuildTilingAngles(BestFitResult candidate)
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

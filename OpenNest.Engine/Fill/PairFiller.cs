using OpenNest.Engine.BestFit;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine;

namespace OpenNest.Engine.Fill
{
    public class PairFillResult
    {
        public List<Part> Parts { get; set; } = new List<Part>();
        public List<BestFitResult> BestFits { get; set; }
    }

    /// <summary>
    /// Fills a work area using interlocking part pairs from BestFitCache.
    /// </summary>
    public class PairFiller
    {
        private const int MaxTopCandidates = 50;
        private const int MaxStripCandidates = 100;
        private const double MinStripUtilization = 0.3;
        private const int EarlyExitMinTried = 10;
        private const int EarlyExitStaleLimit = 10;

        private readonly Plate plate;
        private readonly Size plateSize;
        private readonly double partSpacing;
        private readonly IFillComparer comparer;
        private readonly GridDedup dedup;

        public PairFiller(Plate plate, IFillComparer comparer = null, GridDedup dedup = null)
        {
            this.plate = plate;
            this.plateSize = plate.Size;
            this.partSpacing = plate.PartSpacing;
            this.comparer = comparer ?? new DefaultFillComparer();
            this.dedup = dedup ?? new GridDedup();
        }

        public PairFillResult Fill(NestItem item, Box workArea,
            int plateNumber = 0,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, plateSize.Length, plateSize.Width, partSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            Debug.WriteLine($"[PairFiller] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");
            Debug.WriteLine($"[PairFiller] Plate: {plateSize.Length:F2}x{plateSize.Width:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}");

            var targetCount = item.Quantity > 0 ? item.Quantity : 0;
            var parts = EvaluateCandidates(candidates, item.Drawing, workArea, targetCount,
                plateNumber, token, progress);

            return new PairFillResult { Parts = parts, BestFits = bestFits };
        }

        private List<Part> EvaluateCandidates(
            List<BestFitResult> candidates, Drawing drawing,
            Box workArea, int targetCount,
            int plateNumber, CancellationToken token, IProgress<NestProgress> progress)
        {
            List<Part> best = null;
            var sinceImproved = 0;
            var effectiveWorkArea = workArea;
            var batchSize = System.Math.Max(2, Environment.ProcessorCount);

            var maxUtilization = candidates.Count > 0 ? candidates.Max(c => c.Utilization) : 1.0;
            var partBox = drawing.Program.BoundingBox();
            var partArea = System.Math.Max(partBox.Width * partBox.Length, 1);

            FillStrategyRegistry.SetEnabled("Pairs", "RectBestFit", "Extents", "Linear");
            try
            {
                for (var batchStart = 0; batchStart < candidates.Count; batchStart += batchSize)
                {
                    token.ThrowIfCancellationRequested();

                    var batchEnd = System.Math.Min(batchStart + batchSize, candidates.Count);
                    var batchCount = batchEnd - batchStart;
                    var batchWorkArea = effectiveWorkArea;
                    var minCountToBeat = best?.Count ?? 0;

                    var results = new List<Part>[batchCount];
                    Parallel.For(0, batchCount,
                        new ParallelOptions { CancellationToken = token },
                        j =>
                        {
                            results[j] = EvaluateCandidate(
                                candidates[batchStart + j], drawing, batchWorkArea,
                                minCountToBeat, maxUtilization, partArea, token);
                        });

                    for (var j = 0; j < batchCount; j++)
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

                        NestEngineBase.ReportProgress(progress, new ProgressReport
                        {
                            Phase = NestPhase.Pairs,
                            PlateNumber = plateNumber,
                            Parts = best,
                            WorkArea = workArea,
                            Description = $"Pairs: {batchStart + j + 1}/{candidates.Count} candidates, best = {best?.Count ?? 0} parts",
                        });
                    }

                    if (batchEnd >= EarlyExitMinTried && sinceImproved >= EarlyExitStaleLimit)
                    {
                        Debug.WriteLine($"[PairFiller] Early exit at {batchEnd}/{candidates.Count} — no improvement in last {sinceImproved} candidates");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Cancelled mid-phase, using results so far");
            }
            finally
            {
                FillStrategyRegistry.SetEnabled(null);
            }

            Debug.WriteLine($"[PairFiller] Best pair result: {best?.Count ?? 0} parts");
            return best ?? new List<Part>();
        }

        private static Box TryReduceWorkArea(List<Part> parts, int targetCount, Box workArea, Box effectiveWorkArea)
        {
            if (targetCount <= 0 || parts.Count <= targetCount)
                return effectiveWorkArea;

            var reduced = ReduceWorkArea(parts, targetCount, workArea);
            if (reduced.Area() >= effectiveWorkArea.Area())
                return effectiveWorkArea;

            Debug.WriteLine($"[PairFiller] Reduced work area to {reduced.Width:F2}x{reduced.Length:F2} (trimmed to {targetCount + 1} parts)");
            return reduced;
        }

        /// <summary>
        /// Given parts that exceed targetCount, sorts by BoundingBox.Top descending,
        /// removes parts from the top until exactly targetCount remain, then returns
        /// the Top of the remaining parts as the new work area height to beat.
        /// </summary>
        private static Box ReduceWorkArea(List<Part> parts, int targetCount, Box workArea)
        {
            if (parts.Count <= targetCount)
                return workArea;

            var sorted = parts
                .OrderByDescending(p => p.BoundingBox.Top)
                .ToList();

            var trimCount = sorted.Count - targetCount;
            var remaining = sorted.Skip(trimCount).ToList();

            var newTop = remaining.Max(p => p.BoundingBox.Top);

            return new Box(workArea.X, workArea.Y,
                workArea.Width,
                System.Math.Min(newTop - workArea.Y, workArea.Length));
        }

        private List<Part> EvaluateCandidate(BestFitResult candidate, Drawing drawing,
            Box workArea, int minCountToBeat, double maxUtilization, double partArea,
            CancellationToken token)
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

                var engine = new FillLinear(workArea, partSpacing);
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
                var optimisticRemnant = EstimateRemnantUpperBound(
                    grids[0].Parts, workArea, maxUtilization, partArea);
                if (topCount + optimisticRemnant <= minCountToBeat)
                {
                    Debug.WriteLine($"[PairFiller] Skipping candidate: grid {topCount} + estimate {optimisticRemnant} <= best {minCountToBeat}");
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
                    var remnantBound = EstimateRemnantUpperBound(
                        gridParts, workArea, maxUtilization, partArea);
                    if (gridParts.Count + remnantBound <= best.Count)
                        break; // sorted descending, so remaining are even smaller
                }

                var remnantParts = FillRemnant(gridParts, drawing, workArea, token);
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

        private int EstimateRemnantUpperBound(List<Part> gridParts, Box workArea,
            double maxUtilization, double partArea)
        {
            var gridBox = ((IEnumerable<IBoundable>)gridParts).GetBoundingBox();

            // L-shaped remnant: top strip (full width) + right strip (grid height only)
            var topHeight = System.Math.Max(0, workArea.Top - gridBox.Top);
            var rightWidth = System.Math.Max(0, workArea.Right - gridBox.Right);

            var topArea = workArea.Width * topHeight;
            var rightArea = rightWidth * System.Math.Min(gridBox.Top - workArea.Y, workArea.Length);
            var remnantArea = topArea + rightArea;

            return (int)(remnantArea * maxUtilization / partArea) + 1;
        }

        private List<Part> FillRemnant(List<Part> gridParts, Drawing drawing,
            Box workArea, CancellationToken token)
        {
            var gridBox = ((IEnumerable<IBoundable>)gridParts).GetBoundingBox();
            var partBox = drawing.Program.BoundingBox();
            var minDim = System.Math.Min(partBox.Width, partBox.Length) + 2 * partSpacing;

            List<Part> bestRemnant = null;

            // Try top remnant (full width, above grid)
            var topY = gridBox.Top + partSpacing;
            var topLength = workArea.Top - topY;
            if (topLength >= minDim)
            {
                var topBox = new Box(workArea.X, topY, workArea.Width, topLength);
                var parts = FillRemnantBox(drawing, topBox, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            // Try right remnant (full height, right of grid)
            var rightX = gridBox.Right + partSpacing;
            var rightWidth = workArea.Right - rightX;
            if (rightWidth >= minDim)
            {
                var rightBox = new Box(rightX, workArea.Y, rightWidth, workArea.Length);
                var parts = FillRemnantBox(drawing, rightBox, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            return bestRemnant;
        }

        private List<Part> FillRemnantBox(Drawing drawing, Box remnantBox, CancellationToken token)
        {
            var cachedResult = FillResultCache.Get(drawing, remnantBox, partSpacing);
            if (cachedResult != null)
            {
                Debug.WriteLine($"[PairFiller] Remnant CACHE HIT: {cachedResult.Count} parts");
                return cachedResult;
            }

            var remnantEngine = NestEngineRegistry.Create(plate);
            var item = new NestItem { Drawing = drawing };
            var parts = remnantEngine.Fill(item, remnantBox, null, token);

            Debug.WriteLine($"[PairFiller] Remnant: {parts?.Count ?? 0} parts in " +
                $"{remnantBox.Width:F2}x{remnantBox.Length:F2}");

            if (parts != null && parts.Count > 0)
            {
                FillResultCache.Store(drawing, remnantBox, partSpacing, parts);
                return parts;
            }

            return null;
        }

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

        private List<BestFitResult> SelectPairCandidates(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(plateSize.Width, plateSize.Length);

            if (workShortSide < plateShortSide * 0.5)
            {
                // Strip mode: prioritize candidates that fit the narrow dimension.
                var stripCandidates = kept
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon
                             && r.Utilization >= MinStripUtilization)
                    .ToList();

                SortByEstimatedCount(stripCandidates, workArea);

                var top = stripCandidates.Take(MaxStripCandidates).ToList();

                Debug.WriteLine($"[PairFiller] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})");
                return top;
            }

            var result = kept.Take(MaxTopCandidates).ToList();
            SortByEstimatedCount(result, workArea);

            return result;
        }

        private void SortByEstimatedCount(List<BestFitResult> candidates, Box workArea)
        {
            var w = workArea.Width;
            var l = workArea.Length;

            candidates.Sort((a, b) =>
            {
                var aCount = EstimateTileCount(a, w, l);
                var bCount = EstimateTileCount(b, w, l);

                if (aCount != bCount)
                    return bCount.CompareTo(aCount);

                return b.Utilization.CompareTo(a.Utilization);
            });
        }

        private int EstimateTileCount(BestFitResult r, double areaW, double areaL)
        {
            var h = EstimateCount(r.BoundingWidth, r.BoundingHeight, areaW, areaL);
            var v = EstimateCount(r.BoundingHeight, r.BoundingWidth, areaW, areaL);
            return System.Math.Max(h, v);
        }

        private int EstimateCount(double pairW, double pairH, double areaW, double areaL)
        {
            if (pairW <= 0 || pairH <= 0) return 0;
            var cols = (int)((areaW + partSpacing) / (pairW + partSpacing));
            var rows = (int)((areaL + partSpacing) / (pairH + partSpacing));
            return cols * rows * 2;
        }
    }
}

using OpenNest.Engine.BestFit;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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

        private readonly Size plateSize;
        private readonly double partSpacing;

        public PairFiller(Size plateSize, double partSpacing)
        {
            this.plateSize = plateSize;
            this.partSpacing = partSpacing;
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
            var bestScore = default(FillScore);
            var sinceImproved = 0;
            var effectiveWorkArea = workArea;

            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var filled = EvaluateCandidate(candidates[i], drawing, effectiveWorkArea);
                    var score = FillScore.Compute(filled, effectiveWorkArea);

                    if (score > bestScore)
                    {
                        best = filled;
                        bestScore = score;
                        sinceImproved = 0;
                        effectiveWorkArea = TryReduceWorkArea(filled, targetCount, workArea, effectiveWorkArea);
                    }
                    else
                    {
                        sinceImproved++;
                    }

                    NestEngineBase.ReportProgress(progress, NestPhase.Pairs, plateNumber, best, workArea,
                        $"Pairs: {i + 1}/{candidates.Count} candidates, best = {bestScore.Count} parts");

                    if (i + 1 >= EarlyExitMinTried && sinceImproved >= EarlyExitStaleLimit)
                    {
                        Debug.WriteLine($"[PairFiller] Early exit at {i + 1}/{candidates.Count} — no improvement in last {sinceImproved} candidates");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Cancelled mid-phase, using results so far");
            }

            Debug.WriteLine($"[PairFiller] Best pair result: {bestScore.Count} parts, density={bestScore.Density:P1}");
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

        private List<Part> EvaluateCandidate(BestFitResult candidate, Drawing drawing, Box workArea)
        {
            var pairParts = candidate.BuildParts(drawing);
            var engine = new FillLinear(workArea, partSpacing);
            var angles = BuildTilingAngles(candidate);
            return FillHelpers.FillPattern(engine, pairParts, angles, workArea);
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
            var top = kept.Take(MaxTopCandidates).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(plateSize.Width, plateSize.Length);

            if (workShortSide < plateShortSide * 0.5)
            {
                var stripCandidates = bestFits
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon
                             && r.Utilization >= MinStripUtilization)
                    .OrderByDescending(r => r.Utilization);

                var existing = new HashSet<BestFitResult>(top);

                foreach (var r in stripCandidates)
                {
                    if (top.Count >= MaxStripCandidates)
                        break;

                    if (existing.Add(r))
                        top.Add(r);
                }

                Debug.WriteLine($"[PairFiller] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})");
            }

            SortByEstimatedCount(top, workArea);

            return top;
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

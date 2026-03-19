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

        /// <summary>
        /// The best-fit results computed during the last Fill call.
        /// Available after Fill returns so callers can reuse without recomputing.
        /// </summary>
        public List<BestFitResult> BestFits { get; private set; }

        public PairFiller(Size plateSize, double partSpacing)
        {
            this.plateSize = plateSize;
            this.partSpacing = partSpacing;
        }

        public List<Part> Fill(NestItem item, Box workArea,
            int plateNumber = 0,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            BestFits = BestFitCache.GetOrCompute(
                item.Drawing, plateSize.Length, plateSize.Width, partSpacing);

            var candidates = SelectPairCandidates(BestFits, workArea);
            Debug.WriteLine($"[PairFiller] Total: {BestFits.Count}, Kept: {BestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");
            Debug.WriteLine($"[PairFiller] Plate: {plateSize.Length:F2}x{plateSize.Width:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}");

            var targetCount = item.Quantity > 0 ? item.Quantity : 0;
            var effectiveWorkArea = workArea;

            List<Part> best = null;
            var bestScore = default(FillScore);
            var sinceImproved = 0;

            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var filled = EvaluateCandidate(candidates[i], item.Drawing, effectiveWorkArea);

                    if (filled != null && filled.Count > 0)
                    {
                        var score = FillScore.Compute(filled, effectiveWorkArea);
                        if (best == null || score > bestScore)
                        {
                            best = filled;
                            bestScore = score;
                            sinceImproved = 0;

                            // If we exceeded the target, reduce the work area for
                            // subsequent candidates by trimming excess parts and
                            // measuring the tighter bounding box.
                            if (targetCount > 0 && filled.Count > targetCount)
                            {
                                var reduced = ReduceWorkArea(filled, targetCount, workArea);
                                if (reduced.Area() < effectiveWorkArea.Area())
                                {
                                    effectiveWorkArea = reduced;
                                    Debug.WriteLine($"[PairFiller] Reduced work area to {effectiveWorkArea.Width:F2}x{effectiveWorkArea.Length:F2} (trimmed to {targetCount + 1} parts)");
                                }
                            }
                        }
                        else
                        {
                            sinceImproved++;
                        }
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

        /// <summary>
        /// Given parts that exceed targetCount, sorts by BoundingBox.Top descending,
        /// removes parts from the top until exactly targetCount remain, then returns
        /// the Top of the remaining parts as the new work area height to beat.
        /// </summary>
        private static Box ReduceWorkArea(List<Part> parts, int targetCount, Box workArea)
        {
            if (parts.Count <= targetCount)
                return workArea;

            // Sort by Top descending — highest parts get trimmed first.
            var sorted = parts
                .OrderByDescending(p => p.BoundingBox.Top)
                .ToList();

            // Remove from the top until exactly targetCount remain.
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

            var p0 = FillHelpers.BuildRotatedPattern(pairParts, 0);
            var p90 = FillHelpers.BuildRotatedPattern(pairParts, Angle.HalfPI);
            engine.RemainderPatterns = new List<Pattern> { p0, p90 };

            // Include the pair's rotating calipers optimal rotation angle
            // alongside the hull edge angles for tiling.
            var angles = new List<double>(candidate.HullAngles);
            var optAngle = -candidate.OptimalRotation;
            if (!angles.Any(a => a.IsEqualTo(optAngle)))
                angles.Add(optAngle);
            var optAngle90 = Angle.NormalizeRad(optAngle + Angle.HalfPI);
            if (!angles.Any(a => a.IsEqualTo(optAngle90)))
                angles.Add(optAngle90);

            return FillHelpers.FillPattern(engine, pairParts, angles, workArea);
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

            return top;
        }
    }
}

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
    /// Extracted from DefaultNestEngine.FillWithPairs.
    /// </summary>
    public class PairFiller
    {
        private readonly Size plateSize;
        private readonly double partSpacing;

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
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, plateSize.Length, plateSize.Width, partSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            Debug.WriteLine($"[PairFiller] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");
            Debug.WriteLine($"[PairFiller] Plate: {plateSize.Length:F2}x{plateSize.Width:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}");

            List<Part> best = null;
            var bestScore = default(FillScore);
            var sinceImproved = 0;

            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var result = candidates[i];
                    var pairParts = result.BuildParts(item.Drawing);
                    var angles = result.HullAngles;
                    var engine = new FillLinear(workArea, partSpacing);

                    // Let the remainder strip try pair-based filling too.
                    var p0 = FillHelpers.BuildRotatedPattern(pairParts, 0);
                    var p90 = FillHelpers.BuildRotatedPattern(pairParts, Angle.HalfPI);
                    engine.RemainderPatterns = new List<Pattern> { p0, p90 };

                    var filled = FillHelpers.FillPattern(engine, pairParts, angles, workArea);

                    if (filled != null && filled.Count > 0)
                    {
                        var score = FillScore.Compute(filled, workArea);
                        if (best == null || score > bestScore)
                        {
                            best = filled;
                            bestScore = score;
                            sinceImproved = 0;
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

                    // Early exit: stop if we've tried enough candidates without improvement.
                    if (i >= 9 && sinceImproved >= 10)
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

        private List<BestFitResult> SelectPairCandidates(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();
            var top = kept.Take(50).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(plateSize.Width, plateSize.Length);

            if (workShortSide < plateShortSide * 0.5)
            {
                var stripCandidates = bestFits
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon
                             && r.Utilization >= 0.3)
                    .OrderByDescending(r => r.Utilization);

                var existing = new HashSet<BestFitResult>(top);

                foreach (var r in stripCandidates)
                {
                    if (top.Count >= 100)
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

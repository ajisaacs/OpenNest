using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Selects and ranks BestFitCache pair candidates for a work area, favoring
    /// narrow "strip" candidates when the work area is much smaller than the plate.
    /// </summary>
    public class PairCandidateSelector
    {
        private const int MaxTopCandidates = 50;
        private const int MaxStripCandidates = 100;
        private const double MinStripUtilization = 0.3;

        private readonly Size plateSize;
        private readonly double partSpacing;

        public PairCandidateSelector(Size plateSize, double partSpacing)
        {
            this.plateSize = plateSize;
            this.partSpacing = partSpacing;
        }

        public List<BestFitResult> Select(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(plateSize.Width, plateSize.Length);

            if (workShortSide < plateShortSide * 0.5)
            {
                // Strip mode: prioritize candidates that fit the narrow dimension.
                var stripCandidates = kept.Where(r =>
                        r.ShortestSide <= workShortSide + Tolerance.Epsilon
                        && r.Utilization >= MinStripUtilization
                    )
                    .ToList();

                SortByEstimatedCount(stripCandidates, workArea);

                var top = stripCandidates.Take(MaxStripCandidates).ToList();

                Debug.WriteLine(
                    $"[PairFiller] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})"
                );
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

            candidates.Sort(
                (a, b) =>
                {
                    var aCount = EstimateTileCount(a, w, l);
                    var bCount = EstimateTileCount(b, w, l);

                    if (aCount != bCount)
                        return bCount.CompareTo(aCount);

                    return b.Utilization.CompareTo(a.Utilization);
                }
            );
        }

        private int EstimateTileCount(BestFitResult r, double areaW, double areaL)
        {
            var h = EstimateCount(r.BoundingWidth, r.BoundingHeight, areaW, areaL);
            var v = EstimateCount(r.BoundingHeight, r.BoundingWidth, areaW, areaL);
            return System.Math.Max(h, v);
        }

        private int EstimateCount(double pairW, double pairH, double areaW, double areaL)
        {
            if (pairW <= 0 || pairH <= 0)
                return 0;
            var cols = (int)((areaW + partSpacing) / (pairW + partSpacing));
            var rows = (int)((areaL + partSpacing) / (pairH + partSpacing));
            return cols * rows * 2;
        }
    }
}

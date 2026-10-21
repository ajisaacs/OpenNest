using System.Collections.Generic;
using System.Linq;
using OpenNest.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Saves and restores <see cref="BestFitCache"/> entries for .nest archives. Only kept
    /// results are exported; imported sets are added without replacing computed ones.
    /// Each set records the canonical angle its pairs were computed in, and import drops sets
    /// whose angle no longer matches the drawing's recomputed one.
    /// </summary>
    public sealed class BestFitCacheStore : IBestFitStore
    {
        /// <summary>
        /// Largest difference (radians) at which a saved canonical angle still matches. The
        /// angle round-trips exactly, so this only absorbs floating-point noise in recomputing it.
        /// </summary>
        private const double FrameTolerance = 1e-9;

        /// <summary>The store over the process-wide <see cref="BestFitCache"/>.</summary>
        public static BestFitCacheStore Shared { get; } = new();

        private BestFitCacheStore() { }

        public IReadOnlyList<SavedPairSet> Export(
            Drawing drawing,
            IReadOnlySet<(double Width, double Length, double Spacing)> plateKeys
        )
        {
            var allBestFits = BestFitCache.GetAllForDrawing(drawing);
            var sets = new List<SavedPairSet>();

            foreach (var kvp in allBestFits)
            {
                if (!plateKeys.Contains((kvp.Key.PlateWidth, kvp.Key.PlateHeight, kvp.Key.Spacing)))
                    continue;

                var results = kvp
                    .Value.Where(r => r.Keep)
                    .Select(r => new SavedPairResult(
                        r.Candidate.Part1Rotation,
                        r.Candidate.Part2Rotation,
                        r.Candidate.Part2Offset.X,
                        r.Candidate.Part2Offset.Y,
                        r.Candidate.StrategyIndex,
                        r.Candidate.TestNumber,
                        r.Candidate.Spacing,
                        r.RotatedArea,
                        r.BoundingWidth,
                        r.BoundingHeight,
                        r.OptimalRotation,
                        r.Keep,
                        r.Reason,
                        r.TrueArea,
                        r.HullAngles
                    ))
                    .ToList();

                sets.Add(new SavedPairSet(
                    kvp.Key.PlateWidth, kvp.Key.PlateHeight, kvp.Key.Spacing, results, FrameAngle(drawing)));
            }

            return sets;
        }

        public void Import(Drawing drawing, IReadOnlyList<SavedPairSet> sets)
        {
            // Saved pairs are in the canonical frame, so their candidates hold a canonical copy,
            // as computed ones do; the source drawing would rebuild a different pair.
            Drawing canonical = null;

            foreach (var set in sets)
            {
                if (set.Results.Count == 0 || !MatchesFrame(set, drawing))
                    continue;

                canonical ??= CanonicalFrame.AsCanonicalCopy(drawing);
                var results = set
                    .Results.Select(r => new BestFitResult
                    {
                        Candidate = new PairCandidate
                        {
                            Drawing = canonical,
                            Part1Rotation = r.Part1Rotation,
                            Part2Rotation = r.Part2Rotation,
                            Part2Offset = new Vector(r.Part2OffsetX, r.Part2OffsetY),
                            StrategyIndex = r.StrategyType,
                            TestNumber = r.TestNumber,
                            Spacing = r.CandidateSpacing,
                        },
                        RotatedArea = r.RotatedArea,
                        BoundingWidth = r.BoundingWidth,
                        BoundingHeight = r.BoundingHeight,
                        OptimalRotation = r.OptimalRotation,
                        Keep = r.Keep,
                        Reason = r.Reason,
                        TrueArea = r.TrueArea,
                        HullAngles = r.HullAngles?.ToList(),
                    })
                    .ToList();

                BestFitCache.Populate(drawing, set.PlateWidth, set.PlateHeight, set.Spacing, results);
            }
        }

        private static double FrameAngle(Drawing drawing) => drawing.Source?.Angle ?? 0.0;

        /// <summary>
        /// True when the set was saved in the drawing's current canonical frame. A changed
        /// canonical-angle computation would replay old pairs in a turned frame, where they can
        /// overlap; sets with no saved angle (older files) are treated the same way.
        /// </summary>
        private static bool MatchesFrame(SavedPairSet set, Drawing drawing)
        {
            if (set.CanonicalAngle is not double saved || !double.IsFinite(saved))
                return false;

            var difference = System.Math.IEEERemainder(saved - FrameAngle(drawing), 2.0 * System.Math.PI);
            return System.Math.Abs(difference) <= FrameTolerance;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using OpenNest.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Saves and restores <see cref="BestFitCache"/> entries for .nest archives. Only kept
    /// results are exported; imported sets are added without replacing computed ones.
    /// </summary>
    public sealed class BestFitCacheStore : IBestFitStore
    {
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

                sets.Add(new SavedPairSet(kvp.Key.PlateWidth, kvp.Key.PlateHeight, kvp.Key.Spacing, results));
            }

            return sets;
        }

        public void Import(Drawing drawing, IReadOnlyList<SavedPairSet> sets)
        {
            foreach (var set in sets)
            {
                var results = set
                    .Results.Select(r => new BestFitResult
                    {
                        Candidate = new PairCandidate
                        {
                            Drawing = drawing,
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
    }
}

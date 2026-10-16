using System.Collections.Generic;

namespace OpenNest.BestFit
{
    /// <summary>One saved best-fit pair result, as persisted in a .nest archive.</summary>
    public sealed record SavedPairResult(
        double Part1Rotation,
        double Part2Rotation,
        double Part2OffsetX,
        double Part2OffsetY,
        int StrategyType,
        int TestNumber,
        double CandidateSpacing,
        double RotatedArea,
        double BoundingWidth,
        double BoundingHeight,
        double OptimalRotation,
        bool Keep,
        string Reason,
        double TrueArea,
        IReadOnlyList<double> HullAngles
    );

    /// <summary>The saved best-fit results for one drawing at one plate size and spacing.</summary>
    public sealed record SavedPairSet(
        double PlateWidth,
        double PlateHeight,
        double Spacing,
        IReadOnlyList<SavedPairResult> Results
    );

    /// <summary>
    /// Persists best-fit pair caches across .nest round trips. Implementations own their
    /// cache; readers and writers never assume one exists, and a file without saved sets is
    /// always valid because the data can be recomputed.
    /// </summary>
    public interface IBestFitStore
    {
        /// <summary>
        /// Sets to save for <paramref name="drawing"/>, limited to the given
        /// (plate width, plate length, part spacing) keys.
        /// </summary>
        IReadOnlyList<SavedPairSet> Export(
            Drawing drawing,
            IReadOnlySet<(double Width, double Length, double Spacing)> plateKeys
        );

        /// <summary>Restores saved sets for <paramref name="drawing"/>.</summary>
        void Import(Drawing drawing, IReadOnlyList<SavedPairSet> sets);
    }
}

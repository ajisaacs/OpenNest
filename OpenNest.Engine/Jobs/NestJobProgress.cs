namespace OpenNest;

public enum NestJobStage
{
    EvaluatingCandidate,
    PlateCommitted,
}

/// <summary>
/// Whole-job progress. Counts change only after a physical sheet commits; LegacyProgress is optional
/// non-authoritative detail from a plate nester while its candidate remains under evaluation.
/// </summary>
public sealed record NestJobProgress(
    NestJobStage Stage,
    string StockId,
    int PlateIndex,
    int CommittedPlates,
    int CommittedParts,
    NestProgress LegacyProgress = null
);

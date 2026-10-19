using System.Collections.Generic;

namespace OpenNest.Engine.Jobs;

public enum NestJobStage
{
    EvaluatingCandidate,
    PlateCommitted,

    /// <summary>
    /// The engine's best whole-job plan so far, in <see cref="NestJobProgress.Plan"/>. Not
    /// validated and possibly replaced by a later plan; never counts as committed.
    /// </summary>
    PlanImproved,
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
    NestProgress LegacyProgress = null,
    NestJobResult Plan = null
)
{
    /// <summary>
    /// <see cref="Plan"/> as displayable sheets, filled in by <see cref="NestPipeline"/> for
    /// <see cref="NestJobStage.PlanImproved"/> reports. Parts use detached drawings built from
    /// the job geometry, so they never touch the caller's drawings or quantities.
    /// </summary>
    public IReadOnlyList<ProposedPlate> PlanSheets { get; init; }
}

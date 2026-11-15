using System;
using System.Collections.Generic;
using System.Threading;

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

    /// <summary>
    /// Another complete whole-job plan the engine found that is not its best, in
    /// <see cref="NestJobProgress.Plan"/>: a different layout a front end may offer beside the
    /// best. Optional, unranked, not validated, and never the engine's result.
    /// </summary>
    PlanAlternative,
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
    /// <see cref="NestJobStage.PlanImproved"/> and <see cref="NestJobStage.PlanAlternative"/>
    /// reports. Parts use detached drawings built from the job geometry, so they never touch
    /// the caller's drawings or quantities.
    /// </summary>
    public IReadOnlyList<ProposedPlate> PlanSheets { get; init; }

    /// <summary>
    /// Turns <see cref="Plan"/> into a proposal exactly as the pipeline does an engine's result:
    /// validated, then bound to the caller's drawings. Filled in with <see cref="PlanSheets"/>,
    /// for a caller that stops the search to use this plan. Call it only once the solve that
    /// reported the plan has ended.
    /// </summary>
    public Func<CancellationToken, NestPipelineResult> AcceptPlan { get; init; }
}

using System;
using OpenNest;

namespace OpenNest.Engine.Jobs;

/// <summary>Immutable per-job options; selection never changes the legacy global registry.</summary>
public sealed class NestJobOptions
{
    public NestJobOptions(
        string placementStrategy = "Lattice",
        int? maxPlates = null,
        double salvageRate = 0,
        double minimumSalvageDimension = 0,
        bool searchUntilCancelled = false,
        CutOffAxis? offcutCut = null
    )
    {
        if (offcutCut is { } axis && !Enum.IsDefined(axis))
            throw new ArgumentOutOfRangeException(nameof(offcutCut));
        ArgumentException.ThrowIfNullOrWhiteSpace(placementStrategy);
        if (maxPlates <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPlates));
        if (!double.IsFinite(salvageRate) || salvageRate < 0 || salvageRate > 1)
            throw new ArgumentOutOfRangeException(nameof(salvageRate));
        if (!double.IsFinite(minimumSalvageDimension) || minimumSalvageDimension < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSalvageDimension));
        PlacementStrategy = placementStrategy;
        MaxPlates = maxPlates;
        SalvageRate = salvageRate;
        MinimumSalvageDimension = minimumSalvageDimension;
        SearchUntilCancelled = searchUntilCancelled;
        OffcutCut = offcutCut;
    }

    /// <summary>
    /// When set, only the edge strips one cut along this axis frees count as a sheet's offcut, for
    /// salvage credit and for the compactness tie-break between equal-cost plans:
    /// <see cref="CutOffAxis.Horizontal"/> keeps full-width strips at the bottom or top (the drop
    /// off the end of a sheet), <see cref="CutOffAxis.Vertical"/> full-length ones at the sides.
    /// Null (the default) counts the largest of all four.
    /// </summary>
    public CutOffAxis? OffcutCut { get; }

    /// <summary>
    /// Asks engines that support it to keep searching after their normal search, reporting
    /// each better plan (<see cref="NestJobStage.PlanImproved"/>) until the solve's token is
    /// cancelled, which then ends the solve with <see cref="OperationCanceledException"/>.
    /// Meant for a caller that keeps the best reported plan at a time limit, such as
    /// <see cref="NestingEngines.DeadlineNestingEngine"/>. Ignored when the token cannot be
    /// cancelled. Results then depend on how long the search ran.
    /// </summary>
    public bool SearchUntilCancelled { get; }

    /// <summary>A copy of these options with <see cref="SearchUntilCancelled"/> set.</summary>
    public NestJobOptions WithSearchUntilCancelled(bool value) =>
        new(PlacementStrategy, MaxPlates, SalvageRate, MinimumSalvageDimension, value, OffcutCut)
        {
            CommonLineUsage = CommonLineUsage,
            CommonLineKerf = CommonLineKerf,
        };

    /// <summary>
    /// When the pipeline may place parts that allow it on common lines, one
    /// <see cref="CommonLineKerf"/> apart (see docs/common-line-cutting.md). Off by default;
    /// without a positive kerf it acts as off.
    /// </summary>
    public CommonLineUsage CommonLineUsage
    {
        get => commonLineUsage;
        init => commonLineUsage = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(CommonLineUsage));
    }

    private readonly CommonLineUsage commonLineUsage;
    private readonly double commonLineKerf;

    /// <summary>The cutting kerf for the job's material and thickness, in job units; zero when unknown.</summary>
    public double CommonLineKerf
    {
        get => commonLineKerf;
        init => commonLineKerf = double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(CommonLineKerf));
    }

    /// <summary>True when common lines may be made: a level is on and the kerf is positive.</summary>
    public bool UsesCommonLines => CommonLineUsage != CommonLineUsage.Off && CommonLineKerf > 0;

    /// <summary>Fraction of eligible edge-offcut area credited by <see cref="NestJobCost.NetSheetCost"/> (0..1).</summary>
    public double SalvageRate { get; }

    /// <summary>Both offcut dimensions must meet this caller-supplied minimum in job units.
    /// Zero disables credit; scraps and holes are never credited.</summary>
    public double MinimumSalvageDimension { get; }

    public string PlacementStrategy { get; }

    /// <summary>Maximum physical sheets to commit, or null for no explicit cap.</summary>
    public int? MaxPlates { get; }
}

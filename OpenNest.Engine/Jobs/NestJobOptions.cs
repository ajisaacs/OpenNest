using System;

namespace OpenNest.Engine.Jobs;

/// <summary>Immutable per-job options; selection never changes the legacy global registry.</summary>
public sealed class NestJobOptions
{
    public NestJobOptions(
        string placementStrategy = "Fill",
        int? maxPlates = null,
        double salvageRate = 0,
        double minimumSalvageDimension = 0,
        bool searchUntilCancelled = false
    )
    {
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
    }

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
        new(PlacementStrategy, MaxPlates, SalvageRate, MinimumSalvageDimension, value);

    /// <summary>Fraction of eligible edge-offcut area credited by <see cref="NestJobCost.NetSheetCost"/> (0..1).</summary>
    public double SalvageRate { get; }

    /// <summary>Both offcut dimensions must meet this caller-supplied minimum in job units.
    /// Zero disables credit; scraps and holes are never credited.</summary>
    public double MinimumSalvageDimension { get; }

    public string PlacementStrategy { get; }

    /// <summary>Maximum physical sheets to commit, or null for no explicit cap.</summary>
    public int? MaxPlates { get; }
}

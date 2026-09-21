using System;

namespace OpenNest.Engine.Jobs;

/// <summary>Immutable per-job options; selection never changes the legacy global registry.</summary>
public sealed class NestJobOptions
{
    public NestJobOptions(
        string placementStrategy = "Default",
        int? maxPlates = null,
        double salvageRate = 0,
        double minimumSalvageDimension = 0
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
    }

    /// <summary>Fraction of eligible edge-offcut area credited by StockLadder (0..1).</summary>
    public double SalvageRate { get; }

    /// <summary>Both offcut dimensions must meet this caller-supplied minimum in job units.
    /// Zero disables credit; scraps and holes are never credited.</summary>
    public double MinimumSalvageDimension { get; }

    public string PlacementStrategy { get; }

    /// <summary>Maximum physical sheets to commit, or null for no explicit cap.</summary>
    public int? MaxPlates { get; }
}

using System;

namespace OpenNest;

/// <summary>Immutable per-job options; selection never changes the legacy global registry.</summary>
public sealed class NestJobOptions
{
    public NestJobOptions(string placementStrategy = "Default", int? maxPlates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placementStrategy);
        if (maxPlates <= 0) throw new ArgumentOutOfRangeException(nameof(maxPlates));
        PlacementStrategy = placementStrategy;
        MaxPlates = maxPlates;
    }

    public string PlacementStrategy { get; }
    /// <summary>Maximum physical sheets to commit, or null for no explicit cap.</summary>
    public int? MaxPlates { get; }
}

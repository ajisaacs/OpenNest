using System;

using OpenNest.Engine.Jobs.Placement;
namespace OpenNest.Engine.Jobs;

/// <summary>
/// Instance-scoped strategy resolution for the whole-job runner. All four built-in strategies
/// resolve to a filler-backed plate nester from <see cref="PlacementStrategies"/>. Selection is
/// explicit per job; no shared mutable strategy setting is read or modified. Names match
/// case-sensitively ("Default" is Fill's name in earlier releases); unknown keys reject.
/// </summary>
public static class PlateNesterFactory
{
    public static IPlateNester Create(string strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var entry =
            PlacementStrategies.Find(strategy, StringComparison.Ordinal)
            ?? throw new NotSupportedException($"Unknown placement strategy: {strategy}.");
        return new PlateNester(entry.CreateFiller);
    }
}

using System;

using OpenNest.Engine.Jobs.Placement;
namespace OpenNest.Engine.Jobs;

/// <summary>
/// Instance-scoped strategy resolution for the whole-job runner. All four built-in strategies
/// resolve directly to filler-backed plate nesters. Selection is explicit per job; no shared
/// mutable strategy setting is read or modified. Unknown keys reject.
/// </summary>
public static class PlateNesterFactory
{
    public static IPlateNester Create(string strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy switch
        {
            "Default" => new DefaultPlateNester(),
            "Strip" => new StripPlateNester(),
            "Vertical Remnant" => RemnantPlateNester.Vertical(),
            "Horizontal Remnant" => RemnantPlateNester.Horizontal(),
            _ => throw new NotSupportedException($"Unknown placement strategy: {strategy}."),
        };
    }
}

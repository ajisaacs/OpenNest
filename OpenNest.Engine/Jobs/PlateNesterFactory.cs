using System;
namespace OpenNest;

/// <summary>
/// Instance-scoped strategy resolution for the whole-job runner. Default and Strip resolve to the
/// migrated built-in plate nesters; the remnant strategies still use the legacy adapter during
/// rollout. The process-global NestEngineRegistry (including plugin registrations and
/// ActiveEngineName) is neither read nor modified. Unknown keys reject.
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
            "Vertical Remnant" => new LegacyPlateNesterAdapter(plate => new VerticalRemnantEngine(plate)),
            "Horizontal Remnant" => new LegacyPlateNesterAdapter(plate => new HorizontalRemnantEngine(plate)),
            _ => throw new NotSupportedException($"Unknown placement strategy: {strategy}.")
        };
    }
}

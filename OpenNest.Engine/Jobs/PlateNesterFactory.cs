using System;
namespace OpenNest;

/// <summary>
/// Instance-scoped strategy resolution for the whole-job runner. The built-in strategies map
/// to private engine factories; the process-global NestEngineRegistry (including plugin
/// registrations and ActiveEngineName) is neither read nor modified. Unknown keys reject.
/// </summary>
public static class PlateNesterFactory
{
    public static IPlateNester Create(string strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy switch
        {
            "Default" => new LegacyPlateNesterAdapter(plate => new DefaultNestEngine(plate)),
            "Strip" => new LegacyPlateNesterAdapter(plate => new StripNestEngine(plate)),
            "Vertical Remnant" => new LegacyPlateNesterAdapter(plate => new VerticalRemnantEngine(plate)),
            "Horizontal Remnant" => new LegacyPlateNesterAdapter(plate => new HorizontalRemnantEngine(plate)),
            _ => throw new NotSupportedException($"Unknown placement strategy: {strategy}.")
        };
    }
}

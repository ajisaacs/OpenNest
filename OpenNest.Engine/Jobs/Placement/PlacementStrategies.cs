using System;
using System.Collections.Generic;
using OpenNest.Engine.Jobs.Placement.Fillers;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// The built-in placement strategies by name: the filler policy and layout behind each one.
/// <see cref="PlateNesterFactory"/> (whole jobs) and <see cref="PlateFillService"/> (interactive
/// fills) both resolve names here, each with its own case rule.
/// </summary>
internal static class PlacementStrategies
{
    /// <summary>Name of the Fill strategy in earlier releases; accepted as an alias.</summary>
    private const string LegacyFillName = "Default";

    /// <summary>One built-in strategy: its canonical name and the filler it runs.</summary>
    internal sealed record Entry(string Name, PlateFillPolicy Policy, PlateLayout Layout)
    {
        public PlateFiller CreateFiller(Plate plate) => new(plate, Policy, Layout);
    }

    /// <summary>In <see cref="PlateFillService.BuiltInStrategies"/> order.</summary>
    internal static IReadOnlyList<Entry> Entries { get; } =
    [
        new("Fill", PlateFillPolicy.Fill, PlateLayout.FillThenPack),
        new("Strip", PlateFillPolicy.Fill, PlateLayout.Strips),
        new("Vertical Remnant", PlateFillPolicy.VerticalRemnant, PlateLayout.FillThenPack),
        new("Horizontal Remnant", PlateFillPolicy.HorizontalRemnant, PlateLayout.FillThenPack),
    ];

    /// <summary>
    /// Finds the strategy <paramref name="name"/> names under <paramref name="comparison"/>;
    /// "Default", the Fill strategy's earlier name, finds Fill. Null when none matches.
    /// </summary>
    internal static Entry Find(string name, StringComparison comparison)
    {
        if (name == null)
            return null;
        if (name.Equals(LegacyFillName, comparison))
            return Entries[0];
        foreach (var entry in Entries)
        {
            if (entry.Name.Equals(name, comparison))
                return entry;
        }
        return null;
    }
}

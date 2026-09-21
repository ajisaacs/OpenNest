using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Public single-plate placement service over the internal fillers. Resolves one of the four
/// built-in placement strategies by explicit name — no process-global registry state is read or
/// modified. Operations return proposed <see cref="Part"/>s only; caller-owned plate mutation
/// (accepting a preview, adding parts to a plate) and cancel/discard behavior remain with the
/// caller, exactly as they were with the legacy single-plate engine surface.
/// </summary>
public static class PlateFillService
{
    /// <summary>The four built-in strategy names, in registry display order.</summary>
    public static IReadOnlyList<string> BuiltInStrategies { get; } =
    [
        "Default",
        "Strip",
        "Vertical Remnant",
        "Horizontal Remnant",
    ];

    public static List<Part> FillItem(
        string strategy,
        Plate plate,
        NestItem item,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var filler = CreateFiller(strategy, plate);
        return filler.Fill(item, workArea, progress, token);
    }

    public static List<Part> FillGroup(
        string strategy,
        Plate plate,
        List<Part> groupParts,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var filler = CreateFiller(strategy, plate);
        return filler.Fill(groupParts, workArea, progress, token);
    }

    public static List<Part> PackArea(
        string strategy,
        Plate plate,
        Box box,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var filler = CreateFiller(strategy, plate);
        return filler.PackArea(box, items, progress, token);
    }

    private static PlateFillerBase CreateFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(plate);

        // OrdinalIgnoreCase mirrors the legacy registry's ActiveEngineName matching so the
        // interactive callers keep their tolerant name handling while moving off global state.
        foreach (var candidate in BuiltInStrategies)
        {
            if (candidate.Equals(strategy, StringComparison.OrdinalIgnoreCase))
            {
                return candidate switch
                {
                    "Default" => new DefaultPlateFiller(plate),
                    "Strip" => new StripPlateFiller(plate),
                    "Vertical Remnant" => new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical),
                    _ => new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal),
                };
            }
        }

        throw new NotSupportedException(
            $"Unknown placement strategy: {strategy}. Known strategies: {string.Join(", ", BuiltInStrategies)}."
        );
    }
}

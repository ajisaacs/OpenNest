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
        return RequireFiller(strategy, plate).Fill(item, workArea, progress, token);
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
        return RequireFiller(strategy, plate).Fill(groupParts, workArea, progress, token);
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
        return RequireFiller(strategy, plate).PackArea(box, items, progress, token);
    }

    public static List<Part> Nest(
        string strategy,
        Plate plate,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    ) => Nest(strategy, plate, items, 0, progress, token);

    /// <summary>
    /// Whole-plate fill with the strategy's orchestration (fill-vs-pack, compaction). Returns
    /// proposed parts only; committing them to <paramref name="plate"/> stays with the caller.
    /// </summary>
    /// <param name="plateNumber">Plate index reported with progress, as the legacy engine's
    /// <c>PlateNumber</c> was by interactive multi-plate loops.</param>
    public static List<Part> Nest(
        string strategy,
        Plate plate,
        List<NestItem> items,
        int plateNumber,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ResolveStrategy(strategy, allowEmpty: false);
        var filler = CreateFiller(strategy, plate);
        filler.PlateNumber = plateNumber;
        return filler.Nest(items, progress, token);
    }

    /// <summary>
    /// Resolves a caller-supplied strategy name: null or empty means "Default"; otherwise the name
    /// must match a built-in strategy, matched case-insensitively like the legacy registry's
    /// ActiveEngineName so tolerant interactive callers keep working. Returns the canonical name;
    /// unknown names throw <see cref="NotSupportedException"/>.
    /// </summary>
    public static string ResolveStrategy(string strategy) => ResolveStrategy(strategy, true);

    /// <inheritdoc cref="ResolveStrategy(string)"/>
    /// <param name="allowEmpty">False rejects null/empty instead of defaulting (explicit service calls).</param>
    internal static string ResolveStrategy(string strategy, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            if (allowEmpty)
                return "Default";
            throw new NotSupportedException(
                $"Unknown placement strategy: '{strategy}'. Known strategies: {string.Join(", ", BuiltInStrategies)}."
            );
        }

        foreach (var candidate in BuiltInStrategies)
        {
            if (candidate.Equals(strategy, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new NotSupportedException(
            $"Unknown placement strategy: {strategy}. Known strategies: {string.Join(", ", BuiltInStrategies)}."
        );
    }

    /// <summary>
    /// Builds the filler for an optional strategy (null/empty = Default). Internal so the
    /// engine-side multi-plate orchestrators share one resolution/rejection contract.
    /// </summary>
    internal static PlateFillerBase CreateFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        return ResolveStrategy(strategy) switch
        {
            "Default" => new DefaultPlateFiller(plate),
            "Strip" => new StripPlateFiller(plate),
            "Vertical Remnant" => new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical),
            _ => new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal),
        };
    }

    /// <summary>Public operations require an explicit strategy name (null is an argument error).</summary>
    private static PlateFillerBase RequireFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        // An explicit empty string is an unknown strategy, not the orchestrator's
        // null-means-Default defaulting; only the orchestrator boundary may default.
        ResolveStrategy(strategy, allowEmpty: false);
        return CreateFiller(strategy, plate);
    }
}

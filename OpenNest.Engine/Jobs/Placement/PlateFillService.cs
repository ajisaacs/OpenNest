using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
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
    /// <summary>Strategy used when a caller selection names none.</summary>
    public const string DefaultStrategy = "Fill";

    /// <summary>Name of the Fill strategy in earlier releases; accepted as an alias.</summary>
    private const string LegacyFillName = "Default";

    /// <summary>The four built-in strategy names, in registry display order.</summary>
    public static IReadOnlyList<string> BuiltInStrategies { get; } =
    [
        "Fill",
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
        var filler = RequireFiller(strategy, plate);
        if (!InteractiveClamps.HasZones(plate))
            return filler.Fill(item, workArea, progress, token);
        var result = FillClearAreas(plate, workArea, item, token,
            (area, demand) => filler.Fill(demand, area, null, token));
        ReportSafe(progress, result, workArea);
        return result;
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
        var filler = RequireFiller(strategy, plate);
        if (!InteractiveClamps.HasZones(plate))
            return filler.Fill(groupParts, workArea, progress, token);
        var result = ClearAreas(plate, workArea, token, (area, _) =>
            filler.Fill(groupParts, area, null, token), 0);
        ReportSafe(progress, result, workArea);
        return result;
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
        var filler = RequireFiller(strategy, plate);
        if (!InteractiveClamps.HasZones(plate))
            return filler.PackArea(box, items, progress, token);
        var result = new List<Part>();
        foreach (var area in ClearRegionBoxes(plate, box))
        {
            token.ThrowIfCancellationRequested();
            var remaining = items.Select(item => CopyDemand(item,
                item.Quantity > 0 ? System.Math.Max(0, item.Quantity - result.Count(part =>
                    ReferenceEquals(part.BaseDrawing, item.Drawing))) : item.Quantity)).ToList();
            var proposed = filler.PackArea(area, remaining, null, token);
            AddClear(result, proposed, plate, area, items);
        }
        ReportSafe(progress, result, box);
        return result;
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
        if (!InteractiveClamps.HasZones(plate))
            return filler.Nest(items, progress, token);

        // Interactive proposals are never committed here. Unlike the unconstrained
        // strategies, partition the sheet at static keep-outs before searching, then
        // certify each accepted material footprint against the full zone and occupants.
        var result = new List<Part>();
        foreach (var item in items.OrderBy(item => item.Priority))
        {
            token.ThrowIfCancellationRequested();
            var proposed = FillClearAreas(plate, plate.WorkArea(), item, token,
                (area, demand) => filler.Fill(demand, area, null, token), result);
            result.AddRange(proposed);
            if (item.Quantity > 0)
                item.Quantity = System.Math.Max(0, item.Quantity - proposed.Count);
        }
        ReportSafe(progress, result, plate.WorkArea(), plateNumber);
        return result;
    }

    // Do not expose a strategy's unverified intermediate placements: the UI can
    // accept a progress preview before this service returns its certified proposal.
    private static void ReportSafe(IProgress<NestProgress> progress, List<Part> parts,
        Box area, int plateNumber = 0) => progress?.Report(new NestProgress
        {
            BestParts = parts,
            ActiveWorkArea = area,
            PlateNumber = plateNumber,
            IsOverallBest = true,
            Description = $"Clamp-clear proposal: {parts.Count} parts",
        });

    private static NestItem CopyDemand(NestItem item, int quantity) => new()
    {
        Drawing = item.Drawing,
        Quantity = quantity,
        Priority = item.Priority,
        RotationStart = item.RotationStart,
        RotationEnd = item.RotationEnd,
        StepAngle = item.StepAngle,
    };

    private static List<Part> FillClearAreas(Plate plate, Box box, NestItem item,
        CancellationToken token, Func<Box, NestItem, List<Part>> fill,
        List<Part> previouslyPlaced = null) =>
        ClearAreas(plate, box, token, fill, item.Quantity, item, previouslyPlaced);

    private static List<Part> ClearAreas(Plate plate, Box box, CancellationToken token,
        Func<Box, NestItem, List<Part>> fill, int quantity, NestItem item = null,
        List<Part> previouslyPlaced = null)
    {
        var result = new List<Part>();
        foreach (var area in ClearRegionBoxes(plate, box))
        {
            token.ThrowIfCancellationRequested();
            if (quantity > 0 && result.Count >= quantity)
                break;
            var demand = item == null ? null : CopyDemand(item,
                quantity > 0 ? quantity - result.Count : quantity);
            AddClear(result, fill(area, demand), plate, area,
                item == null ? null : new List<NestItem> { item }, previouslyPlaced);
        }
        return result;
    }

    private static List<Box> ClearRegionBoxes(Plate plate, Box area)
    {
        var obstacles = InteractiveClamps.ClearanceRectangles(plate).ToList();
        obstacles.AddRange(plate.Parts.Where(part => !part.BaseDrawing.IsCutOff)
            .Select(part => part.BoundingBox.Offset(plate.PartSpacing)));
        return new RemnantFinder(area, obstacles).FindRemnants();
    }

    private static void AddClear(List<Part> accepted, List<Part> proposed, Plate plate,
        Box area, List<NestItem> demand = null, List<Part> previouslyPlaced = null)
    {
        if (proposed == null)
            return;
        foreach (var part in proposed)
        {
            var bounds = part.BoundingBox;
            if (bounds.Left < area.Left - 1e-6 || bounds.Right > area.Right + 1e-6
                || bounds.Bottom < area.Bottom - 1e-6 || bounds.Top > area.Top + 1e-6)
                continue;
            if (demand != null && !demand.Any(item =>
                ReferenceEquals(item.Drawing, part.BaseDrawing) &&
                (item.Quantity <= 0 || accepted.Count(p =>
                    ReferenceEquals(p.BaseDrawing, item.Drawing)) < item.Quantity)))
                continue;
            if (InteractiveClamps.ValidationKeepOuts(plate).Any(zone =>
                Overlaps(bounds, zone) && InteractiveClamps.MaterialInKeepOut(part, zone)))
                continue;
            if (plate.Parts.Any(existing => !existing.BaseDrawing.IsCutOff &&
                Overlaps(bounds, existing.BoundingBox.Offset(plate.PartSpacing)))
                || accepted.Any(existing => Overlaps(bounds,
                    existing.BoundingBox.Offset(plate.PartSpacing)))
                || (previouslyPlaced?.Any(existing => Overlaps(bounds,
                    existing.BoundingBox.Offset(plate.PartSpacing))) ?? false))
                continue;
            accepted.Add(part);
        }
    }

    private static bool Overlaps(Box a, Box b) => a.Left < b.Right && a.Right > b.Left
        && a.Bottom < b.Top && a.Top > b.Bottom;

    /// <summary>
    /// Resolves a caller-supplied strategy name: null or empty means "Fill"; otherwise the name
    /// must match a built-in strategy, matched case-insensitively like the legacy registry's
    /// ActiveEngineName so tolerant interactive callers keep working. "Default", the Fill
    /// strategy's name in earlier releases, still resolves to "Fill". Returns the canonical name;
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
                return DefaultStrategy;
            throw new NotSupportedException(
                $"Unknown placement strategy: '{strategy}'. Known strategies: {string.Join(", ", BuiltInStrategies)}."
            );
        }

        if (strategy.Equals(LegacyFillName, StringComparison.OrdinalIgnoreCase))
            return DefaultStrategy;
        foreach (var candidate in BuiltInStrategies)
        {
            if (candidate.Equals(strategy, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new NotSupportedException(
            $"Unknown placement strategy: {strategy}. Known strategies: {string.Join(", ", BuiltInStrategies)}."
        );
    }

    /// <summary>Builds the filler for an optional strategy (null/empty = Fill).</summary>
    private static PlateFillerBase CreateFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        return ResolveStrategy(strategy) switch
        {
            "Fill" => new DefaultPlateFiller(plate),
            "Strip" => new StripPlateFiller(plate),
            "Vertical Remnant" => new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical),
            _ => new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal),
        };
    }

    /// <summary>Public operations require an explicit strategy name (null is an argument error).</summary>
    private static PlateFillerBase RequireFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        // An explicit empty string is an unknown strategy; only ResolveStrategy(string),
        // used for caller selections, maps null/empty to Fill.
        ResolveStrategy(strategy, allowEmpty: false);
        return CreateFiller(strategy, plate);
    }
}

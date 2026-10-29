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

    /// <summary>The four built-in strategy names, in registry display order.</summary>
    public static IReadOnlyList<string> BuiltInStrategies { get; } =
    [
        "Fill",
        "Strip",
        "Vertical Remnant",
        "Horizontal Remnant",
    ];

    /// <summary>Why no strategy fills a plate with a sheet outline (see <see cref="Refusal"/>).</summary>
    public const string OutlinedSheetMessage =
        "Fill strategies need a rectangular sheet. Nest an outlined sheet with Auto Nest and the Irregular engine.";

    /// <summary>
    /// Why the fill strategies cannot fill <paramref name="plate"/>, or null when they can. Callers
    /// show it to the user instead of calling an operation that would throw it.
    /// </summary>
    public static string Refusal(Plate plate) => plate?.Outline == null ? null : OutlinedSheetMessage;

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

        // Two clamp-clear layouts compete: the area filled as if the clamps were absent less
        // the parts that hit one, and the area trimmed past the clamps' edge strip.
        var demand = new List<NestItem> { item };
        return FillBesideClamps(filler, plate, workArea, progress, token,
            (area, sink) => FillDroppingClampHits(filler, plate, item, demand, area, sink, token),
            demand, 1);
    }

    private static List<Part> FillDroppingClampHits(PlateFiller filler, Plate plate,
        NestItem item, List<NestItem> demand, Box area, IProgress<NestProgress> progress,
        CancellationToken token)
    {
        // Ask for dropped copies again, within a bounded number of tries, when a quantity
        // is requested.
        var result = new List<Part>();
        var request = item.Quantity;
        for (var attempt = 0; attempt <= MaxClampRefills; attempt++)
        {
            var proposed = filler.Fill(CopyDemand(item, request), area, progress, token);
            result = new List<Part>();
            AddClear(result, proposed, plate, area, demand);
            if (item.Quantity <= 0 || result.Count >= item.Quantity
                || proposed.Count < request || token.IsCancellationRequested)
                break;
            request += item.Quantity - result.Count;
        }
        return result;
    }

    /// <summary>
    /// Fills <paramref name="workArea"/> ignoring the clamps and drops the parts that hit one
    /// (a layout packed around the clamps is usually worse). When every clamp in the area lies
    /// on one edge, also fills the area with that edge moved past them, as a larger edge
    /// spacing would, and keeps the better layout by the filler's own ranking; a tie keeps the
    /// first. Previews never fall below the layout already found.
    /// </summary>
    private static List<Part> FillBesideClamps(PlateFiller filler, Plate plate, Box workArea,
        IProgress<NestProgress> progress, CancellationToken token,
        Func<Box, IProgress<NestProgress>, List<Part>> fill, List<NestItem> demand, int groupSize)
    {
        var dropped = fill(workArea, ClearProgress(progress, plate, workArea, demand, groupSize, null, null));
        var strip = ClampFreeArea(plate, workArea);
        if (strip == null || token.IsCancellationRequested)
            return dropped;

        var beside = fill(strip, ClearProgress(progress, plate, strip, demand, groupSize,
            dropped, (candidate, current) => filler.IsBetterFill(candidate, current, workArea)));
        return filler.IsBetterFill(beside, dropped, workArea) ? beside : dropped;
    }

    /// <summary>
    /// <paramref name="area"/> with one edge moved past every clamp keep-out it meets, or null
    /// when it meets none or they do not all lie on one of its edges.
    /// </summary>
    internal static Box ClampFreeArea(Plate plate, Box area)
    {
        var keepOuts = InteractiveClamps.ClearanceRectangles(plate)
            .Where(k => k.Left < area.Right && k.Right > area.Left
                && k.Bottom < area.Top && k.Top > area.Bottom)
            .ToList();
        if (keepOuts.Count == 0)
            return null;

        Box result = null;
        if (keepOuts.All(k => k.Bottom <= area.Bottom))
        {
            var bottom = keepOuts.Max(k => k.Top);
            result = new Box(area.Left, bottom, area.Length, area.Top - bottom);
        }
        else if (keepOuts.All(k => k.Top >= area.Top))
        {
            var top = keepOuts.Min(k => k.Bottom);
            result = new Box(area.Left, area.Bottom, area.Length, top - area.Bottom);
        }
        else if (keepOuts.All(k => k.Left <= area.Left))
        {
            var left = keepOuts.Max(k => k.Right);
            result = new Box(left, area.Bottom, area.Right - left, area.Width);
        }
        else if (keepOuts.All(k => k.Right >= area.Right))
        {
            var right = keepOuts.Min(k => k.Left);
            result = new Box(area.Left, area.Bottom, right - area.Left, area.Width);
        }
        return result != null && result.Length > 0 && result.Width > 0 ? result : null;
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

        // As FillItem; a copy is dropped whole when any member hits a clamp.
        var groupSize = groupParts.Count;
        return FillBesideClamps(filler, plate, workArea, progress, token, (area, sink) =>
        {
            var result = new List<Part>();
            AddClear(result, filler.Fill(groupParts, area, sink, token), plate, area,
                groupSize: groupSize);
            return result;
        }, null, groupSize);
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
            // Fill/Remnant whole-plate orchestration skips exhausted demand; the
            // single-item Fill API uses zero to mean unlimited. Strip has its own
            // historical zero-quantity behavior, preserved here.
            if (item.Quantity <= 0 && ResolveStrategy(strategy) != "Strip")
                continue;
            var proposed = FillClearAreas(plate, plate.WorkArea(), item, token,
                (area, demand) => filler.Fill(demand, area, null, token), result);
            result.AddRange(proposed);
            if (item.Quantity > 0)
                item.Quantity = System.Math.Max(0, item.Quantity - proposed.Count);
        }
        ReportSafe(progress, result, plate.WorkArea(), plateNumber);
        return result;
    }

    /// <summary>Extra fills a quantity request may run to replace copies dropped at clamps.</summary>
    private const int MaxClampRefills = 2;

    /// <summary>
    /// Forwards a clamp-blind fill's progress with the parts that hit a clamp or an occupant
    /// removed by the same check as the returned proposal, so an accepted preview is never
    /// an unverified placement.
    /// </summary>
    /// <remarks>A preview is promoted only when it beats <paramref name="floor"/>, the
    /// layout an earlier competing fill already returned.</remarks>
    private static IProgress<NestProgress> ClearProgress(IProgress<NestProgress> progress,
        Plate plate, Box area, List<NestItem> demand, int groupSize, List<Part> floor,
        Func<List<Part>, List<Part>, bool> isBetter) =>
        progress == null ? null
            : new ClearPartsProgress(progress, plate, area, demand, groupSize, floor, isBetter);

    private sealed class ClearPartsProgress(IProgress<NestProgress> inner, Plate plate, Box area,
        List<NestItem> demand, int groupSize, List<Part> floor,
        Func<List<Part>, List<Part>, bool> isBetter) : IProgress<NestProgress>
    {
        public void Report(NestProgress value)
        {
            var clear = new List<Part>();
            AddClear(clear, value.BestParts, plate, area, demand, groupSize: groupSize);
            if (clear.Count == 0)
                return;
            inner.Report(new NestProgress
            {
                Phase = value.Phase,
                PlateNumber = value.PlateNumber,
                BestParts = clear,
                Description = value.Description,
                ActiveWorkArea = value.ActiveWorkArea,
                IsOverallBest = value.IsOverallBest && (floor == null || isBetter(clear, floor)),
            });
        }
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
        List<Part> previouslyPlaced = null, int groupSize = 1)
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
                item == null ? null : new List<NestItem> { item }, previouslyPlaced, groupSize);
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
        Box area, List<NestItem> demand = null, List<Part> previouslyPlaced = null,
        int groupSize = 1)
    {
        if (proposed == null || groupSize < 1)
            return;
        // FillLinear may append an incomplete trailing tile. Keep complete copies
        // ahead of it, but never expose the incomplete tail as a group.
        for (var start = 0; start + groupSize <= proposed.Count; start += groupSize)
        {
            // A group is one proposal: never return just its frame or just its
            // insert when a member collides with a clamp or occupied material.
            var batch = new List<Part>();
            for (var index = start; index < start + groupSize; index++)
            {
                if (!TryAddClear(batch, proposed[index], accepted, plate, area,
                    demand, previouslyPlaced))
                    break;
            }
            if (batch.Count == groupSize)
                accepted.AddRange(batch);
        }
    }

    private static bool TryAddClear(List<Part> batch, Part part, List<Part> accepted,
        Plate plate, Box area, List<NestItem> demand, List<Part> previouslyPlaced)
    {
        var bounds = part.BoundingBox;
        var full = plate.WorkArea();
        // The packer permits a tiny overhang only at a real sheet edge,
        // never across a remnant boundary cut by a clamp or another part.
        double Slack(double edge, double sheet) => System.Math.Abs(edge - sheet) < 1e-9
            ? NestTolerances.WorkAreaSlack : 1e-6;
        if (bounds.Left < area.Left - Slack(area.Left, full.Left)
            || bounds.Right > area.Right + Slack(area.Right, full.Right)
            || bounds.Bottom < area.Bottom - Slack(area.Bottom, full.Bottom)
            || bounds.Top > area.Top + Slack(area.Top, full.Top))
            return false;
        if (demand != null && !demand.Any(item =>
            ReferenceEquals(item.Drawing, part.BaseDrawing) &&
            (item.Quantity <= 0 || accepted.Count(p =>
                ReferenceEquals(p.BaseDrawing, item.Drawing)) + batch.Count(p =>
                ReferenceEquals(p.BaseDrawing, item.Drawing)) < item.Quantity)))
            return false;
        if (InteractiveClamps.ValidationKeepOuts(plate).Any(zone =>
            Overlaps(bounds, zone) && InteractiveClamps.MaterialInKeepOut(part, zone)))
            return false;
        if (plate.Parts.Any(existing => !existing.BaseDrawing.IsCutOff &&
            Overlaps(bounds, existing.BoundingBox.Offset(plate.PartSpacing))
            && InteractiveClamps.MaterialsTooClose(part, existing, plate.PartSpacing))
            || accepted.Any(existing => Overlaps(bounds,
                existing.BoundingBox.Offset(plate.PartSpacing))
                && InteractiveClamps.MaterialsTooClose(part, existing, plate.PartSpacing))
            || batch.Any(existing => Overlaps(bounds,
                existing.BoundingBox.Offset(plate.PartSpacing))
                && InteractiveClamps.MaterialsTooClose(part, existing, plate.PartSpacing))
            || (previouslyPlaced?.Any(existing => Overlaps(bounds,
                existing.BoundingBox.Offset(plate.PartSpacing))
                && InteractiveClamps.MaterialsTooClose(part, existing, plate.PartSpacing)) ?? false))
            return false;
        batch.Add(part);
        return true;
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

        return Find(strategy).Name;
    }

    private static PlacementStrategies.Entry Find(string strategy) =>
        PlacementStrategies.Find(strategy, StringComparison.OrdinalIgnoreCase)
        ?? throw new NotSupportedException(
            $"Unknown placement strategy: {strategy}. Known strategies: {string.Join(", ", BuiltInStrategies)}."
        );

    /// <summary>Builds the filler for an optional strategy (null/empty = Fill).</summary>
    private static PlateFiller CreateFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        return Find(ResolveStrategy(strategy)).CreateFiller(plate);
    }

    /// <summary>Public operations require an explicit strategy name (null is an argument error).</summary>
    private static PlateFiller RequireFiller(string strategy, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(plate);
        // Every filler lays parts out in a rectangle; only Auto Nest's Irregular engine honors an outline.
        if (Refusal(plate) is { } refusal)
            throw new InvalidOperationException(refusal);
        // An explicit empty string is an unknown strategy; only ResolveStrategy(string),
        // used for caller selections, maps null/empty to Fill.
        ResolveStrategy(strategy, allowEmpty: false);
        return CreateFiller(strategy, plate);
    }
}

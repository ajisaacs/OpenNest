using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Public single-plate placement service over the internal fillers. Resolves one of the four
/// built-in placement strategies by explicit name — no process-global registry state is read or
/// modified. Operations return proposed <see cref="Part"/>s only; caller-owned plate mutation
/// (accepting a preview, adding parts to a plate) and cancel/discard behavior remain with the
/// caller, exactly as they were with the legacy single-plate engine surface.
/// </summary>
public static partial class PlateFillService
{
    /// <summary>Strategy used when a caller selection names none.</summary>
    public const string DefaultStrategy = "Lattice";

    /// <summary>The four built-in strategy names, in registry display order.</summary>
    public static IReadOnlyList<string> BuiltInStrategies { get; } =
    [
        "Lattice",
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
            (area, sink) => FillDroppingClampHits(
                (demanded, box, inner) => filler.Fill(demanded, box, inner, token),
                plate, item, demand, area, sink, token),
            demand, 1);
    }

    internal static List<Part> FillDroppingClampHits(
        Func<NestItem, Box, IProgress<NestProgress>, List<Part>> fill, Plate plate,
        NestItem item, List<NestItem> demand, Box area, IProgress<NestProgress> progress,
        CancellationToken token)
    {
        // Ask for dropped copies again, within a bounded number of tries, when a quantity
        // is requested.
        var result = new List<Part>();
        var request = item.Quantity;
        var dropped = false;
        for (var attempt = 0; attempt <= MaxClampRefills; attempt++)
        {
            var proposed = fill(CopyDemand(item, request), area, progress);
            result = new List<Part>();
            AddClear(result, proposed, plate, area, demand);
            dropped = result.Count < proposed.Count;
            if (item.Quantity <= 0 || result.Count >= item.Quantity
                || proposed.Count < request || token.IsCancellationRequested)
                break;
            request += item.Quantity - result.Count;
        }
        if (dropped)
        {
            var before = result.Count;
            result = RefillBesideClamps(fill, plate, item, demand, area, result, token);
            // The progress sink re-certifies these and promotes them only over its floor.
            if (result.Count > before)
                progress?.Report(new NestProgress
                {
                    BestParts = result,
                    ActiveWorkArea = area,
                    IsOverallBest = true,
                    Description = $"Filled gaps beside clamps: {result.Count} parts",
                });
        }
        return result;
    }

    /// <summary>
    /// The better of two refills of a layout that lost parts at the clamps: its gaps filled
    /// with every part left in place, or the clamped edge's row taken out and refilled
    /// between the clamps, which lines the row up with the space the clamps leave instead
    /// of the grid the clamp-blind fill chose. The repacked row wins only with more parts.
    /// </summary>
    private static List<Part> RefillBesideClamps(
        Func<NestItem, Box, IProgress<NestProgress>, List<Part>> fill, Plate plate,
        NestItem item, List<NestItem> demand, Box area, List<Part> layout,
        CancellationToken token)
    {
        var gapFilled = new List<Part>(layout);
        FillGapsBesideClamps(fill, plate, item, demand, area, gapFilled, token);
        var band = ClampedEdgeBand(plate, area);
        if (band == null || token.IsCancellationRequested)
            return gapFilled;

        var repacked = layout.Where(part => !Overlaps(part.BoundingBox, band)).ToList();
        if (repacked.Count == layout.Count)
            return gapFilled;
        foreach (var slot in ClampedEdgeSlots(plate, area, band, repacked))
        {
            if (token.IsCancellationRequested
                || (item.Quantity > 0 && repacked.Count >= item.Quantity))
                break;
            if (!Fits(slot, item.Drawing.Program.BoundingBox()))
                continue;
            var remaining = item.Quantity > 0 ? item.Quantity - repacked.Count : item.Quantity;
            AddClear(repacked, fill(CopyDemand(item, remaining), slot, null), plate, slot, demand);
        }
        FillGapsBesideClamps(fill, plate, item, demand, area, repacked, token);
        return repacked.Count > gapFilled.Count ? repacked : gapFilled;
    }

    /// <summary>
    /// The free space along the clamped edge once its row is taken out, one slot between each
    /// pair of clamp keep-outs (and one past each end), each running from the edge to the
    /// nearest part beyond the band at the part spacing. Slots never overlap, so filling one
    /// never steals another's space.
    /// </summary>
    private static List<Box> ClampedEdgeSlots(Plate plate, Box area, Box band,
        List<Part> remaining)
    {
        var alongX = band.Length >= area.Length - Tolerance.Epsilon;
        var keepOuts = InteractiveClamps.ClearanceRectangles(plate)
            .Where(keepOut => Overlaps(keepOut, band))
            .Select(keepOut => alongX ? (Low: keepOut.Left, High: keepOut.Right)
                : (Low: keepOut.Bottom, High: keepOut.Top))
            .OrderBy(span => span.Low);
        var occupied = plate.Parts.Where(part => !part.BaseDrawing.IsCutOff)
            .Concat(remaining).Select(part => part.BoundingBox).ToList();
        var spacing = plate.PartSpacing;
        var slots = new List<Box>();

        void Add(double low, double high)
        {
            if (high - low <= Tolerance.Epsilon)
                return;
            var beside = occupied.Where(box => alongX
                ? box.Left < high && box.Right > low
                : box.Bottom < high && box.Top > low).ToList();
            Box slot;
            if (alongX && band.Bottom <= area.Bottom)
            {
                var top = beside.Select(box => box.Bottom - spacing).Append(area.Top).Min();
                slot = new Box(low, area.Bottom, high - low, top - area.Bottom);
            }
            else if (alongX)
            {
                var bottom = beside.Select(box => box.Top + spacing).Append(area.Bottom).Max();
                slot = new Box(low, bottom, high - low, area.Top - bottom);
            }
            else if (band.Left <= area.Left)
            {
                var right = beside.Select(box => box.Left - spacing).Append(area.Right).Min();
                slot = new Box(area.Left, low, right - area.Left, high - low);
            }
            else
            {
                var left = beside.Select(box => box.Right + spacing).Append(area.Left).Max();
                slot = new Box(left, low, area.Right - left, high - low);
            }
            if (slot.Length > 0 && slot.Width > 0)
                slots.Add(slot);
        }

        var start = alongX ? area.Left : area.Bottom;
        foreach (var span in keepOuts)
        {
            Add(start, span.Low);
            start = System.Math.Max(start, span.High);
        }
        Add(start, alongX ? area.Right : area.Top);
        return slots;
    }

    /// <summary>
    /// The strip of <paramref name="area"/> between its clamped edge and the far side of the
    /// clamp keep-outs, or null when <see cref="ClampFreeArea"/> finds no single clamped edge.
    /// </summary>
    internal static Box ClampedEdgeBand(Plate plate, Box area)
    {
        var free = ClampFreeArea(plate, area);
        if (free == null)
            return null;
        if (free.Bottom > area.Bottom)
            return new Box(area.Left, area.Bottom, area.Length, free.Bottom - area.Bottom);
        if (free.Top < area.Top)
            return new Box(area.Left, free.Top, area.Length, area.Top - free.Top);
        if (free.Left > area.Left)
            return new Box(area.Left, area.Bottom, free.Left - area.Left, area.Width);
        return new Box(free.Right, area.Bottom, area.Right - free.Right, area.Width);
    }

    /// <summary>Gap-fill rounds after dropping clamp hits; a round that adds nothing ends it.</summary>
    private const int MaxGapFillRounds = 3;

    /// <summary>
    /// Fills the clear rectangles left once the parts that hit a clamp are dropped: the
    /// clamp-blind layout put a part across each clamp, and dropping it can leave room for a
    /// part between two clamps or beside one. Each free rectangle large enough for the part is
    /// filled with the remaining demand and every proposal is certified like the main fill.
    /// Only adds parts; the main layout is never moved.
    /// </summary>
    private static void FillGapsBesideClamps(
        Func<NestItem, Box, IProgress<NestProgress>, List<Part>> fill, Plate plate,
        NestItem item, List<NestItem> demand, Box area, List<Part> result,
        CancellationToken token)
    {
        var size = item.Drawing.Program.BoundingBox();
        var keepOuts = InteractiveClamps.ClearanceRectangles(plate);
        for (var round = 0; round < MaxGapFillRounds; round++)
        {
            var before = result.Count;
            var obstacles = keepOuts.ToList();
            obstacles.AddRange(plate.Parts.Where(part => !part.BaseDrawing.IsCutOff)
                .Concat(result)
                .Select(part => part.BoundingBox.Offset(plate.PartSpacing)));
            foreach (var gap in new RemnantFinder(area, obstacles).FindRemnants())
            {
                if (token.IsCancellationRequested
                    || (item.Quantity > 0 && result.Count >= item.Quantity))
                    return;
                if (!Fits(gap, size))
                    continue;
                var remaining = item.Quantity > 0 ? item.Quantity - result.Count : item.Quantity;
                AddClear(result, fill(CopyDemand(item, remaining), gap, null), plate, gap, demand);
            }
            if (result.Count == before)
                return;
        }
    }

    private static bool Fits(Box gap, Box size)
    {
        const double slack = 1e-6;
        return (gap.Length + slack >= size.Length && gap.Width + slack >= size.Width)
            || (gap.Length + slack >= size.Width && gap.Width + slack >= size.Length);
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
        foreach (var item in items.OrderByDescending(item => item.Priority))
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
    internal static IProgress<NestProgress> ClearProgress(IProgress<NestProgress> progress,
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
        // The fillers permit the geometry tolerance's overhang at every edge of their area. A
        // sheet edge always allows it. An inner edge is an occupant (or cut-off) held off by the
        // part spacing, or a clamp keep-out inflated by at least the spacing slack, so the
        // overhang stays inside that margin when the spacing is at least the slack; the material
        // checks below still confirm occupants and clamps. Without that margin, hold the edge.
        var innerSlack = plate.PartSpacing >= NestTolerances.SpacingSlack
            ? NestTolerances.WorkAreaSlack : 1e-6;
        double Slack(double edge, double sheet) => System.Math.Abs(edge - sheet) < 1e-9
            ? NestTolerances.WorkAreaSlack : innerSlack;
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
    /// Resolves a caller-supplied strategy name: null or empty means "Lattice"; otherwise the name
    /// must match a built-in strategy, matched case-insensitively like the legacy registry's
    /// ActiveEngineName so tolerant interactive callers keep working. "Default" and "Fill", the
    /// strategy's names in earlier releases, still resolve to "Lattice". Returns the canonical name;
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

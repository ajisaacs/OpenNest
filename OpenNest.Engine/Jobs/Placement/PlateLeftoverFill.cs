using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>New parts proposed for the leftover space of a plate that already holds parts.</summary>
/// <param name="Plate">The nest's plate; never changed by the fill.</param>
/// <param name="Occupants">The plate's parts when the fill ran, in order; the commit refuses a
/// plate whose parts changed since.</param>
/// <param name="Parts">The proposed parts, clear of <paramref name="Occupants"/> and the clamps.</param>
public sealed record LeftoverPlateFill(Plate Plate, IReadOnlyList<Part> Occupants, IReadOnlyList<Part> Parts);

/// <summary>What <see cref="PlateLeftoverFill.Run"/> proposed.</summary>
/// <param name="Plates">One entry per plate that received parts, in the order given.</param>
/// <param name="Violations">Layout problems the independent check found in the proposals.</param>
/// <param name="Skipped">Occupied plates the fill cannot use: an outlined sheet, or one with
/// cut-offs.</param>
public sealed record LeftoverFillResult(
    IReadOnlyList<LeftoverPlateFill> Plates,
    IReadOnlyList<string> Violations,
    IReadOnlyList<Plate> Skipped)
{
    public static LeftoverFillResult Empty { get; } =
        new(Array.Empty<LeftoverPlateFill>(), Array.Empty<string>(), Array.Empty<Plate>());

    /// <summary>Copies of each drawing the proposals use, counting every physical copy of a plate
    /// (<see cref="Plate.Quantity"/>), by drawing reference.</summary>
    public IReadOnlyDictionary<Drawing, int> Placed()
    {
        var placed = new Dictionary<Drawing, int>(ReferenceEqualityComparer.Instance);
        foreach (var fill in Plates)
            foreach (var part in fill.Parts)
                placed[part.BaseDrawing] = placed.GetValueOrDefault(part.BaseDrawing)
                    + System.Math.Max(1, fill.Plate.Quantity);
        return placed;
    }
}

/// <summary>
/// Auto Nest's "current plates" leftover stage: fills the free space on plates that already hold
/// parts with the fill strategies, around the parts already there, and checks each proposal with
/// the same layout rules as a whole-job result, the existing parts held fixed. Whole-job engines
/// never place on an occupied plate; only these certified fill proposals do. Nothing is
/// committed: the plates and the caller's items are left unchanged.
/// </summary>
public static class PlateLeftoverFill
{
    /// <summary>Fill rounds per item on one plate; a round that adds nothing ends it.</summary>
    private const int MaxRounds = 8;

    /// <summary>True when <paramref name="plate"/> holds parts and the fill can use its leftover
    /// space: a rectangular sheet without cut-offs.</summary>
    public static bool CanFill(Plate plate) =>
        plate != null && HasParts(plate) && PlateFillService.Refusal(plate) == null
        && plate.CutOffs.Count == 0 && !plate.Parts.Any(p => p.BaseDrawing.IsCutOff);

    /// <summary>True when <paramref name="plate"/> holds at least one part that is not a cut-off.</summary>
    public static bool HasParts(Plate plate) => plate.Parts.Any(p => !p.BaseDrawing.IsCutOff);

    /// <summary>
    /// Fills each occupied plate of <paramref name="plates"/> (the nest's plates, in order: messages
    /// name a plate by its position here) in turn with what remains of
    /// <paramref name="items"/>: items by priority, then larger parts first, each into the largest
    /// free rectangles left beside the parts already there. A plate repeated
    /// <see cref="Plate.Quantity"/> times uses that many copies of each part it receives.
    /// </summary>
    public static LeftoverFillResult Run(
        IReadOnlyList<Plate> plates,
        IReadOnlyList<NestItem> items,
        string strategy,
        IProgress<NestProgress> progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(items);
        var remaining = items.Where(i => i?.Drawing != null && i.Quantity > 0)
            .Select(i => (Item: i, Left: i.Quantity))
            .ToList();
        var fills = new List<LeftoverPlateFill>();
        var violations = new List<string>();
        var skipped = new List<Plate>();

        for (var index = 0; index < plates.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var plate = plates[index];
            if (plate == null || !HasParts(plate))
                continue;
            if (!CanFill(plate))
            {
                skipped.Add(plate);
                continue;
            }
            if (remaining.All(r => r.Left <= 0))
                break;

            var copies = System.Math.Max(1, plate.Quantity);
            var occupants = plate.Parts.ToList();
            var accepted = new List<Part>();
            var caps = new Dictionary<Drawing, int>(ReferenceEqualityComparer.Instance);
            foreach (var (item, left) in remaining
                .OrderByDescending(r => r.Item.Priority)
                .ThenByDescending(r => r.Item.Drawing.Area))
            {
                var cap = left / copies;
                if (cap <= 0)
                    continue;
                caps[item.Drawing] = caps.GetValueOrDefault(item.Drawing) + cap;
                FillItem(plate, item, cap, strategy, accepted, token);
            }

            if (accepted.Count == 0)
                continue;
            var check = Check(plate, occupants, accepted, caps);
            violations.AddRange(check.Select(v => $"Plate {index + 1}: {v}"));
            fills.Add(new LeftoverPlateFill(plate, occupants.AsReadOnly(), accepted.AsReadOnly()));
            // Two items of one drawing share its parts: each takes what it can, in order.
            var used = accepted
                .GroupBy(p => p.BaseDrawing, ReferenceEqualityComparer.Instance)
                .ToDictionary(g => g.Key, g => g.Count() * copies, ReferenceEqualityComparer.Instance);
            for (var i = 0; i < remaining.Count; i++)
            {
                var drawing = remaining[i].Item.Drawing;
                var take = System.Math.Min(remaining[i].Left, used.GetValueOrDefault(drawing));
                used[drawing] = used.GetValueOrDefault(drawing) - take;
                remaining[i] = (remaining[i].Item, remaining[i].Left - take);
            }
            progress?.Report(new NestProgress
            {
                BestParts = occupants.Concat(accepted).ToList(),
                ActiveWorkArea = plate.WorkArea(),
                PlateNumber = index + 1,
                IsOverallBest = true,
                Description = $"Plate {index + 1}: {accepted.Count} parts added around existing parts",
            });
        }

        return new LeftoverFillResult(fills.AsReadOnly(), violations.AsReadOnly(), skipped.AsReadOnly());
    }

    /// <summary>
    /// Up to <paramref name="cap"/> copies of <paramref name="item"/> into the free rectangles of
    /// <paramref name="plate"/>, largest first, recomputing them after every accepted fill (free
    /// rectangles overlap). Every proposal is certified clear of the plate's parts, the parts
    /// already accepted and the clamps, and must keep the item's rotation limits.
    /// </summary>
    private static void FillItem(Plate plate, NestItem item, int cap, string strategy,
        List<Part> accepted, CancellationToken token)
    {
        var rotation = RotationPolicy.FromLegacy(item.StepAngle, item.RotationStart, item.RotationEnd,
            item.Drawing.Constraints?.Allow180Equivalent ?? false);
        var size = item.Drawing.Program.BoundingBox();
        var placed = 0;
        for (var round = 0; round < MaxRounds && placed < cap; round++)
        {
            var before = placed;
            foreach (var region in FreeRegions(plate, accepted))
            {
                token.ThrowIfCancellationRequested();
                if (placed >= cap)
                    break;
                if (!Fits(region, size))
                    continue;
                var demand = new NestItem
                {
                    Drawing = item.Drawing,
                    Quantity = cap - placed,
                    Priority = item.Priority,
                    RotationStart = item.RotationStart,
                    RotationEnd = item.RotationEnd,
                    StepAngle = item.StepAngle,
                };
                var proposed = PlateFillService.FillItem(strategy, plate, demand, region, null, token)
                    ?.Where(p => rotation.Allows(p.Rotation))
                    .ToList();
                var clear = PlateFillService.CertifyClear(plate, region, proposed, accepted,
                    new List<NestItem> { demand });
                if (clear.Count == 0)
                    continue;
                accepted.AddRange(clear);
                placed += clear.Count;
                // The free rectangles changed; start again from the largest.
                break;
            }
            if (placed == before)
                return;
        }
    }

    /// <summary>The plate's free rectangles beside its parts, the accepted parts and the clamps,
    /// largest first.</summary>
    private static List<Box> FreeRegions(Plate plate, List<Part> accepted)
    {
        var obstacles = InteractiveClamps.ClearanceRectangles(plate).ToList();
        obstacles.AddRange(plate.Parts.Where(part => !part.BaseDrawing.IsCutOff)
            .Concat(accepted)
            .Select(part => part.BoundingBox.Offset(plate.PartSpacing)));
        return new RemnantFinder(plate.WorkArea(), obstacles).FindRemnants()
            .OrderByDescending(box => box.Length * box.Width)
            .ToList();
    }

    private static bool Fits(Box region, Box size)
    {
        const double slack = 1e-6;
        return (region.Length + slack >= size.Length && region.Width + slack >= size.Width)
            || (region.Length + slack >= size.Width && region.Width + slack >= size.Length);
    }

    /// <summary>The whole-job layout rules on the plate's parts plus the proposal, the parts
    /// already there held fixed.</summary>
    private static List<string> Check(Plate plate, List<Part> occupants, List<Part> proposed,
        IReadOnlyDictionary<Drawing, int> caps)
    {
        var requirements = caps.ToDictionary(
            c => c.Key, c => (c.Key.Name, c.Value), (IEqualityComparer<Drawing>)ReferenceEqualityComparer.Instance);
        var fixedParts = new HashSet<Part>(occupants, ReferenceEqualityComparer.Instance);
        return NestLayoutCheck.Validate(
            new List<(Plate, List<Part>)> { (plate, occupants.Concat(proposed).ToList()) },
            requirements, fixedParts);
    }
}

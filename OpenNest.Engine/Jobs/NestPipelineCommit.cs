using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>Applies accepted layouts and total copy quantities to empty/new plates only.
/// Caller must keep the nest and its drawings stable from request construction through commit.</summary>
public static class NestPipelineCommit
{
    public static IReadOnlyList<Plate> ApplyToEmptyPlates(
        NestPipelineResult result,
        PlateManager manager,
        bool allowInvalid = false,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(manager);
        EnsureCommittable(result, allowInvalid, token);
        // Commit is synchronous on the caller's owning thread. Cancellation is checked
        // before mutation, not partway through attachment (which would leave half a job).
        manager.BeginBatch();
        try
        {
            return Attach(result, manager.GetOrCreateEmpty);
        }
        finally
        {
            manager.EndBatch();
        }
    }

    /// <summary>
    /// Same checks and commit as <see cref="ApplyToEmptyPlates"/> for callers without a
    /// <see cref="PlateManager"/> (no trailing empty sheet). <paramref name="nextPlate"/> is
    /// called once per non-empty proposed layout, after every check has passed, and must
    /// return a plate without parts that already belongs to the nest.
    /// </summary>
    public static IReadOnlyList<Plate> ApplyToPlates(
        NestPipelineResult result,
        Func<Plate> nextPlate,
        bool allowInvalid = false,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(nextPlate);
        EnsureCommittable(result, allowInvalid, token);
        return Attach(result, nextPlate);
    }

    private static void EnsureCommittable(NestPipelineResult result, bool allowInvalid, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(result);
        token.ThrowIfCancellationRequested();
        if (!result.CanKeep || (!result.IsValid && !allowInvalid))
            throw new InvalidOperationException("The nesting result cannot be committed without a keepable layout and explicit consent to its violations.");
        if (NestPipelineDrawingFreshness.Changes(result.Job, result.DrawingsByPartId).Count > 0
            || NestPipelineDrawingFreshness.BoundChanges(result.Job, result.Raw,
                result.Plates, result.DrawingsByPartId, result.IsValid, token).Count > 0)
            throw new InvalidOperationException("A drawing or proposed part changed after the nesting proposal was validated; run Auto Nest again.");
        token.ThrowIfCancellationRequested();
    }

    private static IReadOnlyList<Plate> Attach(NestPipelineResult result, Func<Plate> nextPlate)
    {
        var applied = new List<Plate>();
        foreach (var proposed in result.Plates)
        {
            if (proposed.Parts.Count == 0)
                continue;
            var plate = nextPlate();
            plate.Size = proposed.Stock.Size;
            plate.PartSpacing = proposed.Stock.PartSpacing;
            plate.EdgeSpacing = proposed.Stock.EdgeSpacing;
            plate.Quadrant = proposed.Stock.Quadrant;
            plate.Quantity = proposed.Quantity;
            SyncClampZones(plate, proposed.Stock);
            plate.Parts.AddRange(proposed.Parts);
            applied.Add(plate);
        }
        return applied.AsReadOnly();
    }

    /// <summary>
    /// The live plate keeps exactly the clamp keep-outs its layout was solved and validated
    /// against, including sheets created for this commit. Stock zones live in the placement
    /// frame; plate zones in plate coordinates, so the stock's translation is inverted. Zones
    /// already equal in the placement frame are left untouched (no change events). Replaced
    /// zones stay tied to the plate's clamp rail only when that rail recreates them (directly,
    /// or minus whichever clamps Auto Nest removed as a last resort, D8/C13); otherwise a
    /// later size or quadrant assignment would silently swap in other clamps.
    /// </summary>
    private static void SyncClampZones(Plate plate, NestPlateStock stock)
    {
        var left = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var bottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        // The rail the solved zones were resolved from wins over whatever the target follows;
        // explicit stock keeps the target's own rail as before.
        var rail = stock.ClampRail ?? plate.ClampRail;
        if (!SameInPlacementFrame(plate.ClampZones, stock.ClampZones, left, bottom))
        {
            plate.SetClampRail(null); // explicit zones from here on
            foreach (var zone in stock.ClampZones)
                plate.ClampZones.Add(new ClampZone(zone.Id, zone.Bounds.Translate(-left, -bottom)));
        }
        if (rail == null)
            return;

        // D8: the sheet's zones may be the rail's resolution for it minus clamps Auto Nest
        // removed as a last resort; derive which (if any) so the committed plate keeps
        // following the rail - and the removal - through later resizes (C13). This runs even
        // when the target already held exactly these zones, so the removal is never lost.
        var kept = new HashSet<string>(plate.ClampZones.Select(z => z.Id), StringComparer.Ordinal);
        var removedClamps = rail.ResolveFor(plate.Size, plate.Quadrant)
            .Select(z => z.Id)
            .Where(id => !kept.Contains(id))
            .ToList();
        var unchanged = ReferenceEquals(plate.ClampRail, rail) && plate.RemovedClamps.SetEquals(removedClamps);
        if (!unchanged && rail.Matches(plate, removedClamps))
            plate.SetClampRail(rail, removedClamps);
    }

    private static bool SameInPlacementFrame(
        IList<ClampZone> plateZones,
        IReadOnlyList<ClampZone> stockZones,
        double left,
        double bottom)
    {
        if (plateZones.Count != stockZones.Count)
            return false;
        for (var i = 0; i < plateZones.Count; i++)
        {
            // Same arithmetic NestPlateStock uses to carry plate zones into the placement frame.
            var placed = plateZones[i].Bounds.Translate(left, bottom);
            var solved = stockZones[i].Bounds;
            if (!string.Equals(plateZones[i].Id, stockZones[i].Id, StringComparison.Ordinal)
                || placed.X != solved.X || placed.Y != solved.Y
                || placed.Length != solved.Length || placed.Width != solved.Width)
                return false;
        }
        return true;
    }
}

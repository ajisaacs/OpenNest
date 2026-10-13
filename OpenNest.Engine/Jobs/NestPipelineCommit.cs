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
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(manager);
        token.ThrowIfCancellationRequested();
        if (!result.CanKeep || (!result.IsValid && !allowInvalid))
            throw new InvalidOperationException("The nesting result cannot be committed without a keepable layout and explicit consent to its violations.");
        if (NestPipelineDrawingFreshness.Changes(result.Job, result.DrawingsByPartId).Count > 0
            || NestPipelineDrawingFreshness.BoundChanges(result.Job, result.Raw,
                result.Plates, result.DrawingsByPartId, result.IsValid, token).Count > 0)
            throw new InvalidOperationException("A drawing or proposed part changed after the nesting proposal was validated; run Auto Nest again.");
        token.ThrowIfCancellationRequested();

        var applied = new List<Plate>();
        // Commit is synchronous on the caller's owning thread. Cancellation is checked
        // before mutation, not partway through attachment (which would leave half a job).
        manager.BeginBatch();
        try
        {
            foreach (var proposed in result.Plates)
            {
                if (proposed.Parts.Count == 0)
                    continue;
                var plate = manager.GetOrCreateEmpty();
                plate.Size = proposed.Stock.Size;
                plate.PartSpacing = proposed.Stock.PartSpacing;
                plate.EdgeSpacing = proposed.Stock.EdgeSpacing;
                plate.Quadrant = proposed.Stock.Quadrant;
                plate.Quantity = proposed.Quantity;
                SyncClampZones(plate, proposed.Stock);
                plate.Parts.AddRange(proposed.Parts);
                applied.Add(plate);
            }
        }
        finally
        {
            manager.EndBatch();
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
        if (SameInPlacementFrame(plate.ClampZones, stock.ClampZones, left, bottom))
            return;

        var rail = plate.ClampRail;
        plate.SetClampRail(null); // explicit zones from here on
        foreach (var zone in stock.ClampZones)
            plate.ClampZones.Add(new ClampZone(zone.Id, zone.Bounds.Translate(-left, -bottom)));
        if (rail == null)
            return;

        // D8: the sheet's zones may be the rail's resolution for it minus clamps Auto Nest
        // removed as a last resort; derive which (if any) so the committed plate keeps
        // following the rail - and the removal - through later resizes (C13).
        var kept = new HashSet<string>(plate.ClampZones.Select(z => z.Id), StringComparer.Ordinal);
        var removedClamps = rail.ResolveFor(plate.Size, plate.Quadrant)
            .Select(z => z.Id)
            .Where(id => !kept.Contains(id))
            .ToList();
        if (rail.Matches(plate, removedClamps))
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

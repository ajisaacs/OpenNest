using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.BestFit;

namespace OpenNest.Engine.Jobs.Adapters;

/// <summary>Explicit-ID input mapping and exact supported-geometry reconstruction. Never retains caller objects.</summary>
public static class DrawingJobMapper
{
    public static NestJobPart FromDrawing(string partId, Drawing drawing, int quantity)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var constraints = drawing.Constraints;
        return new NestJobPart(
            partId,
            PartGeometrySnapshot.FromProgram(drawing.Program),
            quantity,
            drawing.Priority,
            constraints == null
                ? RotationPolicy.Automatic
                : RotationPolicy.FromLegacy(
                    constraints.StepAngle,
                    constraints.StartAngle,
                    constraints.EndAngle,
                    constraints.Allow180Equivalent
                )
        )
        { AllowCommonLine = drawing.AllowCommonLine };
    }

    public static NestJobPart FromItem(string partId, NestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Drawing);
        return new NestJobPart(
            partId,
            PartGeometrySnapshot.FromProgram(item.Drawing.Program),
            item.Quantity,
            item.Priority,
            RotationPolicy.FromLegacy(
                item.StepAngle,
                item.RotationStart,
                item.RotationEnd,
                item.Drawing.Constraints?.Allow180Equivalent ?? false
            )
        )
        { AllowCommonLine = item.Drawing.AllowCommonLine };
    }

    /// <summary>Available stock is explicit; the legacy plate repeat count is not inventory.</summary>
    public static NestPlateStock FromPlate(string stockId, Plate plate, int? quantity)
    {
        ArgumentNullException.ThrowIfNull(plate);
        return new NestPlateStock(
            stockId,
            plate.Size,
            quantity,
            plate.PartSpacing,
            plate.EdgeSpacing,
            plate.Quadrant,
            cost: null,
            clampZones: new List<ClampZone>(plate.ClampZones),
            outline: plate.Outline
        )
        { ClampRail = plate.ClampRail };
    }

    public static Program ToProgram(PartGeometrySnapshot geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var program = new Program(geometry.Mode);
        foreach (var motion in geometry.Motions)
        {
            var code = motion.Type switch
            {
                CodeType.RapidMove => (Motion)new RapidMove(motion.X, motion.Y),
                CodeType.LinearMove => new LinearMove(motion.X, motion.Y) { Layer = motion.Layer },
                CodeType.ArcMove => new ArcMove(
                    motion.X,
                    motion.Y,
                    motion.CenterX,
                    motion.CenterY,
                    motion.Rotation
                )
                {
                    Layer = motion.Layer,
                },
                _ => throw new NotSupportedException("Unsupported snapshot motion."),
            };
            code.Suppressed = motion.Suppressed;
            program.Codes.Add(code);
        }
        return program;
    }

    /// <summary>A private drawing of the part for placement code. Its best-fit candidates are
    /// pooled by geometry content (<see cref="BestFitCache.ShareByGeometry"/>), so the same part
    /// geometry in a later job, engine instance or Auto Nest run does not search again.</summary>
    internal static Drawing CreateDrawing(NestJobPart part)
    {
        var drawing = new Drawing(part.Id, ToProgram(part.Geometry))
        {
            Priority = part.Priority,
            AllowCommonLine = part.AllowCommonLine,
        };
        drawing.Quantity.Required = part.Quantity;
        drawing.Constraints = new NestConstraints
        {
            StepAngle = LegacyStep(part.Rotation),
            StartAngle = part.Rotation.Start,
            EndAngle = part.Rotation.End,
            Allow180Equivalent = part.Rotation.Allow180Equivalent,
        };
        BestFitCache.ShareByGeometry(drawing, part.Geometry);
        return drawing;
    }

    // A fixed angle needs a nonzero legacy step so it is not misread as automatic.
    internal static double LegacyStep(RotationPolicy policy) =>
        policy.Kind == RotationPolicyKind.Fixed ? OpenNest.Math.Angle.TwoPI : policy.Step;

    internal static Plate CreatePlate(NestPlateStock stock)
    {
        var plate = new Plate(stock.Size)
        {
            Quantity = 1,
            PartSpacing = stock.PartSpacing,
            EdgeSpacing = stock.EdgeSpacing,
            Quadrant = stock.Quadrant,
        };
        // Every Plate stores zones in plate (sheet lower-left) coordinates; consumers map
        // them into the placement frame through ClampZoneFrame, exactly as the stock did.
        foreach (var zone in stock.SheetClampZones)
            plate.ClampZones.Add(new ClampZone(zone.Id, zone.Bounds));
        plate.Outline = stock.Outline;
        // The rail supplies the clamp clearance, so the plate is checked as the stock was packed.
        if (stock.ClampRail != null)
            FollowClampRail(plate, stock.ClampRail);
        return plate;
    }

    /// <summary>
    /// Ties the plate to <paramref name="rail"/> when the rail recreates its zones, directly or
    /// minus the clamps they lack (Auto Nest's last-resort removal, D8), so it keeps following
    /// the rail and the removal (C13). Other zones stay explicit: tying them to the rail would
    /// swap in other clamps.
    /// </summary>
    internal static void FollowClampRail(Plate plate, ClampRail rail)
    {
        var kept = new HashSet<string>(plate.ClampZones.Select(z => z.Id), StringComparer.Ordinal);
        var removedClamps = rail.ResolveFor(plate.Size, plate.Quadrant)
            .Select(z => z.Id)
            .Where(id => !kept.Contains(id))
            .ToList();
        var unchanged = ReferenceEquals(plate.ClampRail, rail) && plate.RemovedClamps.SetEquals(removedClamps);
        if (!unchanged && rail.Matches(plate, removedClamps))
            plate.SetClampRail(rail, removedClamps);
    }
}

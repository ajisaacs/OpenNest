using System;
using OpenNest.CNC;

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
                    constraints.EndAngle
                )
        );
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
            RotationPolicy.FromLegacy(item.StepAngle, item.RotationStart, item.RotationEnd)
        );
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
            plate.Quadrant
        );
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

    internal static Drawing CreateDrawing(NestJobPart part)
    {
        var drawing = new Drawing(part.Id, ToProgram(part.Geometry)) { Priority = part.Priority };
        drawing.Quantity.Required = part.Quantity;
        drawing.Constraints = new NestConstraints
        {
            StepAngle = LegacyStep(part.Rotation),
            StartAngle = part.Rotation.Start,
            EndAngle = part.Rotation.End,
        };
        return drawing;
    }

    // A fixed angle needs a nonzero legacy step so it is not misread as automatic.
    internal static double LegacyStep(RotationPolicy policy) =>
        policy.Kind == RotationPolicyKind.Fixed ? OpenNest.Math.Angle.TwoPI : policy.Step;

    internal static Plate CreatePlate(NestPlateStock stock) =>
        new(stock.Size)
        {
            Quantity = 1,
            PartSpacing = stock.PartSpacing,
            EdgeSpacing = stock.EdgeSpacing,
            Quadrant = stock.Quadrant,
        };
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>A detached mutable domain nest plus explicit requirement identity (never inferred from names).</summary>
public sealed class MaterializedNestResult
{
    internal MaterializedNestResult(Nest nest, Dictionary<string, Drawing> drawings)
    {
        Nest = nest;
        DrawingsByPartId = new ReadOnlyDictionary<string, Drawing>(drawings);
    }

    public Nest Nest { get; }
    public IReadOnlyDictionary<string, Drawing> DrawingsByPartId { get; }
}

/// <summary>Materializes a result from the same job. Geometry safety remains the solver's future validation boundary.</summary>
public static class NestResultMaterializer
{
    public static MaterializedNestResult Materialize(NestJob job, NestJobResult result)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(result);
        var nest = new Nest();
        var drawings = job.Parts.ToDictionary(
            p => p.Id,
            DrawingJobMapper.CreateDrawing,
            StringComparer.Ordinal
        );
        foreach (var drawing in drawings.Values)
            nest.Drawings.Add(drawing);
        foreach (var sheet in result.Plates)
        {
            var plate = DrawingJobMapper.CreatePlate(sheet.Stock);
            foreach (var pose in sheet.Placements)
            {
                if (!drawings.TryGetValue(pose.PartId, out var drawing))
                    throw new ArgumentException(
                        "Result contains a requirement not present in the job.",
                        nameof(result)
                    );
                // Do not use CreateAtOrigin: it normalizes bounds and would change the snapshot frame.
                var part = new Part(drawing);
                part.Rotate(pose.Rotation);
                part.Location = new Vector(pose.X, pose.Y);
                part.UpdateBounds();
                // Quantity=1 is set before the only attachment; Plate's event owns Nested accounting.
                plate.Parts.Add(part);
            }
            nest.Plates.Add(plate);
        }
        return new MaterializedNestResult(nest, drawings);
    }
}

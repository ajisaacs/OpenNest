using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenNest.CommonLines;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Adapters;

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
            AddCommonLines(plate, plate.Parts.ToList(), sheet.CommonLines);
            nest.Plates.Add(plate);
        }
        return new MaterializedNestResult(nest, drawings);
    }

    /// <summary>
    /// Adds a sheet's explicit common-line groups to <paramref name="plate"/>; members are the
    /// sheet's placements by position in <paramref name="parts"/>. A group naming a position
    /// outside the sheet, a part twice, a part of an earlier group, or fewer than two parts throws:
    /// such a result is malformed.
    /// </summary>
    internal static void AddCommonLines(Plate plate, IReadOnlyList<Part> parts,
        IReadOnlyList<NestJobCommonLine> groups)
    {
        if (groups.Count == 0)
            return;
        var taken = new HashSet<int>();
        foreach (var group in groups)
        {
            if (group.Placements.Count < 2 || group.Placements.Any(i => i < 0 || i >= parts.Count || !taken.Add(i)))
                throw new ArgumentException("A common-line group names a placement that is missing, repeated or in another group.");
            plate.CommonLines.Add(new CommonLineGroup(group.Placements.Select(i => parts[i]), group.Kerf));
        }
    }
}

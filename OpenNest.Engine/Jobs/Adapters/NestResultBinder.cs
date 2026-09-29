using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Adapters;

/// <summary>
/// Binds one result sheet's poses to the caller's own drawings. The pose semantics match
/// <see cref="NestResultMaterializer"/>: rotate about the snapshot origin, then translate.
/// Placements whose requirement ID is not in the map are skipped here; the pipeline reports
/// them as violations instead of dropping them silently.
/// </summary>
public static class NestResultBinder
{
    public static IReadOnlyList<Part> Bind(
        NestJobPlateResult sheet,
        IReadOnlyDictionary<string, Drawing> drawingsByPartId
    )
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(drawingsByPartId);
        var parts = new List<Part>(sheet.Placements.Count);
        foreach (var pose in sheet.Placements)
        {
            if (!drawingsByPartId.TryGetValue(pose.PartId, out var drawing))
                continue;
            var part = new Part(drawing);
            part.Rotate(pose.Rotation);
            part.Location = new Vector(pose.X, pose.Y);
            part.UpdateBounds();
            parts.Add(part);
        }
        return parts;
    }
}

using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Adapters;

/// <summary>
/// Binds one result sheet's poses to the caller's own drawings. The pose semantics match
/// <see cref="NestResultMaterializer"/>: rotate about the snapshot origin, then translate.
/// Every pose must be finite and refer to a mapped drawing. Malformed sheets are rejected
/// before binding any parts; the pipeline reports these violations without invoking binding.
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
        foreach (var pose in sheet.Placements)
        {
            if (pose.PartId == null || !drawingsByPartId.TryGetValue(pose.PartId, out var drawing) || drawing == null)
                throw new ArgumentException("Result contains a requirement not present in the drawing map.", nameof(sheet));
            if (!double.IsFinite(pose.X) || !double.IsFinite(pose.Y) || !double.IsFinite(pose.Rotation))
                throw new ArgumentException("Result contains a nonfinite placement pose.", nameof(sheet));
        }
        var parts = new List<Part>(sheet.Placements.Count);
        foreach (var pose in sheet.Placements)
        {
            var part = new Part(drawingsByPartId[pose.PartId]);
            part.Rotate(pose.Rotation);
            part.Location = new Vector(pose.X, pose.Y);
            part.UpdateBounds();
            parts.Add(part);
        }
        return parts;
    }
}

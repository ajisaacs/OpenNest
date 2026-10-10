using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Refuse a proposal when a caller drawing no longer matches its checked snapshot.</summary>
internal static class NestPipelineDrawingFreshness
{
    internal static IReadOnlyList<string> Changes(NestJob job,
        IReadOnlyDictionary<string, Drawing> drawings)
    {
        var changed = new List<string>();
        foreach (var requirement in job.Parts)
        {
            if (!drawings.TryGetValue(requirement.Id, out var drawing)
                || drawing?.Program == null)
            {
                changed.Add($"Drawing for '{requirement.Id}' changed after the nesting snapshot");
                continue;
            }
            try
            {
                var current = PartGeometrySnapshot.FromProgram(drawing.Program);
                if (Same(requirement.Geometry, current))
                    continue;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                or InvalidOperationException)
            {
                // A now-unsupported program is not the geometry that was checked.
            }
            changed.Add($"Drawing '{drawing.Name ?? requirement.Id}' changed after the nesting snapshot");
        }
        return changed;
    }

    /// <summary>Check the actual cloned programs, not only the drawing before/after
    /// binding. A transient edit during Clone may leave the drawing unchanged.</summary>
    internal static IReadOnlyList<string> BoundChanges(NestJob job, NestJobResult raw,
        IReadOnlyList<ProposedPlate> plates, IReadOnlyDictionary<string, Drawing> drawings)
    {
        var changed = new List<string>();
        var requirements = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        if (plates.Count != raw.Plates.Count)
            return new[] { "Bound plate count changed after validation" };
        for (var sheetIndex = 0; sheetIndex < plates.Count; sheetIndex++)
        {
            var poses = raw.Plates[sheetIndex].Placements;
            var bound = plates[sheetIndex].Parts;
            if (bound.Count != poses.Count)
                return new[] { "Bound part count changed after validation" };
            for (var index = 0; index < poses.Count; index++)
            {
                var pose = poses[index];
                var actual = bound[index];
                if (pose.PartId == null || !requirements.TryGetValue(pose.PartId, out var requirement)
                    || !drawings.TryGetValue(pose.PartId, out var drawing)
                    || !ReferenceEquals(actual.BaseDrawing, drawing))
                    return new[] { "A bound part no longer matches its requirement" };
                try
                {
                    var expected = new Part(DrawingJobMapper.CreateDrawing(requirement));
                    expected.Rotate(pose.Rotation);
                    expected.Location = new Vector(pose.X, pose.Y);
                    if (Same(PartGeometrySnapshot.FromProgram(expected.Program),
                            PartGeometrySnapshot.FromProgram(actual.Program))
                        && Bits(expected.Location.X) == Bits(actual.Location.X)
                        && Bits(expected.Location.Y) == Bits(actual.Location.Y)
                        && Bits(expected.Rotation) == Bits(actual.Rotation))
                        continue;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                    or InvalidOperationException)
                {
                    // A changed/unsupported program cannot represent the checked pose.
                }
                changed.Add($"Bound part for '{drawing.Name ?? requirement.Id}' changed after validation");
            }
        }
        return changed;
    }

    private static bool Same(PartGeometrySnapshot a, PartGeometrySnapshot b) =>
        a.Mode == b.Mode && a.Motions.Count == b.Motions.Count
        && a.Motions.Zip(b.Motions).All(pair =>
            pair.First.Type == pair.Second.Type
            && Bits(pair.First.X) == Bits(pair.Second.X)
            && Bits(pair.First.Y) == Bits(pair.Second.Y)
            && Bits(pair.First.CenterX) == Bits(pair.Second.CenterX)
            && Bits(pair.First.CenterY) == Bits(pair.Second.CenterY)
            && pair.First.Rotation == pair.Second.Rotation
            && pair.First.Layer == pair.Second.Layer
            && pair.First.Suppressed == pair.Second.Suppressed);

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}

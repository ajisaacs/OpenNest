using System;
using System.Collections.Generic;
using System.Linq;

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

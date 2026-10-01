#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// One allowed pose of a part type: its rotation, its polygonized outline at that rotation
/// (reference point = snapshot origin), and the outline's conservative bounds.
/// </summary>
internal sealed class Orientation
{
    public required int TypeIndex { get; init; }
    public required int Index { get; init; }
    public required double Rotation { get; init; }

    /// <summary>CCW outline whose every point lies within <see cref="Tolerance"/> of the true perimeter.</summary>
    public required PathD Outline { get; init; }

    /// <summary>Chord deviation used for arcs; footprints are grown by it to stay conservative.</summary>
    public required double Tolerance { get; init; }

    /// <summary>
    /// Outline bounds grown by the outline's actual flattening error, so they contain the true
    /// perimeter: by <see cref="Tolerance"/> when arcs were flattened, not at all for line-only
    /// outlines, whose vertices are exact. Footprints and NFPs still use <see cref="Tolerance"/>.
    /// </summary>
    public required double MinX { get; init; }
    public required double MinY { get; init; }
    public required double MaxX { get; init; }
    public required double MaxY { get; init; }

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

internal sealed class PartType
{
    public required int Index { get; init; }
    public required NestJobPart Part { get; init; }
    public required double Area { get; init; }
    public required IReadOnlyList<Orientation> Orientations { get; init; }

    /// <summary>Analytic perimeter the orientations were flattened from; null when unreadable.</summary>
    public Shape? Perimeter { get; init; }
}

/// <summary>
/// Converts job snapshots into the polygon world the packer works in. Parts whose geometry
/// cannot be read are kept with no orientations, so they surface as unplaced instead of
/// failing the whole job.
/// </summary>
internal static class PartCatalog
{
    /// <summary>Finest chord deviation of the working outline from true arcs, in job units.</summary>
    public const double ChordTolerance = 0.002;

    /// <summary>Outline vertex count above which arcs are polygonized more coarsely (NFP cost is ~n*m).</summary>
    private const int TargetVertices = 64;

    /// <summary>Hard cap on distinct orientations evaluated per part type.</summary>
    private const int MaxOrientations = 8;

    public static IReadOnlyList<PartType> Build(NestJob job)
    {
        // Fewer orientations per type for jobs with many distinct parts; every (type, rotation)
        // pair costs a feasible-region update per placement.
        var perType = System.Math.Clamp(48 / System.Math.Max(1, job.Parts.Count), 2, MaxOrientations);
        var types = new List<PartType>(job.Parts.Count);
        for (var index = 0; index < job.Parts.Count; index++)
        {
            var part = job.Parts[index];
            Shape? perimeter;
            try
            {
                perimeter = ReadPerimeter(part.Geometry);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                perimeter = null;
            }

            if (perimeter == null)
            {
                types.Add(new PartType { Index = index, Part = part, Area = 0, Orientations = [] });
                continue;
            }

            var angles = RotationCandidates.DistinctOutlines(perimeter,
                CandidateAngles(part.Rotation, perimeter, perType));
            var tolerance = ChooseTolerance(perimeter);
            var orientations = new List<Orientation>();
            foreach (var angle in angles)
            {
                var outline = Polygonize(perimeter, angle, tolerance);
                if (outline.Count < 3)
                    continue;
                orientations.Add(MakeOrientation(index, orientations.Count, angle, outline, tolerance, perimeter));
            }

            var area = orientations.Count == 0 ? 0 : System.Math.Abs(Clipper.Area(orientations[0].Outline));
            types.Add(new PartType
            {
                Index = index,
                Part = part,
                Area = area,
                Orientations = orientations,
                Perimeter = perimeter,
            });
        }
        return types;
    }

    /// <summary>
    /// An extra pose of <paramref name="type"/> at <paramref name="angle"/>, flattened like the
    /// catalog's own. <paramref name="index"/> must be unique within the type, because NFP
    /// caches key on it.
    /// </summary>
    internal static Orientation? CreateOrientation(PartType type, int index, double angle)
    {
        if (type.Perimeter == null || type.Orientations.Count == 0)
            return null;
        var tolerance = type.Orientations[0].Tolerance;
        var outline = Polygonize(type.Perimeter, angle, tolerance);
        return outline.Count < 3 ? null : MakeOrientation(type.Index, index, angle, outline, tolerance, type.Perimeter);
    }

    private static Shape? ReadPerimeter(PartGeometrySnapshot geometry) =>
        JobPartGeometry.TryRead(geometry)?.Perimeter;

    /// <summary>
    /// Coarsens arc polygonization (up to 0.1% of the part size) until the outline is small
    /// enough for cheap Minkowski sums. Lines are always exact, so only arc-heavy parts pay.
    /// </summary>
    private static double ChooseTolerance(Shape perimeter)
    {
        var box = perimeter.BoundingBox;
        var cap = System.Math.Max(ChordTolerance, 0.001 * System.Math.Max(box.Width, box.Length));
        var tolerance = ChordTolerance;
        while (tolerance * 2 <= cap && perimeter.ToPolygonWithTolerance(tolerance).Vertices.Count > TargetVertices)
            tolerance *= 2;
        return tolerance;
    }

    private static PathD Polygonize(Shape perimeter, double angle, double tolerance)
    {
        var shape = (Shape)perimeter.Clone();
        if (angle != 0)
            shape.Rotate(angle);
        var polygon = shape.ToPolygonWithTolerance(tolerance);
        var path = new PathD(polygon.Vertices.Count);
        foreach (var v in polygon.Vertices)
        {
            if (path.Count > 0 && System.Math.Abs(path[^1].x - v.X) < 1e-9 && System.Math.Abs(path[^1].y - v.Y) < 1e-9)
                continue;
            path.Add(new PointD(v.X, v.Y));
        }
        if (path.Count > 1 && System.Math.Abs(path[0].x - path[^1].x) < 1e-9 && System.Math.Abs(path[0].y - path[^1].y) < 1e-9)
            path.RemoveAt(path.Count - 1);
        if (!Clipper.IsPositive(path))
            path.Reverse();
        return path;
    }

    private static Orientation MakeOrientation(int typeIndex, int index, double angle, PathD outline,
        double tolerance, Shape perimeter)
    {
        var bounds = Clipper.GetBounds(outline);
        // Only flattened arcs deviate from the true perimeter; line vertices are exact, so a
        // line-only part keeps its nominal material bounds and can fill its work area exactly.
        var padding = perimeter.Entities.All(e => e.Type == EntityType.Line) ? 0 : tolerance;
        return new Orientation
        {
            TypeIndex = typeIndex,
            Index = index,
            Rotation = angle,
            Outline = outline,
            Tolerance = tolerance,
            MinX = bounds.left - padding,
            MinY = bounds.top - padding, // Clipper RectD: top is the minimum Y.
            MaxX = bounds.right + padding,
            MaxY = bounds.bottom + padding,
        };
    }

    internal static List<double> CandidateAngles(RotationPolicy policy, Shape perimeter, int limit) =>
        RotationCandidates.ForShape(policy, perimeter, limit).ToList();
}

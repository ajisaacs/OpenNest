using Clipper2Lib;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Opus55;

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

    /// <summary>Outline bounds grown by the tolerance, so they contain the true perimeter.</summary>
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

    private const double TwoPi = System.Math.PI * 2;

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

            var angles = CandidateAngles(part.Rotation, perimeter, perType);
            var tolerance = ChooseTolerance(perimeter);
            var orientations = new List<Orientation>();
            var signatures = new List<string>();
            foreach (var angle in angles)
            {
                var outline = Polygonize(perimeter, angle, tolerance);
                if (outline.Count < 3)
                    continue;
                // Point-symmetric parts (rectangles, discs...) look identical at several angles;
                // evaluating duplicates only costs time.
                var signature = Signature(outline);
                if (signatures.Contains(signature))
                    continue;
                signatures.Add(signature);
                orientations.Add(MakeOrientation(index, orientations.Count, angle, outline, tolerance));
            }

            var area = orientations.Count == 0 ? 0 : System.Math.Abs(Clipper.Area(orientations[0].Outline));
            types.Add(new PartType { Index = index, Part = part, Area = area, Orientations = orientations });
        }
        return types;
    }

    private static Shape? ReadPerimeter(PartGeometrySnapshot geometry)
    {
        var entities = ConvertProgram
            .ToGeometry(DrawingJobMapper.ToProgram(geometry))
            .Where(e => !ReferenceEquals(e.Layer, SpecialLayers.Rapid))
            .ToList();
        if (entities.Count == 0)
            return null;
        var profile = new ShapeProfile(entities);
        return profile.Perimeter is { } perimeter && perimeter.Area() > 1e-9 ? perimeter : null;
    }

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

    private static Orientation MakeOrientation(int typeIndex, int index, double angle, PathD outline, double tolerance)
    {
        var bounds = Clipper.GetBounds(outline);
        return new Orientation
        {
            TypeIndex = typeIndex,
            Index = index,
            Rotation = angle,
            Outline = outline,
            Tolerance = tolerance,
            MinX = bounds.left - tolerance,
            MinY = bounds.top - tolerance, // Clipper RectD: top is the minimum Y.
            MaxX = bounds.right + tolerance,
            MaxY = bounds.bottom + tolerance,
        };
    }

    private static string Signature(PathD outline)
    {
        var bounds = Clipper.GetBounds(outline);
        var points = outline
            .Select(p => (System.Math.Round(p.x - bounds.left, 5), System.Math.Round(p.y - bounds.top, 5)))
            .OrderBy(p => p.Item1)
            .ThenBy(p => p.Item2)
            .Select(p => $"{p.Item1:R},{p.Item2:R}");
        return string.Join(";", points);
    }

    /// <summary>
    /// Rotations to try, all satisfying the part's policy. Automatic parts get the four
    /// right angles plus the two orientations that align their minimum-area bounding
    /// rectangle with the sheet axes.
    /// </summary>
    internal static List<double> CandidateAngles(RotationPolicy policy, Shape perimeter, int limit)
    {
        var raw = new List<double>();
        switch (policy.Kind)
        {
            case RotationPolicyKind.Fixed:
                raw.Add(policy.Start);
                if (policy.Allow180Equivalent)
                    raw.Add(policy.Start + System.Math.PI);
                break;

            case RotationPolicyKind.BoundedSweep:
                {
                    var steps = (int)System.Math.Floor((policy.End - policy.Start) / policy.Step + 1e-9);
                    var samples = System.Math.Min(steps + 1, policy.Allow180Equivalent ? System.Math.Max(1, limit / 2) : limit);
                    for (var i = 0; i < samples; i++)
                    {
                        var k = samples == 1 ? 0 : (int)System.Math.Round(i * (double)steps / (samples - 1));
                        raw.Add(policy.Start + k * policy.Step);
                        if (policy.Allow180Equivalent)
                            raw.Add(policy.Start + k * policy.Step + System.Math.PI);
                    }
                    break;
                }

            default:
                {
                    var rightAngles = new[] { 0, System.Math.PI / 2, System.Math.PI, System.Math.PI * 1.5 };
                    var aligned = AlignedAngle(perimeter);
                    raw.Add(0);
                    raw.Add(System.Math.PI / 2);
                    if (aligned is double a)
                    {
                        raw.Add(Normalize(a));
                        raw.Add(Normalize(a + System.Math.PI / 2));
                    }
                    raw.Add(System.Math.PI);
                    raw.Add(System.Math.PI * 1.5);
                    if (aligned is double b)
                    {
                        raw.Add(Normalize(b + System.Math.PI));
                        raw.Add(Normalize(b + System.Math.PI * 1.5));
                    }
                    break;
                }
        }

        var result = new List<double>();
        foreach (var angle in raw)
        {
            if (!policy.Allows(angle))
                continue;
            if (result.Any(existing => SameTurn(existing, angle)))
                continue;
            result.Add(angle);
            if (result.Count >= limit)
                break;
        }
        return result;
    }

    private static double? AlignedAngle(Shape perimeter)
    {
        var polygon = perimeter.ToPolygonWithTolerance(ChordTolerance * 5);
        if (polygon.Vertices.Count < 3)
            return null;
        var mbr = RotatingCalipers.MinimumBoundingRectangle(polygon.Vertices);
        var angle = Normalize(-mbr.Angle) % (System.Math.PI / 2);
        // Already axis-aligned (within ~0.05°): the right angles cover it.
        if (angle < 1e-3 || System.Math.PI / 2 - angle < 1e-3)
            return null;
        return angle;
    }

    private static double Normalize(double angle)
    {
        var value = angle % TwoPi;
        return value < 0 ? value + TwoPi : value;
    }

    private static bool SameTurn(double a, double b)
    {
        var delta = System.Math.Abs(Normalize(a - b));
        return delta < 1e-9 || TwoPi - delta < 1e-9;
    }
}

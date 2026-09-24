using Clipper2Lib;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using M = System.Math;

namespace OpenNest.Engine.Astra;

internal sealed record PreparedPart(NestJobPart Requirement, double Area, ShapeVariant[] Variants);

internal sealed class ShapeVariant
{
    internal required int Id { get; init; }
    internal required int Part { get; init; }
    internal required double Angle { get; init; }
    internal required double OriginX { get; init; }
    internal required double OriginY { get; init; }
    internal required double Width { get; init; }
    internal required double Height { get; init; }
    internal required bool Curved { get; init; }
    internal required PathsD Material { get; init; }
    internal required Polygon Outline { get; init; }
    internal required Polygon ContactOutline { get; init; }
    internal required double ContactError { get; init; }
    internal required Polygon Hull { get; init; }
    internal required bool Convex { get; init; }
    internal required ShapeProfile ValidationProfile { get; init; }
    internal bool BoxLike => Material.Count == 1 && GridAligned(OriginX) && GridAligned(OriginY) &&
        GridAligned(Width) && GridAligned(Height) &&
        M.Abs(Outline.Area() - Width * Height) < 1e-8 * M.Max(1, Width * Height);
    private static bool GridAligned(double x) => M.Abs(x - M.Round(x * 10000) / 10000) < 1e-9;
    private readonly Dictionary<double, PathsD> validationRegions = new();

    internal PathsD ValidationRegion(double spacing)
    {
        if (validationRegions.TryGetValue(spacing, out var cached)) return cached;
        // Match the external validator's sequence: flatten/round in the original
        // rotated snapshot frame, then translate. Rounding after normalization is
        // not equivalent at a zero-clearance contact.
        var region = ClipperBridge.OffsetForValidation(ValidationProfile, spacing, 0.001);
        var paths = new PathsD(region.Outers.Select(p => ClipperBridge.ToPath(p, true)));
        paths.AddRange(region.Holes.Select(p => ClipperBridge.ToPath(p, false)));
        return validationRegions[spacing] = GeometryPrecision.Translate(paths, -OriginX, -OriginY);
    }
    private readonly Dictionary<double, PathsD> halos = new();

    internal PathsD Halo(double spacing)
    {
        if (halos.TryGetValue(spacing, out var cached)) return cached;
        // Raw outlines already circumscribe curves; the extra clearance covers independent
        // flattenings after pose materialization and the validator's four-decimal grid.
        var delta = spacing + (Curved ? 0.0021 : spacing > 0 ? 0.00015 : 0);
        return halos[spacing] = delta == 0 ? Material : Clipper.InflatePaths(Material, delta,
            JoinType.Round, EndType.Polygon, 2, GeometryPrecision.Digits, 0.00001);
    }
}

internal static class GeometryPrecision
{
    internal const int Digits = 6;
    internal const double Scale = 1_000_000;
    internal const double Epsilon = 0.000002;

    internal static PathsD Translate(PathsD paths, double x, double y) =>
        new(paths.Select(path => new PathD(path.Select(p => new PointD(p.x + x, p.y + y)))));

    internal static PathsD FromPolygons(IEnumerable<Polygon> polygons, bool positive) =>
        new(polygons.Select(p => ClipperBridge.ToPath(p, positive)));
}

internal static class GeometryPreparation
{
    internal static PreparedPart[] Prepare(NestJob job, CancellationToken token)
    {
        var id = 0;
        return job.Parts.Select((part, index) =>
        {
            token.ThrowIfCancellationRequested();
            var entities = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(part.Geometry))
                .Where(e => !ReferenceEquals(e.Layer, SpecialLayers.Rapid)).ToList();
            // Input validation has established that open marks lie inside material. They
            // must not be interpreted as holes by ShapeProfile.
            var closed = ShapeBuilder.GetShapes(entities).Where(s => s.IsClosed())
                .SelectMany(s => s.Entities).ToList();
            var baseProfile = new ShapeProfile(closed);
            var area = baseProfile.Perimeter.Area() - baseProfile.Cutouts.Sum(h => h.Area());
            var variants = new List<ShapeVariant>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var angle in Angles(part.Rotation, baseProfile))
            {
                token.ThrowIfCancellationRequested();
                var rotated = closed.Select(e => { var copy = e.Clone(); copy.Rotate(angle); return copy; }).ToList();
                var x = rotated.Min(e => e.Left);
                var y = rotated.Min(e => e.Bottom);
                var w = rotated.Max(e => e.Right) - x;
                var h = rotated.Max(e => e.Top) - y;
                if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0)
                    throw new ArgumentException($"Unusable rotated bounds: {part.Id}.");
                var validationProfile = new ShapeProfile(rotated.Select(e => e.Clone()).ToList());
                foreach (var e in rotated) e.Offset(-x, -y);
                var profile = new ShapeProfile(rotated);
                var material = ClipperBridge.ToRegion(profile, 0.001, circumscribe: true);
                // Circular/symmetric parts should not multiply identical NFP work. Compare
                // normalized closed contours, including holes, independent of start vertex.
                var key = string.Join("|", material.Select(Canonical).Order(StringComparer.Ordinal));
                if (!keys.Add(key)) continue;
                var outline = ClipperBridge.Flatten(profile.Perimeter, 0.001, circumscribe: true);
                var hull = ConvexHull.Compute(outline.Vertices);
                var convex = M.Abs(hull.Area() - outline.Area()) < 1e-7 * M.Max(1, hull.Area());
                // Concave Minkowski sums have quadratic input size. Only the contact
                // proposal outline is simplified; fine material remains the safety gate.
                // Pad the resulting NFP by both approximation error bounds.
                var contactError = !convex && outline.Vertices.Count > 64 ? M.Max(0.002, M.Min(w, h) * 0.002) : 0;
                var contactOutline = contactError == 0 ? outline :
                    ClipperBridge.Flatten(profile.Perimeter, contactError, circumscribe: true);
                variants.Add(new ShapeVariant { Id = id++, Part = index, Angle = angle,
                    OriginX = x, OriginY = y, Width = w, Height = h,
                    Curved = rotated.Any(e => e is Arc or Circle), Material = material,
                    Outline = outline, ContactOutline = contactOutline, ContactError = contactError,
                    Hull = hull, Convex = convex, ValidationProfile = validationProfile });
            }
            var ordered = variants.OrderBy(v => M.Round(v.Width * v.Height, 7)).ToArray();
            if (part.Rotation.Kind == RotationPolicyKind.Automatic && ordered.Length > 8)
            {
                var minimum = ordered[0].Width * ordered[0].Height;
                var all = ordered;
                var shortlist = ordered.Where(v => v.Width * v.Height <= minimum * 1.08 + 1e-7).Take(16).ToList();
                // A diagonal may be the only orientation fitting a narrow stock. Never
                // discard every fitting orientation merely because its envelope is larger.
                foreach (var stock in job.Plates)
                {
                    bool Fits(ShapeVariant v) => v.Width <= stock.Size.Length - stock.EdgeSpacing.Left - stock.EdgeSpacing.Right + 1e-9 &&
                        v.Height <= stock.Size.Width - stock.EdgeSpacing.Top - stock.EdgeSpacing.Bottom + 1e-9;
                    if (!shortlist.Any(Fits)) shortlist.AddRange(all.Where(Fits).Take(4));
                }
                ordered = shortlist.DistinctBy(v => v.Id).ToArray();
            }
            return new PreparedPart(part, area, ordered);
        }).ToArray();
    }

    private static string Canonical(PathD path)
    {
        if (path.Count == 0) return "";
        var points = path.Select(p => ((long)M.Round(p.x * 100000), (long)M.Round(p.y * 100000))).ToArray();
        var first = 0;
        for (var i = 1; i < points.Length; i++) if (points[i].CompareTo(points[first]) < 0) first = i;
        return string.Join(";", Enumerable.Range(0, points.Length).Select(i => points[(i + first) % points.Length]));
    }

    private static IEnumerable<double> Angles(RotationPolicy policy, ShapeProfile profile)
    {
        var values = new List<double>();
        if (policy.Kind == RotationPolicyKind.Automatic)
        {
            // All half-turns matter for asymmetric parts, unlike envelope-only packing.
            for (var i = 0; i < 24; i++) values.Add(i * M.PI / 12);
            foreach (var line in profile.Perimeter.Entities.OfType<Line>().OrderByDescending(l => l.Length).Take(8))
            {
                var angle = -M.Atan2(line.EndPoint.Y - line.StartPoint.Y, line.EndPoint.X - line.StartPoint.X);
                for (var i = 0; i < 4; i++) values.Add(angle + i * M.PI / 2);
            }
        }
        else
        {
            var last = policy.Kind == RotationPolicyKind.Fixed ? 0 : M.Floor((policy.End - policy.Start) / policy.Step);
            if (!double.IsFinite(last)) last = 720;
            var samples = (int)M.Min(720, last);
            for (var i = 0; i <= samples; i++)
            {
                var k = samples == 0 ? 0 : M.Floor(last * ((double)i / samples));
                var angle = policy.Start + k * policy.Step;
                if (!double.IsFinite(angle) || !policy.Allows(angle)) continue;
                values.Add(angle);
                if (policy.Allow180Equivalent) values.Add(angle + M.PI);
            }
        }
        var seen = new HashSet<long>();
        foreach (var value in values)
        {
            var angle = value % (2 * M.PI);
            if (angle < 0) angle += 2 * M.PI;
            if (policy.Allows(angle) && seen.Add((long)M.Round(angle * 1e9))) yield return angle;
        }
    }
}

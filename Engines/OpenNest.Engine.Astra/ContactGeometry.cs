using Clipper2Lib;
using OpenNest.Geometry;
using M = System.Math;

namespace OpenNest.Engine.Astra;

/// <summary>Per-solve configuration-space cache, never a shared mutable geometry cache.</summary>
internal sealed class ContactGeometry
{
    private readonly Dictionary<(int, int, double), PathsD> cache = new();

    internal PathsD Forbidden(ShapeVariant stationary, ShapeVariant moving, double spacing, CancellationToken token)
    {
        var key = (stationary.Id, moving.Id, spacing);
        if (cache.TryGetValue(key, out var value)) return value;
        token.ThrowIfCancellationRequested();
        PathsD paths;
        if (stationary.BoxLike && moving.BoxLike)
        {
            // Exact axis-aligned rectangle contacts need four configuration-space
            // vertices, not hundreds of round-offset samples. The square corner is
            // conservative for diagonal clearance and leaves row/column fits exact.
            var gap = spacing;
            paths = new PathsD { new PathD {
                new(-moving.Width - gap, -moving.Height - gap),
                new(stationary.Width + gap, -moving.Height - gap),
                new(stationary.Width + gap, stationary.Height + gap),
                new(-moving.Width - gap, stationary.Height + gap) } };
            if (cache.Count >= 8192) cache.Clear();
            return cache[key] = paths;
        }
        if (stationary.Convex && moving.Convex)
        {
            var nfp = NoFitPolygon.ComputeConvex(stationary.Hull, moving.Hull);
            paths = new PathsD { ClipperBridge.ToPath(nfp, positive: true) };
        }
        else
        {
            // Minkowski edge quads may enclose spurious interior voids. Filling all
            // positive outer paths is conservative for solid perimeter nesting; real
            // part holes are searched separately and checked against material regions.
            var a = ToInteger(stationary.ContactOutline, false);
            var b = ToInteger(moving.ContactOutline, true);
            var sum = Clipper.MinkowskiSum(b, a, true);
            paths = new PathsD(sum.Where(Clipper.IsPositive).Select(path => new PathD(
                path.Select(p => new PointD(p.X / GeometryPrecision.Scale, p.Y / GeometryPrecision.Scale)))));
        }
        token.ThrowIfCancellationRequested();
        var delta = spacing + stationary.ContactError + moving.ContactError
            + (stationary.Curved || moving.Curved ? 0.003 : spacing > 0 ? 0.0003 : 0);
        if (delta > 0) paths = Clipper.InflatePaths(paths, delta, JoinType.Round,
            EndType.Polygon, 2, GeometryPrecision.Digits, 0.00001);
        // Bound cache residency for jobs with many distinct rotation pairs.
        if (cache.Count >= 8192) cache.Clear();
        return cache[key] = paths;
    }

    private static Path64 ToInteger(Polygon polygon, bool reflect)
    {
        var scale = reflect ? -GeometryPrecision.Scale : GeometryPrecision.Scale;
        var path = ClipperBridge.ToPath(polygon, positive: true);
        return new Path64(path.Select(p => new Point64((long)M.Round(p.x * scale), (long)M.Round(p.y * scale))));
    }
}

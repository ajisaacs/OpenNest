using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using M = System.Math;

namespace OpenNest.Engine.Astra;

internal sealed record PackedShape(ShapeVariant Variant, double X, double Y);
internal sealed record SheetTrial(int StockIndex, int[] Counts, List<PackedShape> Shapes, double Area, double Span);

/// <summary>Searches vertices of the available translation region and exact-fit contacts.</summary>
internal sealed class ContactPlacer(PreparedPart[] parts, ContactGeometry geometry, CancellationToken token)
{
    private readonly Dictionary<(int, int, double, double, double, double, double), bool> validationCache = new();
    private double validationOriginX;
    private double validationOriginY;

    internal SheetTrial Pack(int stockIndex, NestPlateStock stock, int[] committed, int[] flexibility, int mode)
    {
        validationOriginX = (stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length) + stock.EdgeSpacing.Left;
        validationOriginY = (stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width) + stock.EdgeSpacing.Bottom;
        var width = stock.Size.Length - stock.EdgeSpacing.Left - stock.EdgeSpacing.Right;
        var height = stock.Size.Width - stock.EdgeSpacing.Bottom - stock.EdgeSpacing.Top;
        var counts = (int[])committed.Clone();
        var placed = new List<PackedShape>();
        var spaces = new Dictionary<int, SearchSpace>();
        var order = Enumerable.Range(0, parts.Length)
            .OrderByDescending(i => parts[i].Requirement.Priority)
            .ThenBy(i => flexibility[i])
            .ThenByDescending(i => parts[i].Variants.Select(v => v.Width * v.Height).DefaultIfEmpty(0).Min())
            .ThenBy(i => i).ToArray();
        double area = 0, right = 0, top = 0;
        foreach (var p in order)
        {
            while (counts[p] < parts[p].Requirement.Quantity)
            {
                token.ThrowIfCancellationRequested();
                PackedShape? best = null;
                (double, double, double, double) bestScore = (double.MaxValue, 0, 0, 0);
                foreach (var v in parts[p].Variants)
                {
                    token.ThrowIfCancellationRequested();
                    if (v.Width > width + 1e-9 || v.Height > height + 1e-9) continue;
                    var pose = Find(v, placed, width, height, stock.PartSpacing, mode, right, top, spaces);
                    if (pose == null) continue;
                    var score = Score(pose, width, height, mode, right, top);
                    if (score.CompareTo(bestScore) < 0) { best = pose; bestScore = score; }
                }
                if (best == null) break;
                placed.Add(best);
                counts[p]++;
                area += parts[p].Area;
                right = M.Max(right, best.X + best.Variant.Width);
                top = M.Max(top, best.Y + best.Variant.Height);
            }
        }
        return new(stockIndex, counts, placed, area, right * top);
    }

    private PackedShape? Find(ShapeVariant moving, List<PackedShape> placed, double width, double height,
        double spacing, int mode, double right, double top, Dictionary<int, SearchSpace> spaces)
    {
        var maxX = M.Max(0, width - moving.Width);
        var maxY = M.Max(0, height - moving.Height);
        if (!spaces.TryGetValue(moving.Id, out var space))
        {
            space = new SearchSpace();
            space.Anchors.AddRange(new PointD[] { new(0, 0), new(maxX, 0), new(0, maxY), new(maxX, maxY) });
            space.Free.Add(new PathD { new(0, 0), new(maxX, 0), new(maxX, maxY), new(0, maxY) });
            spaces.Add(moving.Id, space);
        }
        var points = space.Anchors;
        var forbidden = new PathsD();
        var blockers = space.Blockers;
        foreach (var other in placed.Skip(space.Processed))
        {
            token.ThrowIfCancellationRequested();
            var paths = GeometryPrecision.Translate(geometry.Forbidden(other.Variant, moving, spacing, token), other.X, other.Y);
            foreach (var path in paths)
            {
                forbidden.Add(path);
                if (!other.Variant.Material.Any(p => !Clipper.IsPositive(p)))
                    blockers.Add((path, path.Min(p => p.x), path.Min(p => p.y), path.Max(p => p.x), path.Max(p => p.y)));
                // Clipping loses zero-area feasible regions. Retain their NFP vertices
                // and intersections with plate boundaries explicitly for exact fits.
                for (var i = 0; (maxX < 1e-8 || maxY < 1e-8) && i < path.Count; i++)
                {
                    var a = path[i]; var b = path[(i + 1) % path.Count];
                    Add(a.x, a.y);
                    CrossX(0); CrossX(maxX); CrossY(0); CrossY(maxY);
                    void CrossX(double x)
                    {
                        if (M.Abs(b.x - a.x) < 1e-12) return;
                        var t = (x - a.x) / (b.x - a.x);
                        if (t >= 0 && t <= 1) Add(x, a.y + t * (b.y - a.y));
                    }
                    void CrossY(double y)
                    {
                        if (M.Abs(b.y - a.y) < 1e-12) return;
                        var t = (y - a.y) / (b.y - a.y);
                        if (t >= 0 && t <= 1) Add(a.x + t * (b.x - a.x), y);
                    }
                }
            }
            // Axis contacts also cover exact spacing when the padded NFP cannot fit.
            foreach (var x in new[] { other.X, other.X + other.Variant.Width + spacing,
                other.X - moving.Width - spacing })
            foreach (var y in new[] { 0, other.Y, other.Y + other.Variant.Height + spacing,
                other.Y - moving.Height - spacing }) Add(x, y);

            // The solid-outline NFP deliberately fills holes. Search each real hole
            // separately, then validate against material, not the outer envelope.
            foreach (var hole in other.Variant.Material.Where(p => !Clipper.IsPositive(p)))
            {
                var l = hole.Min(p => p.x) + other.X + spacing + 0.0004;
                var b = hole.Min(p => p.y) + other.Y + spacing + 0.0004;
                var r = hole.Max(p => p.x) + other.X - spacing - moving.Width - 0.0004;
                var t = hole.Max(p => p.y) + other.Y - spacing - moving.Height - 0.0004;
                if (r < l || t < b) continue;
                Add(l, b); Add(r, b); Add(l, t); Add(r, t); Add((l + r) / 2, (b + t) / 2);
                // Box corners miss the useful interior of circular and rounded holes.
                // Interior samples also cover fits that require an off-center placement.
                foreach (var fx in new[] { 0.25, 0.5, 0.75 })
                foreach (var fy in new[] { 0.25, 0.5, 0.75 }) Add(l + fx * (r - l), b + fy * (t - b));
            }
        }
        if (placed.Count > 0 && maxX > 1e-8 && maxY > 1e-8)
        {
            space.Free = Clipper.Difference(space.Free, forbidden, FillRule.NonZero, GeometryPrecision.Digits);
        }
        space.Processed = placed.Count;
        points = new List<PointD>(space.Anchors);
        foreach (var path in space.Free) foreach (var p in path) Add(p.x, p.y);
        var seen = new HashSet<(long, long)>();
        foreach (var pose in points.Select(p => new PackedShape(moving, p.x, p.y))
            .OrderBy(p => Score(p, width, height, mode, right, top)))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(((long)M.Round(pose.X * 1e6), (long)M.Round(pose.Y * 1e6)))) continue;
            if (blockers.Any(b => pose.X > b.L && pose.X < b.R && pose.Y > b.B && pose.Y < b.T &&
                StrictlyInside(b.Path, pose.X, pose.Y))) continue;
            if (Valid(pose, placed, spacing)) return pose;
            if (spacing > 0) continue;
            // Exact contacts can be invalid only after the host's four-decimal
            // polygon rounding. Try nearby outward contacts without changing angle.
            foreach (var (dx, dy) in new (double, double)[] {
                (0.0003, 0), (0, 0.0003), (0.0003, 0.0003), (-0.0003, 0),
                (0, -0.0003), (-0.0003, 0.0003), (0.0003, -0.0003), (-0.0003, -0.0003) })
            {
                var nudged = pose with { X = pose.X + dx, Y = pose.Y + dy };
                if (nudged.X < 0 || nudged.Y < 0 || nudged.X > maxX || nudged.Y > maxY) continue;
                if (Valid(nudged, placed, spacing)) return nudged;
            }
        }
        return null;

        void Add(double x, double y)
        {
            if (x < -1e-7 || y < -1e-7 || x > maxX + 1e-7 || y > maxY + 1e-7) return;
            points.Add(new(M.Clamp(x, 0, maxX), M.Clamp(y, 0, maxY)));
        }
    }

    private static (double, double, double, double) Score(PackedShape pose, double width, double height,
        int mode, double right, double top)
    {
        var r = pose.X + pose.Variant.Width;
        var t = pose.Y + pose.Variant.Height;
        // Two directional searches use the same configuration-space algorithm. The
        // third objective minimizes the growing used rectangle rather than a strip.
        return mode switch
        {
            1 => (r + 0.01 * t * width / height, pose.Y, pose.X, pose.Variant.Width * pose.Variant.Height),
            2 => (M.Max(right, r) * M.Max(top, t), t, r, pose.Variant.Width * pose.Variant.Height),
            _ => (t + 0.01 * r * height / width, pose.X, pose.Y, pose.Variant.Width * pose.Variant.Height)
        };
    }

    private sealed class SearchSpace
    {
        internal int Processed;
        internal PathsD Free = new();
        internal List<PointD> Anchors = new();
        internal List<(PathD Path, double L, double B, double R, double T)> Blockers = new();
    }

    private static bool StrictlyInside(PathD path, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i]; var b = path[(i + 1) % path.Count];
            var cross = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
            if (M.Abs(cross) <= 2e-6 * M.Max(1, M.Abs(b.x - a.x) + M.Abs(b.y - a.y)) &&
                x >= M.Min(a.x, b.x) - 1e-6 && x <= M.Max(a.x, b.x) + 1e-6 &&
                y >= M.Min(a.y, b.y) - 1e-6 && y <= M.Max(a.y, b.y) + 1e-6) return false;
            if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    private bool Valid(PackedShape candidate, List<PackedShape> placed, double spacing)
    {
        PathsD? material = null;
        PathsD? validationMaterial = null;
        foreach (var other in placed)
        {
            var gap = spacing + (candidate.Variant.Curved || other.Variant.Curved ? 0.003 : 0.0001);
            if (candidate.X >= other.X + other.Variant.Width + gap ||
                other.X >= candidate.X + candidate.Variant.Width + gap ||
                candidate.Y >= other.Y + other.Variant.Height + gap ||
                other.Y >= candidate.Y + candidate.Variant.Height + gap) continue;
            token.ThrowIfCancellationRequested();
            if (candidate.Variant.BoxLike && other.Variant.BoxLike)
            {
                if (candidate.X >= other.X + other.Variant.Width + spacing - 1e-9 ||
                    other.X >= candidate.X + candidate.Variant.Width + spacing - 1e-9 ||
                    candidate.Y >= other.Y + other.Variant.Height + spacing - 1e-9 ||
                    other.Y >= candidate.Y + candidate.Variant.Height + spacing - 1e-9) continue;
                return false;
            }
            material ??= GeometryPrecision.Translate(candidate.Variant.Material, candidate.X, candidate.Y);
            var obstacle = GeometryPrecision.Translate(other.Variant.Halo(spacing), other.X, other.Y);
            var overlap = Clipper.Intersect(material, obstacle, FillRule.NonZero, GeometryPrecision.Digits);
            if (M.Abs(Clipper.Area(overlap)) > 1e-8) return false;
            validationMaterial ??= GeometryPrecision.Translate(candidate.Variant.ValidationRegion(0), candidate.X, candidate.Y);
            var validationObstacle = GeometryPrecision.Translate(other.Variant.ValidationRegion(spacing), other.X, other.Y);
            if (M.Abs(Clipper.Area(Clipper.Intersect(validationMaterial, validationObstacle,
                FillRule.NonZero, GeometryPrecision.Digits))) > 1e-8) return false;
            if (spacing == 0 || candidate.Variant.Material.Count > 1 || other.Variant.Material.Count > 1)
            {
                var outerIntersection = Clipper.Intersect(
                    new PathsD(validationMaterial.Where(Clipper.IsPositive)),
                    new PathsD(validationObstacle.Where(Clipper.IsPositive)), FillRule.NonZero, GeometryPrecision.Digits);
                if (spacing != 0 && M.Abs(Clipper.Area(outerIntersection)) <= 1e-8) continue;
                var key = (candidate.Variant.Id, other.Variant.Id, spacing,
                    candidate.X + validationOriginX, candidate.Y + validationOriginY,
                    other.X + validationOriginX, other.Y + validationOriginY);
                if (!validationCache.TryGetValue(key, out var collides))
                {
                    collides = ValidationOverlap(
                        GeometryPrecision.Translate(validationMaterial, validationOriginX, validationOriginY),
                        GeometryPrecision.Translate(validationObstacle, validationOriginX, validationOriginY));
                    if (validationCache.Count >= 4096) validationCache.Clear();
                    validationCache[key] = collides;
                }
                if (collides) return false;
            }
        }
        return true;
    }
    private static bool ValidationOverlap(PathsD a, PathsD b)
    {
        var holesA = a.Where(p => !Clipper.IsPositive(p)).Select(ClipperBridge.ToPolygon).ToList();
        var holesB = b.Where(p => !Clipper.IsPositive(p)).Select(ClipperBridge.ToPolygon).ToList();
        foreach (var outerA in a.Where(Clipper.IsPositive))
        foreach (var outerB in b.Where(Clipper.IsPositive))
        {
            var pa = ClipperBridge.ToPolygon(outerA);
            var pb = ClipperBridge.ToPolygon(outerB);
            // The benchmark orders by world-space left bound before clipping.
            if (pa.Left <= pb.Left ? Collision.HasOverlap(pa, pb, holesA, holesB) :
                Collision.HasOverlap(pb, pa, holesB, holesA)) return true;
        }
        return false;
    }

}

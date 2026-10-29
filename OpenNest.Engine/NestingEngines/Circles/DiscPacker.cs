#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Circles;

/// <summary>A requested part packed as its enclosing circle at one allowed rotation.</summary>
internal sealed record DiscType(int Index, NestJobPart Part, Item Item, double Rotation, double MaterialArea)
{
    public string Id => Part.Id;
    public int Priority => Part.Priority;
    public double Radius => Item.Radius;
}

/// <summary>Which way a sheet fills: rows from the bottom edge, or columns from the left edge.</summary>
internal enum DiscOrder
{
    BottomLeft,
    LeftBottom,
}

/// <summary>The lattice one type is tiled in before the greedy pass, if any. Staggered rows are
/// densest on a large sheet; a square grid wins when only a few discs span the sheet.</summary>
internal enum DiscSeed
{
    None,
    Hexagonal,
    Square,
}

/// <summary>
/// Packs discs onto one sheet. Each disc goes to the best feasible center tangent to two of
/// {work-area walls, placed discs, clamp keep-outs}, the exact candidate set for circles, so no
/// no-fit polygons are needed. Discs go lower priority number first, then largest first, so small
/// discs land in the gaps between large ones. An optional hexagonal or square lattice of one type
/// seeds the sheet before the greedy pass fills the rest.
/// </summary>
/// <remarks>Every disc keeps the part spacing plus <see cref="Margin"/> from every other disc, and
/// its radius from the work-area edges and from every clamp zone grown by the stock's clamp
/// clearance. Radii are <see cref="Item.Radius"/>, which covers the layout check's outward flattening,
/// so packing is never more lenient than the check. Deterministic.</remarks>
internal static class DiscPacker
{
    /// <summary>Extra gap beyond the spacing between discs, as in <see cref="CutoutCirclePacker"/>.</summary>
    public const double Margin = CutoutCirclePacker.Margin;

    private const double Epsilon = 1e-9;

    public static SheetLayout Pack(IReadOnlyList<DiscType> types, IReadOnlyList<int> remaining,
        NestPlateStock stock, DiscOrder order, DiscSeed seed, CancellationToken token)
    {
        var left = remaining.ToArray();
        var fits = types.Where(t => left[t.Index] > 0 && Fits(stock, t)).ToList();
        var sheet = new Sheet(stock, types.Where(t => Fits(stock, t)).Select(t => t.Radius).DefaultIfEmpty(0).Max(), order);
        if (seed != DiscSeed.None && LatticeType(fits, left) is { } lattice)
        {
            foreach (var center in Lattice(stock.WorkArea, lattice.Radius, sheet.Gap, order, seed))
            {
                token.ThrowIfCancellationRequested();
                if (left[lattice.Index] == 0)
                    break;
                if (!sheet.Feasible(center, lattice.Radius))
                    continue;
                sheet.Place(lattice, center);
                left[lattice.Index]--;
            }
        }
        // Stable: equal radii keep requirement order.
        foreach (var type in fits.OrderBy(t => t.Priority).ThenByDescending(t => t.Radius))
        {
            while (left[type.Index] > 0)
            {
                token.ThrowIfCancellationRequested();
                // Space only shrinks, so a copy that finds no center leaves none for later copies.
                if (sheet.Best(type.Radius) is not { } center)
                    break;
                sheet.Place(type, center);
                left[type.Index]--;
            }
        }
        return sheet.Layout(types);
    }

    /// <summary>True when the disc fits the work area in both directions.</summary>
    public static bool Fits(NestPlateStock stock, DiscType type)
    {
        if (stock.Outline != null)
            return false; // The lattice fills rectangles only.
        var work = stock.WorkArea;
        return 2 * type.Radius <= work.Right - work.Left + Epsilon
            && 2 * type.Radius <= work.Top - work.Bottom + Epsilon;
    }

    /// <summary>The top-priority type with the most remaining area, when it has copies to tile.</summary>
    private static DiscType? LatticeType(IReadOnlyList<DiscType> fits, int[] left)
    {
        if (fits.Count == 0)
            return null;
        var top = fits.Min(t => t.Priority);
        var best = fits.Where(t => t.Priority == top)
            .OrderByDescending(t => left[t.Index] * t.Radius * t.Radius)
            .First();
        return left[best.Index] >= 2 ? best : null;
    }

    /// <summary>Lattice centers in fill order: rows along X for bottom-left, columns along Y for
    /// left-bottom. Neighbors sit exactly one pitch apart.</summary>
    private static IEnumerable<Vector> Lattice(Box work, double r, double gap, DiscOrder order, DiscSeed seed)
    {
        var pitch = 2 * r + gap;
        var hexagonal = seed == DiscSeed.Hexagonal;
        var step = hexagonal ? pitch * System.Math.Sqrt(3) / 2 : pitch;
        var rows = order == DiscOrder.BottomLeft;
        var (along0, along1) = rows ? (work.Left + r, work.Right - r) : (work.Bottom + r, work.Top - r);
        var (across0, across1) = rows ? (work.Bottom + r, work.Top - r) : (work.Left + r, work.Right - r);
        for (var line = 0; across0 + line * step <= across1 + Epsilon; line++)
        {
            var across = across0 + line * step;
            var shift = hexagonal && line % 2 == 1 ? pitch / 2 : 0;
            for (var along = along0 + shift; along <= along1 + Epsilon; along += pitch)
                yield return rows ? new Vector(along, across) : new Vector(across, along);
        }
    }

    private sealed class Sheet
    {
        private readonly NestPlateStock stock;
        private readonly Box work;
        private readonly Box[] clamps;
        private readonly double cell;
        private readonly DiscOrder order;
        private readonly Dictionary<(long, long), List<int>> grid = new();
        private readonly List<(DiscType Type, Vector Center)> placed = new();
        // A disc with no feasible tangent center for radius r has none for any larger radius:
        // shrinking a feasible larger disc toward the same tangent point keeps it feasible.
        private readonly List<double> deadFrom = new();

        public Sheet(NestPlateStock stock, double maxRadius, DiscOrder order)
        {
            this.stock = stock;
            work = stock.WorkArea;
            // The layout check grows each zone by the clamp clearance as a box, square
            // corners included, so discs keep their radius from that grown box.
            var clearance = stock.ClampClearance;
            clamps = stock.ClampZones.Select(z => z.Bounds.Offset(clearance)).ToArray();
            Gap = stock.PartSpacing + Margin;
            // Any disc within reach of a new one lies in the adjacent cells.
            cell = System.Math.Max(2 * maxRadius + Gap, 1e-6);
            this.order = order;
        }

        public double Gap { get; }

        public void Place(DiscType type, Vector center)
        {
            var key = Key(center);
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = new List<int>();
            list.Add(placed.Count);
            placed.Add((type, center));
            deadFrom.Add(double.PositiveInfinity);
        }

        public bool Feasible(Vector p, double r)
        {
            if (p.X < work.Left + r - Epsilon || p.X > work.Right - r + Epsilon
                || p.Y < work.Bottom + r - Epsilon || p.Y > work.Top - r + Epsilon)
                return false;
            foreach (var zone in clamps)
                if (Distance(p, zone) < r - Epsilon)
                    return false;
            foreach (var j in Near(p, 1))
                if (placed[j].Center.DistanceTo(p) < placed[j].Type.Radius + r + Gap - Epsilon)
                    return false;
            return true;
        }

        /// <summary>The best feasible center for a disc of radius <paramref name="r"/>, or null.</summary>
        public Vector? Best(double r)
        {
            var vertical = new List<double> { work.Left + r, work.Right - r };
            var horizontal = new List<double> { work.Bottom + r, work.Top - r };
            var corners = new List<Circle>();
            foreach (var zone in clamps)
            {
                vertical.Add(zone.Left - r);
                vertical.Add(zone.Right + r);
                horizontal.Add(zone.Bottom - r);
                horizontal.Add(zone.Top + r);
                corners.Add(new Circle(new Vector(zone.Left, zone.Bottom), r));
                corners.Add(new Circle(new Vector(zone.Right, zone.Bottom), r));
                corners.Add(new Circle(new Vector(zone.Left, zone.Top), r));
                corners.Add(new Circle(new Vector(zone.Right, zone.Top), r));
            }

            Vector? best = null;
            void Consider(Vector p)
            {
                if ((best is not { } current || Better(p, current)) && Feasible(p, r))
                    best = p;
            }

            foreach (var x in vertical)
                foreach (var y in horizontal)
                    Consider(new Vector(x, y));
            for (var a = 0; a < corners.Count; a++)
            {
                foreach (var p in Lines(corners[a], vertical, horizontal))
                    Consider(p);
                for (var b = a + 1; b < corners.Count; b++)
                    foreach (var p in Intersections(corners[a], corners[b]))
                        Consider(p);
            }

            for (var i = 0; i < placed.Count; i++)
            {
                if (r >= deadFrom[i] - Epsilon)
                    continue;
                var around = new Circle(placed[i].Center, placed[i].Type.Radius + r + Gap);
                var generated = false;
                var feasible = false;
                void Tangent(Vector p)
                {
                    generated = true;
                    if (!Feasible(p, r))
                        return;
                    feasible = true;
                    if (best is not { } current || Better(p, current))
                        best = p;
                }

                foreach (var p in Lines(around, vertical, horizontal))
                    Tangent(p);
                foreach (var corner in corners)
                    foreach (var p in Intersections(around, corner))
                        Tangent(p);
                foreach (var j in Near(around.Center, 2))
                {
                    if (j <= i || r >= deadFrom[j] - Epsilon)
                        continue;
                    foreach (var p in Intersections(around,
                        new Circle(placed[j].Center, placed[j].Type.Radius + r + Gap)))
                        Tangent(p);
                }
                // A tangent circle that crosses nothing is either wholly free or wholly blocked;
                // only one that crosses constraints yet keeps no feasible point is surely closed.
                if (generated && !feasible)
                    deadFrom[i] = r;
            }
            return best;
        }

        public SheetLayout Layout(IReadOnlyList<DiscType> types)
        {
            var counts = new int[types.Count];
            var poses = new List<(string PartId, double X, double Y, double Rotation)>(placed.Count);
            Box? envelope = null;
            foreach (var (type, center) in placed)
            {
                counts[type.Index]++;
                var origin = center + Rotate(type.Item.PartOffset, type.Rotation);
                poses.Add((type.Id, origin.X, origin.Y, type.Rotation));
                var box = new Box(center.X - type.Radius, center.Y - type.Radius, 2 * type.Radius, 2 * type.Radius);
                envelope = envelope == null ? box : Union(envelope, box);
            }
            // Summed per type in type order, the form of the stock loop's demand area.
            var area = types.Sum(t => counts[t.Index] * t.MaterialArea);
            return new SheetLayout(stock, poses, area, envelope);
        }

        private bool Better(Vector candidate, Vector current)
        {
            var (a, b) = order == DiscOrder.BottomLeft
                ? (candidate.Y - current.Y, candidate.X - current.X)
                : (candidate.X - current.X, candidate.Y - current.Y);
            if (System.Math.Abs(a) > Epsilon)
                return a < 0;
            return b < -Epsilon;
        }

        private (long, long) Key(Vector p) =>
            ((long)System.Math.Floor((p.X - work.Left) / cell), (long)System.Math.Floor((p.Y - work.Bottom) / cell));

        private IEnumerable<int> Near(Vector p, int cells)
        {
            var (kx, ky) = Key(p);
            for (var dx = -cells; dx <= cells; dx++)
                for (var dy = -cells; dy <= cells; dy++)
                    if (grid.TryGetValue((kx + dx, ky + dy), out var list))
                        foreach (var j in list)
                            yield return j;
        }

        private static double Distance(Vector p, Box box)
        {
            var dx = System.Math.Max(0, System.Math.Max(box.Left - p.X, p.X - box.Right));
            var dy = System.Math.Max(0, System.Math.Max(box.Bottom - p.Y, p.Y - box.Top));
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private static IEnumerable<Vector> Lines(Circle circle, List<double> vertical, List<double> horizontal)
    {
        var c = circle.Center;
        var r = circle.Radius;
        foreach (var x in vertical)
        {
            var d = x - c.X;
            if (System.Math.Abs(d) > r)
                continue;
            var h = System.Math.Sqrt(System.Math.Max(0, r * r - d * d));
            yield return new Vector(x, c.Y - h);
            if (h > Epsilon)
                yield return new Vector(x, c.Y + h);
        }
        foreach (var y in horizontal)
        {
            var d = y - c.Y;
            if (System.Math.Abs(d) > r)
                continue;
            var h = System.Math.Sqrt(System.Math.Max(0, r * r - d * d));
            yield return new Vector(c.X - h, y);
            if (h > Epsilon)
                yield return new Vector(c.X + h, y);
        }
    }

    private static IEnumerable<Vector> Intersections(Circle a, Circle b)
    {
        var d = a.Center.DistanceTo(b.Center);
        if (d < Epsilon || d > a.Radius + b.Radius || d < System.Math.Abs(a.Radius - b.Radius))
            yield break;
        var along = (a.Radius * a.Radius - b.Radius * b.Radius + d * d) / (2 * d);
        var across = System.Math.Sqrt(System.Math.Max(0, a.Radius * a.Radius - along * along));
        var ux = (b.Center.X - a.Center.X) / d;
        var uy = (b.Center.Y - a.Center.Y) / d;
        var mx = a.Center.X + ux * along;
        var my = a.Center.Y + uy * along;
        yield return new Vector(mx - uy * across, my + ux * across);
        if (across > Epsilon)
            yield return new Vector(mx + uy * across, my - ux * across);
    }

    private static Vector Rotate(Vector v, double angle)
    {
        var sine = System.Math.Sin(angle);
        var cosine = System.Math.Cos(angle);
        return new Vector(v.X * cosine - v.Y * sine, v.X * sine + v.Y * cosine);
    }

    private static Box Union(Box a, Box b)
    {
        var l = System.Math.Min(a.Left, b.Left);
        var bo = System.Math.Min(a.Bottom, b.Bottom);
        var r = System.Math.Max(a.Right, b.Right);
        var t = System.Math.Max(a.Top, b.Top);
        return new Box(l, bo, r - l, t - bo);
    }
}

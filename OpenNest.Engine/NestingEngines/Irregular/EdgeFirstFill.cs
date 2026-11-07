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
/// Packs one sheet's given parts edge first: the large parts go to the sheet's corners and edges,
/// one large part (the key) fits the pocket they leave in the middle, and the small parts fill the
/// gaps last. All or nothing: returns null unless every part is placed.
///
/// The frontier packer grows a layout from one side, so parts that only fit together when the
/// outer ones hug opposite edges never meet. Here each edge part takes the corner where it sits
/// flush, preferring the pose with the most outline on the sheet boundary and then the corner
/// farthest from the parts already placed, so the free room collects in the middle. The key part
/// takes any sampled orientation that fits there; failing that, <see cref="ContactFit"/> turns it
/// against its neighbors to an angle no catalog holds. Each large part is tried as the key in
/// turn, largest first.
///
/// Every position comes from an exact free region (inner-fit rectangle less the no-fit polygons
/// of clamps and placed parts), as in <see cref="FrontierPacker"/>. Deterministic.
/// </summary>
internal sealed class EdgeFirstFill(
    IReadOnlyList<PartType> types,
    NoFitCache nfps,
    NestPlateStock stock,
    ClampObstacles? clamps,
    WorkCounter counter,
    Func<FrontierPacker> fillers,
    Func<PartType, double, Orientation?> orient)
{
    /// <summary>Parts below this share of the largest part's area fill gaps after the key.</summary>
    internal const double FillerShare = 0.15;

    /// <summary>Corner poses tried per edge part, best first.</summary>
    internal const int Branches = 2;

    /// <summary>Most complete edge arrangements tried per key.</summary>
    internal const int MaxLeaves = 8;

    /// <summary>Most part types tried as the key.</summary>
    internal const int MaxKeys = 4;

    /// <summary>Corner distances within this count as equally flush.</summary>
    private const double Flush = 0.05;

    /// <summary>Outline edges this close to a work-area side lie on it.</summary>
    private const double OnEdge = 0.02;

    /// <summary>Most pockets the key part is started in.</summary>
    private const int MaxPockets = 2;

    /// <summary>Angle step, in radians (0.1 degree), of the exact checks around a solved angle.</summary>
    private const double RefineStep = System.Math.PI / 1800;

    /// <summary>Exact checks each side of a solved angle (two degrees).</summary>
    private const int RefineSteps = 20;

    /// <summary>Same slack as the frontier packer's inner-fit rectangle.</summary>
    private const double FitSlack = 2e-4;

    /// <summary>
    /// The work area this fill packs: the stock's, with each side that carries clamps moved in
    /// just past their keep-outs, as a larger edge spacing would. Packing the clear rectangle
    /// above the clamps keeps parts flush with one straight edge instead of stepping between
    /// clamps. A clamp off every side stays an obstacle instead.
    /// </summary>
    private readonly Box work = ClearOfClamps(stock.WorkArea, clamps).Area;

    /// <summary>Clamps not cleared by moving a side: obstacles inside the work area.</summary>
    private readonly ClampObstacles? obstacles = ClearOfClamps(stock.WorkArea, clamps).OnEdges ? null : clamps;

    private static (Box Area, bool OnEdges) ClearOfClamps(Box area, ClampObstacles? clamps)
    {
        var allOnEdges = true;
        double left = area.Left, right = area.Right, bottom = area.Bottom, top = area.Top;
        for (var i = 0; clamps != null && i < clamps.Count; i++)
        {
            var box = Clipper.GetBounds(clamps.Outline(i));
            if (box.right <= area.Left || box.left >= area.Right || box.bottom <= area.Bottom || box.top >= area.Top)
                continue; // Outside the work area: nothing to clear.
            if (box.left <= area.Left + OnEdge)
                left = System.Math.Max(left, box.right);
            else if (box.right >= area.Right - OnEdge)
                right = System.Math.Min(right, box.left);
            else if (box.top <= area.Bottom + OnEdge) // Clipper RectD: top is the minimum Y.
                bottom = System.Math.Max(bottom, box.bottom);
            else if (box.bottom >= area.Top - OnEdge)
                top = System.Math.Min(top, box.top);
            else
                allOnEdges = false;
        }
        if (!allOnEdges || right <= left || top <= bottom)
            return (area, false);
        return (new Box(left, bottom, right - left, top - bottom), true);
    }

    public SheetFill? Fill(IReadOnlyList<int> demand, CancellationToken token)
    {
        var instances = new List<int>();
        foreach (var type in types)
            for (var i = 0; i < demand[type.Index]; i++)
                instances.Add(type.Index);
        if (instances.Count == 0 || instances.Any(t => types[t].Orientations.Count == 0))
            return null;
        var largest = instances.Max(t => types[t].Area);
        var majors = instances.Where(t => types[t].Area >= FillerShare * largest)
            .OrderByDescending(t => types[t].Area).ThenBy(t => t).ToList();
        var small = new int[types.Count];
        foreach (var t in instances.Where(t => types[t].Area < FillerShare * largest))
            small[t]++;
        var smallCount = small.Sum();

        foreach (var key in majors.Distinct().Take(MaxKeys))
        {
            token.ThrowIfCancellationRequested();
            var edges = majors.ToList();
            edges.Remove(key);
            var placed = new List<Placed>();
            var leaves = 0;
            if (!Arrange(edges, 0, types[key], placed, small, smallCount, ref leaves, token))
                continue;
            return new SheetFill(stock, placed, placed.Sum(p => types[p.Orientation.TypeIndex].Area));
        }
        return null;
    }

    /// <summary>
    /// Places the edge parts from <paramref name="next"/> on, each in one of its best
    /// <see cref="Branches"/> corner poses, depth first, then the key and the small parts; at most
    /// <see cref="MaxLeaves"/> complete edge arrangements per key. Leaves <paramref name="placed"/>
    /// holding the sheet on success.
    /// </summary>
    private bool Arrange(List<int> edges, int next, PartType key, List<Placed> placed, int[] small, int smallCount,
        ref int leaves, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (next == edges.Count)
        {
            if (leaves++ >= MaxLeaves)
                return false;
            var middle = Key(key, placed);
            if (middle == null)
                return false;
            placed.Add(middle);
            if (smallCount > 0)
            {
                var gaps = fillers().Fill(small, token, placed);
                if (gaps.Parts.Count != smallCount)
                {
                    placed.RemoveAt(placed.Count - 1);
                    return false;
                }
                placed.AddRange(gaps.Parts);
            }
            return true;
        }
        foreach (var pose in Corners(types[edges[next]], placed).Take(Branches))
        {
            placed.Add(pose);
            if (Arrange(edges, next + 1, key, placed, small, smallCount, ref leaves, token))
                return true;
            placed.RemoveAt(placed.Count - 1);
            if (leaves >= MaxLeaves)
                return false;
        }
        return false;
    }

    /// <summary>The flush corner pose with the most outline on the sheet edges, then the one
    /// thinnest across the sheet (hugging its long edges), then the one farthest from the parts
    /// placed.</summary>
    private List<Placed> Corners(PartType type, IReadOnlyList<Placed> placed)
    {
        var ranked = new List<(Placed Pose, double Gap, double Contact, double Across, double Spread)>();
        var lengthwise = work.Length >= work.Width;
        foreach (var o in type.Orientations)
        {
            var free = Free(o, placed, out var minX, out var minY, out var maxX, out var maxY);
            if (free.Count == 0)
                continue;
            for (var corner = 0; corner < 4; corner++)
            {
                var right = corner is 1 or 2;
                var top = corner is 2 or 3;
                double cornerGap = double.PositiveInfinity, x = 0, y = 0;
                foreach (var path in free)
                    foreach (var raw in path)
                    {
                        var px = System.Math.Clamp(raw.x, minX, maxX);
                        var py = System.Math.Clamp(raw.y, minY, maxY);
                        var gap = (right ? work.Right - (px + o.MaxX) : px + o.MinX - work.Left)
                            + (top ? work.Top - (py + o.MaxY) : py + o.MinY - work.Bottom);
                        if (gap < cornerGap - 1e-9)
                        {
                            cornerGap = gap;
                            x = px;
                            y = py;
                        }
                    }
                if (double.IsPositiveInfinity(cornerGap))
                    continue;
                var candidate = new Placed(o, x, y);
                if (ranked.Any(r => ReferenceEquals(r.Pose.Orientation, o) && System.Math.Abs(r.Pose.X - x) < 1e-6
                        && System.Math.Abs(r.Pose.Y - y) < 1e-6))
                    continue;
                ranked.Add((candidate, System.Math.Floor(cornerGap / Flush), Contact(candidate),
                    lengthwise ? o.Height : o.Width, Spread(candidate, placed)));
            }
        }
        // Flush first, then most outline on the edges, thinnest across the sheet, farthest out;
        // a stable sort keeps orientation and corner order on ties.
        return ranked
            .OrderBy(r => r.Gap)
            .ThenByDescending(r => System.Math.Round(r.Contact, 4))
            .ThenBy(r => System.Math.Round(r.Across, 4))
            .ThenByDescending(r => System.Math.Round(r.Spread, 4))
            .Select(r => r.Pose)
            .ToList();
    }

    /// <summary>The key part: a sampled orientation if one fits, else a contact-solved angle.</summary>
    private Placed? Key(PartType type, IReadOnlyList<Placed> placed)
    {
        var pockets = Pockets(placed, type.Area);
        foreach (var o in type.Orientations)
        {
            var free = Free(o, placed, out var minX, out var minY, out var maxX, out var maxY);
            if (free.Count > 0)
                return Nearest(o, free, minX, minY, maxX, maxY, pockets.FirstOrDefault());
        }
        if (type.Part.Rotation.Kind != RotationPolicyKind.Automatic || pockets.Count == 0)
            return null;

        var contacts = new List<ContactFit.Obstacle>();
        foreach (var part in placed)
            contacts.Add(new(Clipper.TranslatePath(nfps.Footprint(part.Orientation), part.X, part.Y), true));
        for (var i = 0; obstacles != null && i < obstacles.Count; i++)
            contacts.Add(new(obstacles.Outline(i), false));
        foreach (var pocket in pockets)
            foreach (var start in Starts(type))
            {
                var pose = ContactFit.Solve(start, nfps.Footprint(start), contacts, work, pocket.x, pocket.y, counter);
                if (pose is not { } solved)
                    continue;
                // The contacts bring the part to the edge of its fit; the exact free region
                // decides, from the solved angle outward until one fits.
                for (var step = 0; step <= 2 * RefineSteps; step++)
                {
                    var offset = (step + 1) / 2 * RefineStep * (step % 2 == 0 ? 1 : -1);
                    var angle = solved.Rotation + offset;
                    if (!type.Part.Rotation.Allows(angle))
                        continue;
                    var o = orient(type, angle);
                    if (o == null)
                        continue;
                    var free = Free(o, placed, out var minX, out var minY, out var maxX, out var maxY);
                    if (free.Count > 0)
                        return Nearest(o, free, minX, minY, maxX, maxY, null, solved.X, solved.Y);
                }
            }
        return null;
    }

    /// <summary>Starting poses for the contact solve: the orientations whose box fits the work
    /// area, or all of them when none does (only a turn can fit the part then).</summary>
    private IEnumerable<Orientation> Starts(PartType type)
    {
        var fitting = type.Orientations.Where(o => o.Width <= work.Length + 1e-9 && o.Height <= work.Width + 1e-9).ToList();
        return fitting.Count > 0 ? fitting : type.Orientations;
    }

    /// <summary>Centers of the largest free pockets that could hold <paramref name="area"/>.</summary>
    private List<PointD> Pockets(IReadOnlyList<Placed> placed, double area)
    {
        var room = new PathsD { Rectangle(work.Left, work.Bottom, work.Right, work.Top) };
        var taken = new PathsD(placed.Select(p => Clipper.TranslatePath(nfps.Footprint(p.Orientation), p.X, p.Y)));
        for (var i = 0; obstacles != null && i < obstacles.Count; i++)
            taken.Add(obstacles.Outline(i));
        room = Clipper.Difference(room, taken, FillRule.NonZero, NoFitCache.Precision);
        return room.Where(p => Clipper.IsPositive(p) && Clipper.Area(p) >= area)
            .OrderByDescending(p => Clipper.Area(p))
            .Take(MaxPockets)
            .Select(Centroid)
            .ToList();
    }

    private static PointD Centroid(PathD path)
    {
        double a = 0, cx = 0, cy = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var p = path[i];
            var q = path[(i + 1) % path.Count];
            var cross = p.x * q.y - q.x * p.y;
            a += cross;
            cx += (p.x + q.x) * cross;
            cy += (p.y + q.y) * cross;
        }
        return System.Math.Abs(a) < 1e-12 ? path[0] : new PointD(cx / (3 * a), cy / (3 * a));
    }

    /// <summary>The free vertex whose part center (or reference, with <paramref name="refX"/>)
    /// lies nearest the target.</summary>
    private static Placed Nearest(Orientation o, PathsD free, double minX, double minY, double maxX, double maxY,
        PointD? center, double? refX = null, double? refY = null)
    {
        Placed? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var path in free)
            foreach (var raw in path)
            {
                var x = System.Math.Clamp(raw.x, minX, maxX);
                var y = System.Math.Clamp(raw.y, minY, maxY);
                double dx, dy;
                if (refX is double rx && refY is double ry)
                {
                    dx = x - rx;
                    dy = y - ry;
                }
                else if (center is { } c)
                {
                    dx = x + (o.MinX + o.MaxX) / 2 - c.x;
                    dy = y + (o.MinY + o.MaxY) / 2 - c.y;
                }
                else
                {
                    dx = x;
                    dy = y;
                }
                var distance = dx * dx + dy * dy;
                if (distance < bestDistance - 1e-12)
                {
                    bestDistance = distance;
                    best = new Placed(o, x, y);
                }
            }
        return best!;
    }

    /// <summary>Length of outline lying on the work area's sides.</summary>
    private double Contact(Placed part)
    {
        var outline = part.Orientation.Outline;
        // Arc outlines keep flattening padding in their bounds, so they stop that far short.
        var near = OnEdge + part.Orientation.Tolerance;
        bool On(double a, double b, double side) => System.Math.Abs(a - side) < near && System.Math.Abs(b - side) < near;
        var total = 0.0;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            double ax = a.x + part.X, ay = a.y + part.Y, bx = b.x + part.X, by = b.y + part.Y;
            var along = On(ax, bx, work.Left) || On(ax, bx, work.Right) || On(ay, by, work.Bottom) || On(ay, by, work.Top);
            if (along)
                total += System.Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        }
        return total;
    }

    private static double Spread(Placed part, IReadOnlyList<Placed> placed)
    {
        if (placed.Count == 0)
            return 0;
        var cx = (part.Left + part.Right) / 2;
        var cy = (part.Bottom + part.Top) / 2;
        return placed.Min(p =>
        {
            var dx = (p.Left + p.Right) / 2 - cx;
            var dy = (p.Bottom + p.Top) / 2 - cy;
            return System.Math.Sqrt(dx * dx + dy * dy);
        });
    }

    /// <summary>Legal reference points of <paramref name="o"/>: the inner-fit rectangle less the
    /// no-fit polygons of clamps and placed parts. Empty when it does not fit.</summary>
    private PathsD Free(Orientation o, IReadOnlyList<Placed> placed,
        out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = work.Left - o.MinX;
        maxX = work.Right - o.MaxX;
        minY = work.Bottom - o.MinY;
        maxY = work.Top - o.MaxY;
        if (o.Width > work.Length + 1e-9 || o.Height > work.Width + 1e-9)
            return new PathsD();
        maxX = System.Math.Max(minX, maxX);
        maxY = System.Math.Max(minY, maxY);
        var free = new PathsD { Rectangle(minX - FitSlack, minY - FitSlack, maxX + FitSlack, maxY + FitSlack) };
        for (var i = 0; obstacles != null && i < obstacles.Count && free.Count > 0; i++)
            free = Subtract(free, obstacles.Get(i, o), 0, 0);
        foreach (var part in placed)
        {
            if (free.Count == 0)
                break;
            free = Subtract(free, nfps.Get(part.Orientation, o), part.X, part.Y);
        }
        return free;
    }

    private PathsD Subtract(PathsD free, Nfp nfp, double dx, double dy)
    {
        counter.Add(1);
        var result = Clipper.Difference(free, Clipper.TranslatePaths(nfp.Region, dx, dy), FillRule.NonZero,
            NoFitCache.Precision);
        result.RemoveAll(p => p.Count < 3);
        return result;
    }

    private static PathD Rectangle(double left, double bottom, double right, double top) =>
        new() { new(left, bottom), new(right, bottom), new(right, top), new(left, top) };
}

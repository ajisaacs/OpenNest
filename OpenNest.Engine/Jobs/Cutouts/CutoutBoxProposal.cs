#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Engine.RectanglePacking;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>
/// Frame-local proposal for a rectangular cutout, solved the way a person would: a small knapsack
/// over which inserts to take, packed as boxes. A counted beam search over maximal free rectangles
/// picks insert copies at their minimum-area rotation and its quarter turn, plus a part and its half
/// turn slid together when that box is clearly denser, to maximize seated outline area.
/// <see cref="CutoutPackerProposal"/> then drops the remaining demand into what is left. Every pose
/// is certified with <see cref="NestLayoutCheck.Clears"/>. Deterministic; never changes a job.
/// </summary>
internal static class CutoutBoxProposal
{
    private const double MaxSpacing = 100;

    /// <summary>Hole area over bounding-box area below which a hole is not a rectangle.</summary>
    private const double MinRectangularity = 0.97;

    /// <summary>Inscribed rectangle area over hole area below which a notch has ruined it.</summary>
    private const double MinInscribed = 0.8;

    /// <summary>Points per edge when finding what intrudes on the rectangle; catches a chamfer.</summary>
    private const int EdgeSamples = 16;

    private const double InsideTolerance = 1e-9;

    private const int BeamWidth = 150;

    /// <summary>Search states expanded per pass: the bound on the search, independent of time.</summary>
    private const int NodesPerPass = 6000;

    /// <summary>Best distinct insert selections certified and gap-filled.</summary>
    private const int PlansTried = 3;

    /// <summary>A part denser than this in its box is not worth pairing.</summary>
    private const double MaxPairedDensity = 0.8;

    private const double MinPairGain = 0.05;
    private const int SlideSteps = 12;

    /// <summary>One way of searching: anchor order, and how much of the free space the ranking
    /// expects to fill. A lean guess favors dense early choices, a full one keeps options open.</summary>
    private static readonly (bool Columns, double Density)[] Passes =
    {
        (false, 0.85), (true, 0.85), (false, 0.6), (true, 0.6),
    };

    /// <summary>A box the search can place: one copy or a pair. Width and Height carry the part
    /// spacing; each member's pose is relative to the box's lower-left corner.</summary>
    private sealed record Item(int Type, int Copies, double Width, double Height, double Area,
        (double X, double Y, double Rotation)[] Members);

    private sealed record State(IReadOnlyList<Rect> Free, int[] Used, double Seated, double Rank,
        State? Parent, Item? Item, double X, double Y);

    /// <summary>Poses in the frame's own coordinates clearing the frame and each other; empty when
    /// the hole is not a rectangle. Instance indices count per requirement.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CutoutMemo? memo = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(inserts);
        token.ThrowIfCancellationRequested();
        if (inserts.Count == 0 || !double.IsFinite(spacing) || spacing < 0 || spacing > MaxSpacing)
            return Array.Empty<NestJobPlacement>();
        var frameGeometry = memo?.Read(frame.Geometry) ?? JobPartGeometry.TryRead(frame.Geometry);
        if (frameGeometry == null || !CutoutCoordinates.WithinRange(frameGeometry.Bounds))
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var cutout = frameGeometry.Cutouts[cutoutIndex];
        if (!cutout.IsClosed() || !CutoutCoordinates.WithinRange(cutout.BoundingBox))
            return Array.Empty<NestJobPlacement>();
        var hole = ClipperBridge.ToPath(
            ClipperBridge.Flatten(cutout, CutoutLatticeFill.FlattenTolerance, circumscribe: false), new Vector());
        if (InscribedRectangle(hole) is not { } rectangle || spacing * 2 >= System.Math.Min(rectangle.W, rectangle.H))
            return Array.Empty<NestJobPlacement>();
        var geometries = new Dictionary<string, JobPartGeometry>(StringComparer.Ordinal);
        foreach (var insert in inserts)
        {
            var geometry = memo?.Read(insert.Geometry) ?? JobPartGeometry.TryRead(insert.Geometry);
            if (geometry == null || !CutoutCoordinates.WithinRange(geometry.Bounds))
                return Array.Empty<NestJobPlacement>();
            geometries[insert.Id] = geometry;
        }

        // Boxes grow by the spacing on their right and top, and the bin by one spacing: touching
        // boxes are one spacing apart and the last may reach the wall's inset, as on a sheet.
        var binWidth = rectangle.W - spacing;
        var binHeight = rectangle.H - spacing;
        var stock = new NestPlateStock("cutout", new Size(rectangle.H, rectangle.W), 1, spacing);
        var types = BoxCatalog.Build(new NestJob(inserts, new[] { stock }));
        var items = Items(types, geometries, spacing, binWidth, binHeight, memo, token);
        if (items.Count == 0)
            return Array.Empty<NestJobPlacement>();
        var demand = inserts.Select(p => p.Quantity).ToArray();
        var plans = new Dictionary<long, State>();
        foreach (var (columns, density) in Passes)
            Search(items, demand, binWidth, binHeight, columns, density, plans, token);

        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        var preparedFrame = NestLayoutCheck.Prepare(frameGeometry, framePose, spacing);
        List<NestJobPlacement>? best = null;
        var bestArea = 0.0;
        foreach (var plan in plans.Values.OrderByDescending(p => p.Seated).Take(PlansTried))
        {
            token.ThrowIfCancellationRequested();
            var poses = new List<NestJobPlacement>();
            foreach (var (item, x, y) in Placements(plan))
            {
                var kept = new List<NestJobPlacement>();
                foreach (var (dx, dy, rotation) in item.Members)
                {
                    var part = inserts[item.Type];
                    var pose = new NestJobPlacement(part.Id, 0,
                        System.Math.Round(rectangle.X + spacing + x + dx, 8),
                        System.Math.Round(rectangle.Y + spacing + y + dy, 8), rotation);
                    if (Clears(pose, geometries, preparedFrame, poses.Concat(kept), spacing, memo, token))
                        kept.Add(pose);
                }
                poses.AddRange(kept);
            }
            if (poses.Count == 0)
                continue;
            poses.AddRange(GapFill(frame, cutoutIndex, inserts, poses, geometries, rectangle, spacing, memo, token));
            var area = OutlineArea(poses, id => geometries[id]);
            if (area > bestArea)
            {
                best = poses;
                bestArea = area;
            }
        }
        if (best == null)
            return Array.Empty<NestJobPlacement>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        return best.Select(p =>
        {
            var index = counts.GetValueOrDefault(p.PartId);
            counts[p.PartId] = index + 1;
            return p with { InstanceIndex = index };
        }).ToArray();
    }

    /// <summary>Outline area of the placed parts, the measure this proposal maximizes.</summary>
    internal static double OutlineArea(IEnumerable<NestJobPlacement> poses, Func<string, JobPartGeometry> geometry) =>
        poses.Sum(p => System.Math.Abs(geometry(p.PartId).Perimeter.Area()));

    /// <summary>The axis-aligned rectangle a nearly rectangular hole holds, or null: starts from
    /// the bounds and moves a side past whatever intrudes deepest, so rounded corners cost a side
    /// about 0.3 of their radius. It may still clip a corner by a chord; certification decides.</summary>
    internal static Rect? InscribedRectangle(PathD hole)
    {
        if (hole.Count < 3)
            return null;
        var left = hole.Min(p => p.x);
        var right = hole.Max(p => p.x);
        var bottom = hole.Min(p => p.y);
        var top = hole.Max(p => p.y);
        var bounds = (right - left) * (top - bottom);
        var area = System.Math.Abs(Clipper.Area(hole));
        if (!(bounds > 0) || area < MinRectangularity * bounds)
            return null;

        var points = new List<PointD>(hole.Count * EdgeSamples);
        for (var i = 0; i < hole.Count; i++)
        {
            var a = hole[i];
            var b = hole[(i + 1) % hole.Count];
            for (var s = 0; s < EdgeSamples; s++)
            {
                var t = s / (double)EdgeSamples;
                points.Add(new PointD(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
            }
        }
        for (var moves = 0; moves < points.Count; moves++)
        {
            var depth = InsideTolerance;
            PointD? deepest = null;
            foreach (var p in points)
            {
                var d = System.Math.Min(System.Math.Min(p.x - left, right - p.x),
                    System.Math.Min(p.y - bottom, top - p.y));
                if (d > depth)
                {
                    depth = d;
                    deepest = p;
                }
            }
            if (deepest is not { } q)
                break;
            if (q.x - left == depth)
                left = q.x;
            else if (right - q.x == depth)
                right = q.x;
            else if (q.y - bottom == depth)
                bottom = q.y;
            else
                top = q.y;
        }
        var width = right - left;
        var height = top - bottom;
        return width > 0 && height > 0 && width * height >= MinInscribed * area
            ? new Rect(left, bottom, width, height)
            : null;
    }

    private static List<Item> Items(IReadOnlyList<BoxType> types, Dictionary<string, JobPartGeometry> geometries,
        double spacing, double binWidth, double binHeight, CutoutMemo? memo, CancellationToken token)
    {
        var items = new List<Item>();
        foreach (var type in types)
        {
            var geometry = geometries[type.Id];
            var area = System.Math.Abs(geometry.Perimeter.Area());
            foreach (var orientation in type.Orientations)
            {
                token.ThrowIfCancellationRequested();
                Add(new Item(type.Index, 1, orientation.Width + spacing, orientation.Height + spacing, area,
                    new[] { (-orientation.OffsetX, -orientation.OffsetY, orientation.Angle) }));
                var density = area / (orientation.Width * orientation.Height);
                if (type.Part.Quantity < 2 || density >= MaxPairedDensity)
                    continue;
                foreach (var pair in Pairs(type, orientation, geometry, area, density, spacing, memo, token))
                    Add(pair);
            }
        }
        return items;

        void Add(Item item)
        {
            if (item.Width <= binWidth + MaxRectsSheet.Eps && item.Height <= binHeight + MaxRectsSheet.Eps)
                items.Add(item);
        }
    }

    /// <summary>A copy and its half turn slid together along each axis: two triangles close into
    /// a rectangle. No best-fit search, so this adds a dozen clearance checks per slide.</summary>
    private static IEnumerable<Item> Pairs(BoxType type, BoxOrientation orientation, JobPartGeometry geometry,
        double area, double density, double spacing, CutoutMemo? memo, CancellationToken token)
    {
        var turned = Angle.NormalizeRad(orientation.Angle + System.Math.PI);
        if (!type.Part.Rotation.Allows(turned)
            || BoxCatalog.RotatedMaterialBounds(type.Part.Geometry, turned) is not { } bounds)
            yield break;
        var first = new NestJobPlacement(type.Id, 0, -orientation.OffsetX, -orientation.OffsetY, orientation.Angle);
        foreach (var along in new[] { true, false })
        {
            var clear = (along ? orientation.Width : orientation.Height) + spacing;
            NestJobPlacement Second(double slide) => new(type.Id, 1,
                (along ? slide : 0) - bounds.Left, (along ? 0 : slide) - bounds.Bottom, turned);
            bool Free(double slide) => memo?.Clears(geometry, first, geometry, Second(slide), spacing)
                ?? NestLayoutCheck.Clears(geometry, first, geometry, Second(slide), spacing);
            token.ThrowIfCancellationRequested();
            if (!Free(clear))
                continue;
            var low = 0.0;
            for (var step = 0; step < SlideSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                var middle = (low + clear) / 2;
                if (Free(middle))
                    clear = middle;
                else
                    low = middle;
            }
            var width = System.Math.Max(orientation.Width, (along ? clear : 0) + bounds.Width);
            var height = System.Math.Max(orientation.Height, (along ? 0 : clear) + bounds.Height);
            if (2 * area / (width * height) < density + MinPairGain)
                continue;
            yield return new Item(type.Index, 2, width + spacing, height + spacing, 2 * area, new[]
            {
                (first.X, first.Y, first.Rotation),
                ((along ? clear : 0) - bounds.Left, (along ? 0 : clear) - bounds.Bottom, turned),
            });
        }
    }

    /// <summary>Beam search over the lowest-leftmost (or leftmost-lowest) free corner: each level
    /// puts a box there or gives the corner up. Adds its best distinct selections to
    /// <paramref name="plans"/>, which keeps one layout per selection.</summary>
    private static void Search(IReadOnlyList<Item> items, int[] demand, double binWidth, double binHeight,
        bool columns, double density, Dictionary<long, State> plans, CancellationToken token)
    {
        var used = new int[demand.Length];
        var free = Usable(new[] { new Rect(0, 0, binWidth, binHeight) }, items, used, demand);
        var beam = new List<State> { new(free, used, 0, 0, null, null, 0, 0) };
        var nodes = 0;
        while (beam.Count > 0 && nodes < NodesPerPass)
        {
            token.ThrowIfCancellationRequested();
            var next = new List<State>();
            foreach (var state in beam)
            {
                Keep(plans, state);
                if (state.Free.Count == 0 || nodes++ >= NodesPerPass)
                    continue;
                var anchor = state.Free[0];
                foreach (var candidate in state.Free)
                    if (columns
                        ? (candidate.X, candidate.Y).CompareTo((anchor.X, anchor.Y)) < 0
                        : (candidate.Y, candidate.X).CompareTo((anchor.Y, anchor.X)) < 0)
                        anchor = candidate;
                foreach (var item in items)
                {
                    if (state.Used[item.Type] + item.Copies > demand[item.Type]
                        || item.Width > anchor.W + MaxRectsSheet.Eps || item.Height > anchor.H + MaxRectsSheet.Eps)
                        continue;
                    var taken = (int[])state.Used.Clone();
                    taken[item.Type] += item.Copies;
                    var box = new Rect(anchor.X, anchor.Y, item.Width, item.Height);
                    next.Add(Child(Usable(MaxRectsSheet.Carve(state.Free, box), items, taken, demand),
                        taken, state.Seated + item.Area, state, item, anchor.X, anchor.Y));
                }
                // Giving the corner up drops only this rectangle; overlapping ones stay.
                var rest = new List<Rect>(state.Free.Count);
                foreach (var candidate in state.Free)
                    if (candidate != anchor)
                        rest.Add(candidate);
                next.Add(Child(rest, state.Used, state.Seated, state.Parent, state.Item, state.X, state.Y));
            }
            var seen = new HashSet<(long, long)>();
            beam = next.OrderByDescending(s => s.Rank)
                .Where(s => seen.Add((Selection(s.Used), (long)System.Math.Round(s.Rank * 1e6))))
                .Take(BeamWidth)
                .ToList();
        }
        foreach (var state in beam)
            Keep(plans, state);

        State Child(IReadOnlyList<Rect> rest, int[] taken, double seated, State? parent, Item? item, double x, double y) =>
            new(rest, taken, seated, seated + density * UnionArea(rest), parent, item, x, y);
    }

    private static void Keep(Dictionary<long, State> plans, State state)
    {
        if (state.Seated > 0)
            plans.TryAdd(Selection(state.Used), state);
    }

    /// <summary>FNV-1a over the copy counts: stable across processes, unlike HashCode.</summary>
    private static long Selection(int[] used)
    {
        var hash = unchecked((long)0xcbf29ce484222325);
        foreach (var count in used)
            hash = unchecked((hash ^ count) * 0x100000001b3);
        return hash;
    }

    /// <summary>Free rectangles some remaining box still fits; the rest is dead space.</summary>
    private static List<Rect> Usable(IReadOnlyList<Rect> free, IReadOnlyList<Item> items, int[] used, int[] demand)
    {
        var usable = new List<Rect>(free.Count);
        foreach (var rect in free)
            foreach (var item in items)
                if (used[item.Type] + item.Copies <= demand[item.Type]
                    && item.Width <= rect.W + MaxRectsSheet.Eps && item.Height <= rect.H + MaxRectsSheet.Eps)
                {
                    usable.Add(rect);
                    break;
                }
        return usable;
    }

    private static double UnionArea(IReadOnlyList<Rect> rects)
    {
        if (rects.Count == 0)
            return 0;
        var edges = new double[rects.Count * 2];
        for (var i = 0; i < rects.Count; i++)
        {
            edges[2 * i] = rects[i].X;
            edges[2 * i + 1] = rects[i].Right;
        }
        Array.Sort(edges);
        var spans = new List<(double Low, double High)>(rects.Count);
        var area = 0.0;
        for (var i = 1; i < edges.Length; i++)
        {
            var width = edges[i] - edges[i - 1];
            if (width <= 0)
                continue;
            spans.Clear();
            foreach (var rect in rects)
                if (rect.X <= edges[i - 1] && rect.Right >= edges[i])
                    spans.Add((rect.Y, rect.Top));
            spans.Sort();
            var reach = double.NegativeInfinity;
            var height = 0.0;
            foreach (var (low, high) in spans)
            {
                height += System.Math.Max(0, high - System.Math.Max(low, reach));
                reach = System.Math.Max(reach, high);
            }
            area += width * height;
        }
        return area;
    }

    private static List<(Item Item, double X, double Y)> Placements(State plan)
    {
        var placed = new List<(Item, double, double)>();
        for (var state = plan; state?.Item != null; state = state.Parent)
            placed.Add((state.Item, state.X, state.Y));
        placed.Reverse();
        return placed;
    }

    /// <summary>The remaining demand dropped into what the boxes left, by the frontier packer,
    /// which certifies its own poses against the frame and the boxes.</summary>
    private static IReadOnlyList<NestJobPlacement> GapFill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, List<NestJobPlacement> poses,
        Dictionary<string, JobPartGeometry> geometries, Rect rectangle, double spacing, CutoutMemo? memo,
        CancellationToken token)
    {
        var left = rectangle.W * rectangle.H - OutlineArea(poses, id => geometries[id]);
        var rest = new List<NestJobPart>();
        foreach (var part in inserts)
        {
            var quantity = part.Quantity - poses.Count(p => p.PartId == part.Id);
            if (quantity > 0 && System.Math.Abs(geometries[part.Id].Perimeter.Area()) <= left)
                rest.Add(new NestJobPart(part.Id, part.Geometry, quantity, part.Priority, part.Rotation));
        }
        if (rest.Count == 0)
            return Array.Empty<NestJobPlacement>();
        var occupied = poses.Select(p => (geometries[p.PartId], p)).ToArray();
        return CutoutPackerProposal.Fill(frame, cutoutIndex, rest, spacing, occupied, memo, token);
    }

    private static bool Clears(NestJobPlacement pose, Dictionary<string, JobPartGeometry> geometries,
        NestLayoutCheck.PreparedPlacement frame, IEnumerable<NestJobPlacement> accepted, double spacing,
        CutoutMemo? memo, CancellationToken token)
    {
        var geometry = geometries[pose.PartId];
        foreach (var other in accepted)
        {
            token.ThrowIfCancellationRequested();
            var otherGeometry = geometries[other.PartId];
            var clears = memo?.Clears(otherGeometry, other, geometry, pose, spacing)
                ?? NestLayoutCheck.Clears(otherGeometry, other, geometry, pose, spacing);
            if (!clears)
                return false;
        }
        token.ThrowIfCancellationRequested();
        return NestLayoutCheck.Clears(frame, geometry, pose);
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Direction the packing front sweeps across the sheet (the free strip is left behind it).</summary>
internal enum PackAxis
{
    /// <summary>Front moves in +X; parts settle toward low X, then low Y.</summary>
    X,

    /// <summary>Front moves in +Y; parts settle toward low Y, then low X.</summary>
    Y,
}

internal sealed record Placed(Orientation Orientation, double X, double Y)
{
    public double Left => X + Orientation.MinX;
    public double Right => X + Orientation.MaxX;
    public double Bottom => Y + Orientation.MinY;
    public double Top => Y + Orientation.MaxY;
}

internal sealed record SheetFill(NestPlateStock Stock, IReadOnlyList<Placed> Parts, double PartArea);

/// <summary>
/// Fills one sheet with a frontier-advance rule over incrementally maintained free regions.
///
/// For every (part type, orientation) still in play the packer keeps the exact set of legal
/// reference points: the inner-fit rectangle of the work area minus the no-fit polygons of
/// everything already placed. Each placement subtracts one translated NFP from each region,
/// so regions only shrink, and a region that empties is retired for the rest of the sheet.
///
/// Choice rule, applied over all types and orientations at once (not in a fixed order):
///   1. Gap fill - if any part fits without pushing the packing front forward, place the
///      largest such part at its lowest such point.
///   2. Otherwise advance - place the part whose front advance per unit area^beta is smallest,
///      i.e. the one that buys the most material coverage for the sheet length it consumes.
/// Parts are never placed in a sequence given up front; the sheet state decides what comes next.
/// </summary>
internal sealed class FrontierPacker
{
    /// <summary>Slack added around the inner-fit rectangle so zero-width fits survive Clipper;
    /// chosen points are clamped back, which moves them far less than the clearance margin.</summary>
    private const double FitSlack = 2e-4;

    private const double Tie = 1e-6;

    private readonly IReadOnlyList<PartType> types;
    private readonly NoFitCache nfps;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs;
    private readonly NestPlateStock stock;
    private readonly PackAxis axis;
    private readonly double beta;
    private readonly Box work;
    private readonly WorkCounter counter;
    private readonly BlockCatalog? blocks;

    public FrontierPacker(
        IReadOnlyList<PartType> types,
        NoFitCache nfps,
        IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs,
        NestPlateStock stock,
        PackAxis axis,
        double beta,
        WorkCounter counter,
        BlockCatalog? blocks = null
    )
    {
        this.blocks = blocks;
        this.counter = counter;
        this.types = types;
        this.nfps = nfps;
        this.pairs = pairs;
        this.stock = stock;
        this.axis = axis;
        this.beta = beta;
        work = stock.WorkArea;
    }

    public SheetFill Fill(IReadOnlyList<int> remaining, CancellationToken token)
    {
        var left = remaining.ToArray();
        var states = new List<Region>();
        var byOrientation = new Dictionary<Orientation, Region>(ReferenceEqualityComparer.Instance);
        bool Track(Orientation o, bool single)
        {
            if (byOrientation.ContainsKey(o))
                return true;
            if (!stock.Fits(o.Width, o.Height))
                return false;
            var region = new Region(o, work, single);
            states.Add(region);
            byOrientation[o] = region;
            return true;
        }
        var offered = new List<PairState>();
        foreach (var type in types)
        {
            if (left[type.Index] <= 0)
                continue;
            foreach (var o in type.Orientations)
                Track(o, single: true);
            if (left[type.Index] < 2 || !pairs.TryGetValue(type.Index, out var typePairs))
                continue;
            foreach (var pair in typePairs)
            {
                // Members missing from the catalog get regions too, but only for pair placement.
                if (stock.Fits(pair.Width, pair.Height) && Track(pair.A, single: false) && Track(pair.B, single: false))
                    offered.Add(new PairState(pair, byOrientation[pair.A], byOrientation[pair.B]));
            }
        }

        var placed = new List<Placed>();
        var blockStates = new List<BlockState>();
        var preparations = 0;
        void PrepareBlocks()
        {
            if (blocks == null || preparations++ >= 2)
                return;
            var rectangles = BlockCatalog.Rectangles(work, placed, stock.PartSpacing);
            foreach (var type in types.Where(t => left[t.Index] > 2)
                .OrderByDescending(t => t.Area * left[t.Index]).ThenBy(t => t.Index).Take(4))
                foreach (var rectangle in rectangles)
                {
                    var members = blocks.Get(type, left[type.Index], rectangle, token);
                    if (members.Count <= 2)
                        continue;
                    var width = members.Max(p => p.Right) - members.Min(p => p.Left);
                    var height = members.Max(p => p.Top) - members.Min(p => p.Bottom);
                    if (!stock.Fits(width, height))
                        continue;
                    foreach (var member in members)
                    {
                        if (byOrientation.ContainsKey(member.Orientation))
                            continue;
                        if (!Track(member.Orientation, single: false))
                            break;
                        var region = byOrientation[member.Orientation];
                        foreach (var existing in placed)
                            region.Subtract(nfps.Get(existing.Orientation, member.Orientation), existing.X, existing.Y);
                    }
                    if (members.All(p => byOrientation.ContainsKey(p.Orientation)))
                        blockStates.Add(new BlockState(members, members.Select(p => byOrientation[p.Orientation]).ToArray()));
                }
        }
        PrepareBlocks();
        var partArea = 0.0;
        var front = axis == PackAxis.X ? work.Left : work.Bottom;

        while (states.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var choice = Choose(states, offered, blockStates, front);
            if (choice == null)
                break;

            var typeIndex = choice[0].Orientation.TypeIndex;
            foreach (var part in choice)
            {
                placed.Add(part);
                partArea += types[typeIndex].Area;
                front = System.Math.Max(front, axis == PackAxis.X ? part.Right : part.Top);
            }

            left[typeIndex] -= choice.Count;
            blockStates.RemoveAll(b => b.Members.Count > left[b.Members[0].Orientation.TypeIndex]);
            if (left[typeIndex] == 0)
                states.RemoveAll(s => s.Orientation.TypeIndex == typeIndex);
            if (left[typeIndex] < 2)
            {
                offered.RemoveAll(p => p.Pose.TypeIndex == typeIndex);
                states.RemoveAll(s => s.Orientation.TypeIndex == typeIndex && !s.Single);
            }

            // Each surviving region loses the positions the new parts now block. Regions are
            // independent, so they update in parallel without affecting determinism.
            var snapshot = states.ToArray();
            foreach (var part in choice)
            {
                counter.Add(snapshot.Length);
                Parallel.For(
                    0,
                    snapshot.Length,
                    new ParallelOptions { CancellationToken = token },
                    i => snapshot[i].Subtract(nfps.Get(part.Orientation, snapshot[i].Orientation), part.X, part.Y)
                );
            }
            states.RemoveAll(s => s.IsEmpty);
            offered.RemoveAll(p => p.A.IsEmpty || p.B.IsEmpty);
            if (offered.Count == 0 && blocks == null)
                states.RemoveAll(s => !s.Single);
            blockStates.RemoveAll(b => b.Regions.Any(r => r.IsEmpty));
            PrepareBlocks();
        }

        return new SheetFill(stock, placed, partArea);
    }

    /// <summary>
    /// The next placement: one part, or both members of a pair. Singles and pairs compete
    /// under the same rule; a pair counts as one piece of twice the part area, and on a tie
    /// the single (considered first) is kept.
    /// </summary>
    private IReadOnlyList<Placed>? Choose(List<Region> states, List<PairState> offered, List<BlockState> blocks, double front)
    {
        IReadOnlyList<Placed>? bestParts = null;
        var bestFills = false;
        var bestValue = double.PositiveInfinity;
        var bestSide = double.PositiveInfinity;
        var bestLead = double.PositiveInfinity;
        var bestPriority = int.MaxValue;

        void Consider(Func<IReadOnlyList<Placed>> build, int typeIndex, double area, double advance, double side, double lead)
        {
            var priority = types[typeIndex].Part.Priority;
            if (priority > bestPriority)
                return;
            var fills = advance <= Tie;
            // Gap fill prefers bigger parts (negated area); advance prefers least advance per area.
            var value = fills ? -area : advance / System.Math.Pow(System.Math.Max(area, 1e-12), beta);

            var better = bestParts == null
                || priority < bestPriority
                || (fills && !bestFills)
                || (
                    fills == bestFills
                    && (
                        value < bestValue - Tie * System.Math.Max(1, System.Math.Abs(bestValue))
                        || (
                            value <= bestValue + Tie * System.Math.Max(1, System.Math.Abs(bestValue))
                            && (side < bestSide - Tie || (side <= bestSide + Tie && lead < bestLead - Tie))
                        )
                    )
                );
            if (!better)
                return;
            bestParts = build();
            bestPriority = priority;
            bestFills = fills;
            bestValue = value;
            bestSide = side;
            bestLead = lead;
        }

        foreach (var region in states)
        {
            if (!region.Single)
                continue;
            if (!region.TryLowest(axis, front, out var point, out var advance, out var side, out var lead))
                continue;
            var o = region.Orientation;
            Consider(() => new[] { new Placed(o, point.x, point.y) }, o.TypeIndex, types[o.TypeIndex].Area, advance, side, lead);
        }

        foreach (var state in offered)
        {
            var pair = state.Pose;
            if (!state.TryLowest(axis, front, counter, out var point, out var advance, out var side, out var lead))
                continue;
            Consider(
                () => new[] { new Placed(pair.A, point.x, point.y), new Placed(pair.B, point.x + pair.Dx, point.y + pair.Dy) },
                pair.TypeIndex,
                2 * types[pair.TypeIndex].Area,
                advance,
                side,
                lead
            );
        }

        foreach (var block in blocks)
        {
            if (!block.TryLowest(axis, front, counter, out var point, out var advance, out var side, out var lead))
                continue;
            var typeIndex = block.Members[0].Orientation.TypeIndex;
            Consider(() => block.Members.Select(p => new Placed(p.Orientation, p.X + point.x, p.Y + point.y)).ToArray(),
                typeIndex, block.Members.Count * types[typeIndex].Area, advance, side, lead);
        }
        return bestParts;
    }

    private sealed class BlockState(IReadOnlyList<Placed> members, Region[] regions)
    {
        public IReadOnlyList<Placed> Members { get; } = members;
        public Region[] Regions { get; } = regions;

        public bool TryLowest(PackAxis axis, double front, WorkCounter counter,
            out PointD point, out double advance, out double side, out double lead)
        {
            var free = Clipper.TranslatePaths(Regions[0].Free, -Members[0].X, -Members[0].Y);
            var minX = double.NegativeInfinity;
            var minY = double.NegativeInfinity;
            var maxX = double.PositiveInfinity;
            var maxY = double.PositiveInfinity;
            for (var i = 0; i < Members.Count; i++)
            {
                var member = Members[i];
                var region = Regions[i];
                minX = System.Math.Max(minX, region.MinX - member.X);
                minY = System.Math.Max(minY, region.MinY - member.Y);
                maxX = System.Math.Min(maxX, region.MaxX - member.X);
                maxY = System.Math.Min(maxY, region.MaxY - member.Y);
                if (i > 0)
                {
                    counter.Add(1);
                    free = Clipper.Intersect(free, Clipper.TranslatePaths(region.Free, -member.X, -member.Y),
                        FillRule.NonZero, NoFitCache.Precision);
                }
            }
            return BestVertex(free, minX, minY, System.Math.Max(minX, maxX), System.Math.Max(minY, maxY),
                (Members.Min(p => p.Left), Members.Min(p => p.Bottom), Members.Max(p => p.Right), Members.Max(p => p.Top)),
                axis, front, out point, out advance, out side, out lead);
        }
    }

    /// <summary>Legal reference points for one orientation on this sheet.</summary>
    private sealed class Region
    {
        private readonly double minX, minY, maxX, maxY;
        private PathsD free;
        private RectD bounds;

        public Region(Orientation orientation, Box work, bool single)
        {
            Orientation = orientation;
            Single = single;
            minX = work.Left - orientation.MinX;
            maxX = work.Right - orientation.MaxX;
            minY = work.Bottom - orientation.MinY;
            maxY = work.Top - orientation.MaxY;
            // Guard against fits that are infeasible by less than the bounds tolerance.
            if (maxX < minX)
                maxX = minX;
            if (maxY < minY)
                maxY = minY;
            free = new PathsD
            {
                new PathD
                {
                    new(minX - FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, maxY + FitSlack),
                    new(minX - FitSlack, maxY + FitSlack),
                },
            };
            bounds = Clipper.GetBounds(free);
        }

        public Orientation Orientation { get; }

        /// <summary>False for a pair-only orientation: it is never placed on its own.</summary>
        public bool Single { get; }

        public bool IsEmpty => free.Count == 0;

        public void Subtract(Nfp nfp, double dx, double dy)
        {
            if (
                nfp.Bounds.right + dx < bounds.left
                || nfp.Bounds.left + dx > bounds.right
                || nfp.Bounds.bottom + dy < bounds.top
                || nfp.Bounds.top + dy > bounds.bottom
            )
                return;
            var clip = Clipper.TranslatePaths(nfp.Region, dx, dy);
            free = Clipper.Difference(free, clip, FillRule.NonZero, NoFitCache.Precision);
            // Drop numerical dust; a sliver thinner than the precision grid is no real room.
            free.RemoveAll(p => p.Count < 3);
            bounds = free.Count == 0 ? default : Clipper.GetBounds(free);
            Version++;
        }

        /// <summary>Changes whenever the free region does.</summary>
        public int Version { get; private set; }

        public PathsD Free => free;
        public RectD Bounds => bounds;
        public double MinX => minX;
        public double MinY => minY;
        public double MaxX => maxX;
        public double MaxY => maxY;

        /// <summary>
        /// Best vertex of the free region: least front advance, then lowest cross-axis position,
        /// then lowest leading edge. Vertices suffice because every score is linear in position.
        /// </summary>
        public bool TryLowest(PackAxis axis, double front, out PointD point, out double advance, out double side, out double lead)
        {
            var o = Orientation;
            return BestVertex(free, minX, minY, maxX, maxY, (o.MinX, o.MinY, o.MaxX, o.MaxY),
                axis, front, out point, out advance, out side, out lead);
        }
    }

    /// <summary>
    /// Scores the vertices of <paramref name="free"/>, each clamped to the inner-fit box
    /// [minX, maxX] x [minY, maxY], for a piece occupying <paramref name="box"/> around its
    /// reference point.
    /// </summary>
    private static bool BestVertex(
        PathsD free,
        double minX,
        double minY,
        double maxX,
        double maxY,
        (double MinX, double MinY, double MaxX, double MaxY) box,
        PackAxis axis,
        double front,
        out PointD point,
        out double advance,
        out double side,
        out double lead
    )
    {
        point = default;
        advance = side = lead = double.PositiveInfinity;
        var found = false;
        foreach (var path in free)
            foreach (var raw in path)
            {
                var x = System.Math.Clamp(raw.x, minX, maxX);
                var y = System.Math.Clamp(raw.y, minY, maxY);
                double reach, across, start;
                if (axis == PackAxis.X)
                {
                    reach = x + box.MaxX;
                    across = y + box.MinY;
                    start = x + box.MinX;
                }
                else
                {
                    reach = y + box.MaxY;
                    across = x + box.MinX;
                    start = y + box.MinY;
                }
                var adv = System.Math.Max(0, reach - front);
                var better = !found
                    || adv < advance - Tie
                    || (adv <= advance + Tie && (across < side - Tie || (across <= side + Tie && start < lead - Tie)));
                if (!better)
                    continue;
                found = true;
                point = new PointD(x, y);
                advance = adv;
                side = across;
                lead = start;
            }
        return found;
    }

    /// <summary>
    /// Legal reference points for a pair: member A's free region intersected with member B's
    /// moved back by the pair offset. Recomputed only after either member's region changes.
    /// </summary>
    private sealed class PairState(PairPose pose, Region a, Region b)
    {
        private int versionA = -1, versionB = -1;
        private PathsD free = new();

        public PairPose Pose { get; } = pose;
        public Region A { get; } = a;
        public Region B { get; } = b;

        public bool TryLowest(PackAxis axis, double front, WorkCounter counter,
            out PointD point, out double advance, out double side, out double lead)
        {
            if (versionA != A.Version || versionB != B.Version)
            {
                versionA = A.Version;
                versionB = B.Version;
                counter.Add(1);
                free = Intersect();
            }
            // Clamp into both members' inner-fit boxes; they overlap whenever the pair fits.
            var minX = System.Math.Max(A.MinX, B.MinX - Pose.Dx);
            var maxX = System.Math.Max(minX, System.Math.Min(A.MaxX, B.MaxX - Pose.Dx));
            var minY = System.Math.Max(A.MinY, B.MinY - Pose.Dy);
            var maxY = System.Math.Max(minY, System.Math.Min(A.MaxY, B.MaxY - Pose.Dy));
            return BestVertex(free, minX, minY, maxX, maxY, (Pose.MinX, Pose.MinY, Pose.MaxX, Pose.MaxY),
                axis, front, out point, out advance, out side, out lead);
        }

        private PathsD Intersect()
        {
            if (A.IsEmpty || B.IsEmpty)
                return new PathsD();
            var a = A.Bounds;
            var b = B.Bounds;
            if (b.right - Pose.Dx < a.left || b.left - Pose.Dx > a.right
                || b.bottom - Pose.Dy < a.top || b.top - Pose.Dy > a.bottom)
                return new PathsD();
            var shifted = Clipper.TranslatePaths(B.Free, -Pose.Dx, -Pose.Dy);
            var result = Clipper.Intersect(A.Free, shifted, FillRule.NonZero, NoFitCache.Precision);
            result.RemoveAll(p => p.Count < 3);
            return result;
        }
    }
}

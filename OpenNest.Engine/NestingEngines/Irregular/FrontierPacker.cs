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

/// <summary>
/// Fills one sheet with a frontier-advance rule over incrementally maintained free regions.
///
/// For every (part type, orientation) still in play the packer keeps the exact set of legal
/// reference points: the inner-fit rectangle of the work area minus the no-fit polygons of
/// static clamps and everything already placed. Each placement subtracts one translated NFP
/// from each region, so regions only shrink; an empty region is retired for the sheet.
///
/// Choice rule, applied over all types and orientations at once (not in a fixed order):
///   1. Gap fill - if any part fits without pushing the packing front forward, place the
///      largest such part at its lowest such point.
///   2. Otherwise advance - place the part whose front advance per unit area^beta is smallest,
///      i.e. the one that buys the most material coverage for the sheet length it consumes.
/// Parts are never placed in a sequence given up front; the sheet state decides what comes next.
///
/// Given a <see cref="PackBoundary"/>, the packer fills that outline instead of the stock's
/// rectangular work area: each region starts from the orientation's inner-fit polygon of the
/// boundary less the boundary's obstacles, and everything else is unchanged.
///
/// <see cref="Fill"/> reads as its steps: regions and pairs are tracked for the demand, seated
/// parts are taken, and then <see cref="Choose"/> and <see cref="Place"/> alternate until
/// nothing more fits, with Lattice blocks offered before the first two placements.
/// </summary>
internal sealed class FrontierPacker
{
    /// <summary>Slack added around the inner-fit rectangle so zero-width fits survive Clipper;
    /// chosen points are clamped back, which moves them far less than the clearance margin.</summary>
    private const double FitSlack = 2e-4;

    /// <summary>Front advances, cross-axis positions and leading edges closer than this are the
    /// same, and so are placement values closer than this times the larger of one and the value.
    /// An advance within it is a gap fill.</summary>
    private const double Tie = 1e-6;

    /// <summary>Slack when testing that a piece fits a work area cut short.</summary>
    private const double SizeSlack = 1e-9;

    /// <summary>Least area an advance is divided by.</summary>
    private const double MinArea = 1e-12;

    /// <summary>Times one fill asks the block catalog for blocks: on the sheet as the fill finds
    /// it, and again after its first placement.</summary>
    private const int MaxBlockPreparations = 2;

    /// <summary>Types offered blocks per preparation: those with the most part area left.</summary>
    private const int MaxBlockTypes = 4;

    /// <summary>Fewest copies in a block, and so fewest left of a type offered one.</summary>
    private const int MinBlockCopies = 3;

    private readonly IReadOnlyList<PartType> types;
    private readonly NoFitCache nfps;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs;
    private readonly NestPlateStock stock;
    private readonly PackAxis axis;
    private readonly double beta;
    private readonly IReadOnlyList<double>? typeWeights;
    private readonly IReadOnlyList<int>? groupRanks;
    private readonly WorkCounter counter;
    private readonly BlockCatalog? blocks;
    private readonly ClampObstacles? clamps;
    private readonly PackBoundary? boundary;

    /// <summary>The area filled: the boundary's bounds or the stock's work area, cut short by a
    /// reach, or the box the fill is held within.</summary>
    private readonly Box work;

    /// <summary>Whether <see cref="work"/> is less than the whole area, so a piece that fits the
    /// stock may still not fit it.</summary>
    private readonly bool limited;

    /// <param name="boundary">Outline to fill instead of the stock's work area; it must be
    /// fitted to <paramref name="nfps"/>.</param>
    /// <param name="groupRanks">Per type, a rank served after priority: a type is placed only when
    /// no type of the same priority and lower rank can be, so each type packs as one group.</param>
    /// <param name="within">Part of a rectangular stock's work area to fill instead of all of it.</param>
    /// <param name="reach">Farthest the packing front may go: the work area ends there along the
    /// front axis, so the fill must fit a shorter strip of the sheet.</param>
    public FrontierPacker(
        IReadOnlyList<PartType> types,
        NoFitCache nfps,
        IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs,
        NestPlateStock stock,
        PackAxis axis,
        double beta,
        WorkCounter counter,
        BlockCatalog? blocks = null,
        ClampObstacles? clamps = null,
        PackBoundary? boundary = null,
        IReadOnlyList<double>? typeWeights = null,
        IReadOnlyList<int>? groupRanks = null,
        double? reach = null,
        Box? within = null
    )
    {
        if (boundary != null && !ReferenceEquals(boundary.Nfps, nfps))
            throw new ArgumentException("The boundary is fitted to another no-fit cache.", nameof(boundary));
        this.blocks = blocks;
        this.clamps = stock.ClampZones.Count == 0 ? null : clamps ?? new ClampObstacles(stock);
        this.counter = counter;
        this.types = types;
        this.nfps = nfps;
        this.pairs = pairs;
        this.stock = stock;
        this.axis = axis;
        this.beta = beta;
        this.typeWeights = typeWeights;
        this.groupRanks = groupRanks;
        this.boundary = boundary;
        work = WorkArea(boundary?.Bounds ?? stock.WorkArea, axis, reach, within);
        limited = reach != null || within != null;
    }

    /// <summary><paramref name="within"/> when given, else <paramref name="whole"/> ended at
    /// <paramref name="reach"/> along the front axis.</summary>
    private static Box WorkArea(Box whole, PackAxis axis, double? reach, Box? within)
    {
        var area = whole;
        if (reach is double end)
            area = axis == PackAxis.X
                ? new Box(whole.Left, whole.Bottom, System.Math.Clamp(end - whole.Left, 0, whole.Length), whole.Width)
                : new Box(whole.Left, whole.Bottom, whole.Length, System.Math.Clamp(end - whole.Bottom, 0, whole.Width));
        return within ?? area;
    }

    /// <summary>
    /// The placement rule's value of a candidate; lower wins. Gap fill prefers bigger parts
    /// (negated area); advance prefers least advance per area. A type weight above one favors
    /// the type: a more negative gap-fill value, a smaller advance score. Weight one is exact.
    /// </summary>
    internal static double PlacementValue(bool fills, double advance, double area, double beta, double weight) =>
        fills
            ? -area * weight
            : advance / System.Math.Pow(System.Math.Max(area, MinArea), beta) / weight;

    /// <summary>The edge of the work area the packing front leaves from.</summary>
    private double FrontOrigin => axis == PackAxis.X ? work.Left : work.Bottom;

    /// <summary>How far along the front axis a part reaches.</summary>
    private double Reach(Placed part) => axis == PackAxis.X ? part.Right : part.Top;

    private bool Fits(double width, double height) =>
        (boundary?.Fits(width, height) ?? stock.Fits(width, height))
        && (!limited || (width <= work.Length + SizeSlack && height <= work.Width + SizeSlack));

    /// <param name="seated">Parts already on the sheet: they block space and hold the packing
    /// front where they reached, so the fill starts as gap fill. They are not returned.</param>
    /// <param name="front">Packing front to start from instead of the work area's leading edge: at
    /// the far edge, every placement is a gap fill, the largest part that fits at its lowest point.</param>
    public SheetFill Fill(IReadOnlyList<int> remaining, CancellationToken token, IReadOnlyList<Placed>? seated = null,
        double? front = null)
    {
        var sheet = new Sheet(remaining.ToArray(), token)
        {
            Front = System.Math.Max(FrontOrigin, front ?? double.NegativeInfinity),
        };
        TrackDemand(sheet);
        if (seated is { Count: > 0 })
            Seat(sheet, seated);
        var firstNew = sheet.Placed.Count;
        DropEmpty(sheet);
        PrepareBlocks(sheet);

        while (sheet.Regions.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var choice = Choose(sheet);
            if (choice == null)
                break;
            Place(sheet, choice);
            PrepareBlocks(sheet);
        }

        var placed = sheet.Placed;
        return new SheetFill(stock, firstNew == 0 ? placed : placed.GetRange(firstNew, placed.Count - firstNew),
            sheet.PartArea);
    }

    /// <summary>One <see cref="Fill"/> call: the copies left per type, what is still in play, the
    /// parts on the sheet and how far the packing front has come.</summary>
    private sealed class Sheet(int[] left, CancellationToken token)
    {
        public int[] Left { get; } = left;
        public CancellationToken Token { get; } = token;

        /// <summary>Regions with room left for a type with copies left, in the order tracked.</summary>
        public List<Region> Regions { get; } = new();

        /// <summary>The region of every orientation tracked so far, retired or not.</summary>
        public Dictionary<Orientation, Region> ByOrientation { get; } = new(ReferenceEqualityComparer.Instance);

        public List<PairState> Pairs { get; } = new();
        public List<BlockState> Blocks { get; } = new();

        /// <summary>Times the block catalog was to be asked; see <see cref="MaxBlockPreparations"/>.</summary>
        public int BlockPreparations { get; set; }

        /// <summary>Every part on the sheet in placement order, the seated ones first.</summary>
        public List<Placed> Placed { get; } = new();

        /// <summary>Part area this fill placed; seated parts are not counted.</summary>
        public double PartArea { get; set; }

        public double Front { get; set; }
    }

    /// <summary>
    /// Starts a region for every orientation of each type with copies left, and offers each of its
    /// pairs whose members both have room when two or more copies are left.
    /// </summary>
    private void TrackDemand(Sheet sheet)
    {
        foreach (var type in types)
        {
            if (sheet.Left[type.Index] <= 0)
                continue;
            foreach (var o in type.Orientations)
                Track(sheet, o, single: true);
            if (sheet.Left[type.Index] < 2 || !pairs.TryGetValue(type.Index, out var typePairs))
                continue;
            foreach (var pair in typePairs)
            {
                // Members missing from the catalog get regions too, but only for pair placement.
                if (Fits(pair.Width, pair.Height) && Track(sheet, pair.A, single: false)
                    && Track(sheet, pair.B, single: false))
                    sheet.Pairs.Add(new PairState(pair, sheet.ByOrientation[pair.A], sheet.ByOrientation[pair.B]));
            }
        }
    }

    /// <summary>
    /// Whether the orientation has a region on this sheet, starting one when it is new to it. A
    /// region keeps the <paramref name="single"/> it was started with.
    /// </summary>
    private bool Track(Sheet sheet, Orientation o, bool single)
    {
        if (sheet.ByOrientation.ContainsKey(o))
            return true;
        if (StartRegion(o, single, sheet.Token) is not { } region)
            return false;
        sheet.Regions.Add(region);
        sheet.ByOrientation[o] = region;
        return true;
    }

    /// <summary>
    /// The orientation's region on an empty sheet: its inner fit less what the clamps and the
    /// boundary's obstacles block. Null when the orientation does not fit or has no room.
    /// </summary>
    private Region? StartRegion(Orientation o, bool single, CancellationToken token)
    {
        if (!Fits(o.Width, o.Height))
            return null;
        var region = boundary == null ? new Region(o, work, single) : new Region(o, boundary, single);
        if (region.IsEmpty)
            return null;
        if (clamps != null)
            for (var i = 0; i < clamps.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var nfp = clamps.Get(i, o);
                token.ThrowIfCancellationRequested();
                region.Subtract(nfp, 0, 0);
                counter.Add(1);
                token.ThrowIfCancellationRequested();
            }
        if (boundary != null)
            for (var i = 0; i < boundary.ObstacleCount; i++)
            {
                token.ThrowIfCancellationRequested();
                region.Subtract(nfps.Get(boundary.Obstacle(i), o), 0, 0);
                counter.Add(1);
            }
        return region;
    }

    /// <summary>Takes the parts already on the sheet: they hold the packing front where they
    /// reached, and every region loses the positions they block.</summary>
    private void Seat(Sheet sheet, IReadOnlyList<Placed> seated)
    {
        foreach (var part in seated)
        {
            sheet.Placed.Add(part);
            sheet.Front = System.Math.Max(sheet.Front, Reach(part));
        }
        Block(sheet, seated);
    }

    /// <summary>
    /// Takes the positions <paramref name="parts"/> block out of every region in play, in one
    /// pass: regions are independent, so they update in parallel without affecting determinism,
    /// and each loses the parts in order.
    /// </summary>
    private void Block(Sheet sheet, IReadOnlyList<Placed> parts)
    {
        var snapshot = sheet.Regions.ToArray();
        counter.Add((long)snapshot.Length * parts.Count);
        Parallel.For(
            0,
            snapshot.Length,
            new ParallelOptions { CancellationToken = sheet.Token },
            i =>
            {
                foreach (var part in parts)
                    snapshot[i].Block(part, nfps);
            }
        );
    }

    /// <summary>Retires the regions with no room left, and the pairs with a member that has none.</summary>
    private static void DropEmpty(Sheet sheet)
    {
        sheet.Regions.RemoveAll(s => s.IsEmpty);
        sheet.Pairs.RemoveAll(p => p.A.IsEmpty || p.B.IsEmpty);
    }

    /// <summary>
    /// Offers Lattice blocks: for each of the types with the most part area left, the block the
    /// catalog fills each free rectangle of the sheet with, when it holds copies enough and fits.
    /// Only the first <see cref="MaxBlockPreparations"/> calls of a fill ask the catalog.
    /// </summary>
    private void PrepareBlocks(Sheet sheet)
    {
        if (blocks == null || sheet.BlockPreparations++ >= MaxBlockPreparations)
            return;
        var left = sheet.Left;
        var rectangles = BlockCatalog.Rectangles(work, sheet.Placed, stock.PartSpacing, sheet.Token);
        foreach (var type in types.Where(t => left[t.Index] >= MinBlockCopies)
            .OrderByDescending(t => t.Area * left[t.Index]).ThenBy(t => t.Index).Take(MaxBlockTypes))
            foreach (var rectangle in rectangles)
            {
                var members = blocks.Get(type, left[type.Index], rectangle, sheet.Token);
                if (members.Count < MinBlockCopies)
                    continue;
                var width = members.Max(p => p.Right) - members.Min(p => p.Left);
                var height = members.Max(p => p.Top) - members.Min(p => p.Bottom);
                if (!Fits(width, height))
                    continue;
                if (TrackMembers(sheet, members))
                    sheet.Blocks.Add(new BlockState(members,
                        members.Select(p => sheet.ByOrientation[p.Orientation]).ToArray()));
            }
    }

    /// <summary>
    /// Whether every member's orientation has a region. One new to the sheet gets a region for
    /// block placement only, less what the parts already on the sheet block.
    /// </summary>
    private bool TrackMembers(Sheet sheet, IReadOnlyList<Placed> members)
    {
        foreach (var member in members)
        {
            if (sheet.ByOrientation.ContainsKey(member.Orientation))
                continue;
            if (!Track(sheet, member.Orientation, single: false))
                return false;
            var region = sheet.ByOrientation[member.Orientation];
            foreach (var existing in sheet.Placed)
                region.Block(existing, nfps);
        }
        return true;
    }

    /// <summary>
    /// Puts the chosen parts on the sheet, then retires what they end: blocks and pairs the type
    /// has too few copies left for, its regions when none is left, and whatever lost its last room
    /// to the new parts.
    /// </summary>
    private void Place(Sheet sheet, IReadOnlyList<Placed> choice)
    {
        var left = sheet.Left;
        var typeIndex = choice[0].Orientation.TypeIndex;
        foreach (var part in choice)
        {
            sheet.Placed.Add(part);
            sheet.PartArea += types[typeIndex].Area;
            sheet.Front = System.Math.Max(sheet.Front, Reach(part));
        }

        left[typeIndex] -= choice.Count;
        sheet.Blocks.RemoveAll(b => b.Members.Count > left[b.Members[0].Orientation.TypeIndex]);
        if (left[typeIndex] == 0)
            sheet.Regions.RemoveAll(s => s.Orientation.TypeIndex == typeIndex);
        if (left[typeIndex] < 2)
        {
            sheet.Pairs.RemoveAll(p => p.Pose.TypeIndex == typeIndex);
            sheet.Regions.RemoveAll(s => s.Orientation.TypeIndex == typeIndex && !s.Single);
        }

        // Each surviving region loses the positions the new parts now block, a pass per part.
        foreach (var part in choice)
            Block(sheet, new[] { part });
        DropEmpty(sheet);
        if (sheet.Pairs.Count == 0 && blocks == null)
            sheet.Regions.RemoveAll(s => !s.Single);
        sheet.Blocks.RemoveAll(b => b.Regions.Any(r => r.IsEmpty));
    }

    /// <summary>
    /// The next placement: one part, both members of a pair, or a block. All compete under the
    /// same rule; a pair or block counts as one piece of its members' area, and on a tie the one
    /// considered first is kept: singles, then pairs, then blocks.
    /// </summary>
    private IReadOnlyList<Placed>? Choose(Sheet sheet)
    {
        var leader = new Leader();
        var front = sheet.Front;

        foreach (var region in sheet.Regions)
        {
            if (!region.Single)
                continue;
            if (!region.TryLowest(axis, front, out var spot))
                continue;
            var o = region.Orientation;
            Consider(leader, spot, o.TypeIndex, types[o.TypeIndex].Area,
                () => new[] { new Placed(o, spot.Point.x, spot.Point.y) });
        }

        foreach (var state in sheet.Pairs)
        {
            var pair = state.Pose;
            if (!state.TryLowest(axis, front, counter, out var spot))
                continue;
            Consider(leader, spot, pair.TypeIndex, 2 * types[pair.TypeIndex].Area,
                () => new[]
                {
                    new Placed(pair.A, spot.Point.x, spot.Point.y),
                    new Placed(pair.B, spot.Point.x + pair.Dx, spot.Point.y + pair.Dy),
                });
        }

        foreach (var block in sheet.Blocks)
        {
            if (!block.TryLowest(axis, front, counter, out var spot))
                continue;
            var typeIndex = block.Members[0].Orientation.TypeIndex;
            Consider(leader, spot, typeIndex, block.Members.Count * types[typeIndex].Area,
                () => block.Members.Select(p => new Placed(p.Orientation, p.X + spot.Point.x, p.Y + spot.Point.y))
                    .ToArray());
        }
        return leader.Parts;
    }

    /// <summary>The candidate leading a <see cref="Choose"/> so far, and what it scored.</summary>
    private sealed class Leader
    {
        public IReadOnlyList<Placed>? Parts { get; set; }
        public int Priority { get; set; } = int.MinValue;
        public int Rank { get; set; } = int.MaxValue;
        public bool Fills { get; set; }
        public double Value { get; set; } = double.PositiveInfinity;
        public double Side { get; set; } = double.PositiveInfinity;
        public double Lead { get; set; } = double.PositiveInfinity;
    }

    /// <summary>
    /// Makes a candidate the leader when the rule prefers it: the higher priority, then the lower
    /// group rank, then a gap fill over an advance, then the lower <see cref="PlacementValue"/>,
    /// then the spot that <see cref="SettlesLower"/>. Its parts are built only when it leads.
    /// </summary>
    private void Consider(Leader leader, Spot spot, int typeIndex, double area, Func<IReadOnlyList<Placed>> build)
    {
        var priority = types[typeIndex].Part.Priority;
        var rank = groupRanks?[typeIndex] ?? 0;
        if (priority < leader.Priority || (priority == leader.Priority && rank > leader.Rank))
            return;
        var fills = spot.Advance <= Tie;
        var value = PlacementValue(fills, spot.Advance, area, beta, typeWeights?[typeIndex] ?? 1.0);

        var valueTie = Tie * System.Math.Max(1, System.Math.Abs(leader.Value));
        var better = leader.Parts == null
            || priority > leader.Priority
            || rank < leader.Rank
            || (fills && !leader.Fills)
            || (
                fills == leader.Fills
                && (
                    value < leader.Value - valueTie
                    || (value <= leader.Value + valueTie && SettlesLower(spot.Side, spot.Lead, leader.Side, leader.Lead))
                )
            );
        if (!better)
            return;
        leader.Parts = build();
        leader.Priority = priority;
        leader.Rank = rank;
        leader.Fills = fills;
        leader.Value = value;
        leader.Side = spot.Side;
        leader.Lead = spot.Lead;
    }

    /// <summary>Whether a spot lies lower across the front axis than the best, or level with it
    /// and with a lower leading edge.</summary>
    private static bool SettlesLower(double side, double lead, double bestSide, double bestLead) =>
        side < bestSide - Tie || (side <= bestSide + Tie && lead < bestLead - Tie);

    /// <summary>A reference point for a piece and what it scores there: how far it pushes the
    /// packing front, its low edge across the front axis and its leading edge along it.</summary>
    private readonly record struct Spot(PointD Point, double Advance, double Side, double Lead);

    /// <summary>A box by its edges: the reference points a piece may take, or the piece's own
    /// extent around its reference point.</summary>
    private readonly record struct Extents(double MinX, double MinY, double MaxX, double MaxY);

    /// <summary>
    /// The best vertex of <paramref name="free"/> for a piece occupying <paramref name="piece"/>
    /// around its reference point, each vertex clamped to the inner-fit box <paramref name="fit"/>:
    /// least front advance, then the spot that <see cref="SettlesLower"/>. Vertices suffice
    /// because every score is linear in position.
    /// </summary>
    private static bool BestVertex(PathsD free, Extents fit, Extents piece, PackAxis axis, double front, out Spot best)
    {
        var point = default(PointD);
        var advance = double.PositiveInfinity;
        var side = double.PositiveInfinity;
        var lead = double.PositiveInfinity;
        var found = false;
        foreach (var path in free)
            foreach (var raw in path)
            {
                var x = System.Math.Clamp(raw.x, fit.MinX, fit.MaxX);
                var y = System.Math.Clamp(raw.y, fit.MinY, fit.MaxY);
                double reach, across, start;
                if (axis == PackAxis.X)
                {
                    reach = x + piece.MaxX;
                    across = y + piece.MinY;
                    start = x + piece.MinX;
                }
                else
                {
                    reach = y + piece.MaxY;
                    across = x + piece.MinX;
                    start = y + piece.MinY;
                }
                var adv = System.Math.Max(0, reach - front);
                var better = !found
                    || adv < advance - Tie
                    || (adv <= advance + Tie && SettlesLower(across, start, side, lead));
                if (!better)
                    continue;
                found = true;
                point = new PointD(x, y);
                advance = adv;
                side = across;
                lead = start;
            }
        best = new Spot(point, advance, side, lead);
        return found;
    }

    /// <summary>Legal reference points for one orientation on this sheet.</summary>
    private sealed class Region
    {
        private readonly double minX, minY, maxX, maxY;
        private PathsD free;
        private RectD bounds;

        public Region(Orientation orientation, Box work, bool single)
            : this(orientation, work, single, null) { }

        /// <summary>Starts from the orientation's inner fit of the boundary. No fit slack: the
        /// inner fit already lies within the bounds box, and an exact fit against a cutout wall
        /// would fail the clearance check anyway.</summary>
        public Region(Orientation orientation, PackBoundary boundary, bool single)
            : this(orientation, boundary.Bounds, single, boundary.InnerFit(orientation)) { }

        private Region(Orientation orientation, Box work, bool single, PathsD? seed)
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
            free = seed ?? new PathsD
            {
                new PathD
                {
                    new(minX - FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, maxY + FitSlack),
                    new(minX - FitSlack, maxY + FitSlack),
                },
            };
            free.RemoveAll(p => p.Count < 3);
            bounds = free.Count == 0 ? default : Clipper.GetBounds(free);
        }

        public Orientation Orientation { get; }

        /// <summary>False for a pair-only orientation: it is never placed on its own.</summary>
        public bool Single { get; }

        public bool IsEmpty => free.Count == 0;

        /// <summary>Takes out the positions at which this orientation would meet <paramref name="part"/>.</summary>
        public void Block(Placed part, NoFitCache nfps) =>
            Subtract(nfps.Get(part.Orientation, Orientation), part.X, part.Y);

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

        /// <summary>The <see cref="BestVertex"/> of the free region.</summary>
        public bool TryLowest(PackAxis axis, double front, out Spot spot)
        {
            var o = Orientation;
            return BestVertex(free, new Extents(minX, minY, maxX, maxY), new Extents(o.MinX, o.MinY, o.MaxX, o.MaxY),
                axis, front, out spot);
        }
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

        public bool TryLowest(PackAxis axis, double front, WorkCounter counter, out Spot spot)
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
            return BestVertex(free, new Extents(minX, minY, maxX, maxY),
                new Extents(Pose.MinX, Pose.MinY, Pose.MaxX, Pose.MaxY), axis, front, out spot);
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

    /// <summary>
    /// Legal reference points for a block: every member's free region moved back by the member's
    /// place in the block, intersected. Recomputed for every choice.
    /// </summary>
    private sealed class BlockState(IReadOnlyList<Placed> members, Region[] regions)
    {
        public IReadOnlyList<Placed> Members { get; } = members;

        /// <summary>Each member's region, in member order.</summary>
        public Region[] Regions { get; } = regions;

        public bool TryLowest(PackAxis axis, double front, WorkCounter counter, out Spot spot)
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
            return BestVertex(free, new Extents(minX, minY, System.Math.Max(minX, maxX), System.Math.Max(minY, maxY)),
                new Extents(Members.Min(p => p.Left), Members.Min(p => p.Bottom), Members.Max(p => p.Right),
                    Members.Max(p => p.Top)),
                axis, front, out spot);
        }
    }
}

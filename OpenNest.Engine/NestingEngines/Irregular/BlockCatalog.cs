#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Per-solve, per-spacing Fill proposals: a type's Lattice pattern in a rectangle, as a block for
/// the frontier packer (<see cref="Get"/>) or as a band (<see cref="Band"/>). Only the members
/// occupy material.
///
/// Every proposal is a whole Fill of its own, so the catalog keeps what it built, charges it to
/// the solve's work meter and limits the blocks a type may ask for. Later decisions read that
/// meter: when a proposal is charged, and how much, is part of the engine's result.
/// </summary>
internal sealed class BlockCatalog : IDisposable
{
    /// <summary>Fewest copies in a block or band; a Fill of fewer proposes nothing.</summary>
    private const int MinCopies = 3;

    /// <summary>Most blocks <see cref="Get"/> prepares per type; bands are not limited.</summary>
    private const int MaxPreparationsPerType = 8;

    /// <summary>Free rectangles of a sheet that blocks are offered: its largest.</summary>
    private const int MaxRectangles = 2;

    /// <summary>Clearance kept from occupied material beyond its spacing and flattening tolerance.</summary>
    private const double OccupiedMargin = 0.001;

    /// <summary>Miter limit of the offset that grows occupied material by its clearance.</summary>
    private const double MiterLimit = 2;

    /// <summary>Margin for Fill's boundary rounding in a <see cref="Footprint"/>.</summary>
    private const double FillRoundingMargin = 0.001;

    /// <summary>Relative slack on a rectangle's area capacity, for Fill's boundary rounding.</summary>
    private const double CapacitySlack = 0.01;

    /// <summary>Decimals a certified pose's position is rounded to.</summary>
    private const int PositionDecimals = 8;

    /// <summary>Decimals a certified pose's rotation, in radians, is rounded to.</summary>
    private const int RotationDecimals = 10;

    private readonly double spacing;

    /// <summary>The spacing a band is filled at: one grid unit wider; see <see cref="Band"/>.</summary>
    private readonly double bandSpacing;

    private readonly WorkCounter counter;
    private readonly long budget;
    private readonly Func<NestJobPart, Drawing>? drawingFor;

    /// <summary>The catalog's own drawing per type, made only without <see cref="drawingFor"/>.</summary>
    private readonly Dictionary<int, Drawing> drawings = new();

    /// <summary>Per type, the orientations members are bound to: the type's own, its pairs' and
    /// those its proposals added.</summary>
    private readonly Dictionary<int, List<Orientation>> orientations = new();

    private readonly Dictionary<int, Footprint> footprints = new();

    /// <summary>Per type, its material area (holes excluded), which no two copies can share.</summary>
    private readonly Dictionary<int, double> materialAreas = new();

    /// <summary>Per type, the blocks <see cref="Get"/> has prepared.</summary>
    private readonly Dictionary<int, int> preparations = new();

    private readonly Dictionary<PatternKey, IReadOnlyList<Placed>> blocks = new();

    /// <summary>
    /// While set, <see cref="Get"/> returns only blocks already kept and prepares none, so a pass
    /// metered apart leaves the catalog, and every decision read from it later, as it found it.
    /// </summary>
    public bool Frozen { get; set; }

    /// <summary>
    /// Overlap verdicts every Fill proposal of this catalog shares: the same part is filled in
    /// the same box for one quantity after another, testing the same outlines each time.
    /// </summary>
    private readonly OutlineOverlapMemo overlapMemo = new();

    /// <summary>Guarded by its own lock, as are its entries' members and charge.</summary>
    private readonly Dictionary<PatternKey, BandEntry> bands = new();

    /// <summary>What a pattern is kept under: its type, the copies wanted and the size of its box.</summary>
    private readonly record struct PatternKey(int Type, int Quantity, double Length, double Width);

    /// <summary>A pattern asked for: up to <c>Quantity</c> copies of the type in a box the size of
    /// the rectangle.</summary>
    private readonly record struct Request(PartType Type, int Quantity, Box Rectangle)
    {
        public PatternKey Key => new(Type.Index, Quantity, Rectangle.Length, Rectangle.Width);

        /// <summary>Whether nothing can come of it: too few copies wanted, a type with no outline or
        /// a box with no area.</summary>
        public bool IsEmpty =>
            Quantity < MinCopies || Type.Orientations.Count == 0 || Rectangle.Length <= 0 || Rectangle.Width <= 0;

        /// <summary>
        /// Work charged for one Fill proposal. The Fill is a full independent NFP-based pack whose
        /// own operations never touch the solve's effort meter; this charges it by the factors that
        /// drive its real cost, demand and outline complexity, so the shared budget sees it.
        /// </summary>
        public long Cost
        {
            get
            {
                var vertices = Type.Orientations.Count > 0 ? Type.Orientations[0].Outline.Count : 1;
                return (long)Quantity * Quantity * System.Math.Max(1, vertices);
            }
        }
    }

    /// <summary>
    /// Lower bounds on the true part's width at any rotation and on its area: the outline's
    /// convex-hull width and area, less what flattening within the outline's tolerance (and a
    /// margin for Fill's boundary rounding) could hide.
    /// </summary>
    private readonly record struct Footprint(double Width, double Area)
    {
        public static Footprint Of(Orientation orientation)
        {
            var outline = orientation.Outline;
            var width = HullWidth(outline);
            var perimeter = Perimeter(outline);
            return new(double.IsFinite(width) ? width - 2 * orientation.Tolerance - FillRoundingMargin : 0,
                System.Math.Abs(Clipper.Area(outline)) - perimeter * (orientation.Tolerance + FillRoundingMargin));
        }

        /// <summary>The convex hull's least extent across any of its edges; infinite for a hull with
        /// no edge.</summary>
        private static double HullWidth(PathD outline)
        {
            var hull = ConvexHull.Compute(outline.Select(p => new Vector(p.x, p.y)).ToList()).Vertices;
            var width = double.PositiveInfinity;
            for (var i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var length = a.DistanceTo(b);
                if (length <= Tolerance.Epsilon)
                    continue;
                var across = hull.Max(p =>
                    System.Math.Abs((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) / length);
                width = System.Math.Min(width, across);
            }
            return width;
        }

        private static double Perimeter(PathD outline)
        {
            var perimeter = 0.0;
            for (var i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                perimeter += System.Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            }
            return perimeter;
        }
    }

    /// <summary>
    /// A built band pattern: its members, or while only prefetched, its certified poses awaiting
    /// orientations; no poses is an empty pattern. The work it cost is charged on its first
    /// ordinary request.
    /// </summary>
    private sealed class BandEntry(NestJobPlacement[]? poses, long cost, double rejected)
    {
        /// <summary>X extent of a fill the certification rejected; zero when none was.</summary>
        public double Rejected { get; } = rejected;
        public IReadOnlyList<Placed>? Members { get; set; } = poses == null ? Array.Empty<Placed>() : null;
        public NestJobPlacement[]? Poses { get; } = poses;
        public long Cost { get; } = cost;
        public bool Charged { get; set; }
    }

    /// <param name="drawingFor">Shared private drawings whose best-fit entries the caller owns;
    /// null makes the catalog own one per type and release it on dispose.</param>
    public BlockCatalog(double spacing, IReadOnlyList<PartType> types,
        IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs, WorkCounter counter, long budget,
        Func<NestJobPart, Drawing>? drawingFor = null)
    {
        this.spacing = spacing;
        bandSpacing = spacing + System.Math.Pow(10, -NoFitCache.Precision);
        this.counter = counter;
        this.budget = budget;
        this.drawingFor = drawingFor;
        foreach (var type in types)
        {
            orientations[type.Index] = WithPairOrientations(type, pairs);
            if (type.Orientations.Count > 0)
                footprints[type.Index] = Footprint.Of(type.Orientations[0]);
            if (JobPartGeometry.TryRead(type.Part.Geometry)?.MaterialArea is double area && area > 0
                && double.IsFinite(area))
                materialAreas[type.Index] = area;
        }
    }

    /// <summary>The type's orientations, then those of its pairs that are not among them.</summary>
    private static List<Orientation> WithPairOrientations(PartType type,
        IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs)
    {
        var poses = type.Orientations.ToList();
        if (pairs.TryGetValue(type.Index, out var found))
            poses.AddRange(found.SelectMany(p => new[] { p.A, p.B }));
        return poses.Distinct().ToList();
    }

    /// <summary>
    /// The request for up to <paramref name="quantity"/> copies in the rectangle, the copies held to
    /// as many as the rectangle's area could hold of the type's material. A Fill stops at the copies
    /// asked only when it could place more, so asking for more than can fit proposes the same
    /// pattern; capped, one pattern serves every sheet that wants at least that many, rather than
    /// a new Fill, charged by the square of the copies, for each remaining demand.
    /// </summary>
    private Request RequestFor(PartType type, int quantity, Box rectangle)
    {
        if (materialAreas.TryGetValue(type.Index, out var area) && rectangle.Length > 0 && rectangle.Width > 0)
        {
            var capacity = rectangle.Area() * (1 + CapacitySlack) / area + 1;
            if (capacity < quantity)
                quantity = (int)capacity;
        }
        return new Request(type, quantity, rectangle);
    }

    /// <summary>Blocks prepared so far, over all types.</summary>
    internal int PreparationCount => preparations.Values.Sum();

    /// <summary>
    /// The free rectangles of a work area blocks are offered: the largest clear of the parts
    /// placed, their spacing included. Only the largest are searched for: on a sheet of many
    /// detailed parts, sorting out every maximal rectangle takes minutes.
    /// </summary>
    internal static IReadOnlyList<Box> Rectangles(Box work, IReadOnlyList<Placed> placed, double spacing,
        CancellationToken token = default) =>
        MaximalRectangles.InRegion(FreeRegion(work, placed, spacing, token), largest: MaxRectangles, token: token)
            .OrderByDescending(b => b.Area())
            .ThenBy(b => b.Left).ThenBy(b => b.Bottom).ThenBy(b => b.Length).Take(MaxRectangles).ToArray();

    private static PathsD FreeRegion(Box work, IReadOnlyList<Placed> placed, double spacing, CancellationToken token)
    {
        var free = new PathsD { new PathD
        {
            new(work.Left, work.Bottom), new(work.Right, work.Bottom),
            new(work.Right, work.Top), new(work.Left, work.Top),
        } };
        foreach (var part in placed)
        {
            token.ThrowIfCancellationRequested();
            // Physical occupied material, not a reference-point region for any moving part.
            // The catalog currently prepares solid outlines; future profile preparation owns holes.
            var blocked = Clipper.InflatePaths(new PathsD { part.Orientation.Outline },
                spacing + part.Orientation.Tolerance + OccupiedMargin, JoinType.Miter, EndType.Polygon,
                MiterLimit, NoFitCache.Precision);
            free = Clipper.Difference(free, Clipper.TranslatePaths(blocked, part.X, part.Y),
                FillRule.NonZero, NoFitCache.Precision);
        }
        return free;
    }

    /// <summary>
    /// The type's Lattice pattern in the rectangle, up to <paramref name="quantity"/> copies, as a
    /// block: kept per type, quantity and rectangle size once prepared. Empty when no block comes
    /// of it, or when the type's preparations or the solve's work budget are spent, or when the
    /// catalog is <see cref="Frozen"/> and has not kept it.
    /// </summary>
    public IReadOnlyList<Placed> Get(PartType type, int quantity, Box rectangle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = RequestFor(type, quantity, rectangle);
        if (request.IsEmpty)
            return Array.Empty<Placed>();
        var key = request.Key;
        if (blocks.TryGetValue(key, out var kept))
            return kept;
        if (Frozen || CannotPrepare(request))
            return Array.Empty<Placed>();
        preparations[type.Index] = PreparationsOf(type) + 1;
        var block = Prepare(request, token);
        blocks[key] = block;
        return block;
    }

    /// <summary>
    /// Whether no block is prepared for the request: its rectangle is smaller than three parts,
    /// or the type's preparations or the solve's work budget are spent.
    /// </summary>
    private bool CannotPrepare(Request request) =>
        request.Rectangle.Area() < MinCopies * request.Type.Area
        || PreparationsOf(request.Type) >= MaxPreparationsPerType
        || counter.Value >= budget;

    private int PreparationsOf(PartType type) => preparations.GetValueOrDefault(type.Index);

    /// <summary>
    /// Charges a block's proposal and makes it. A rectangle that cannot hold a block is charged
    /// and counted as if filled, so the meter, and every later decision it steers, reads the same
    /// as when the Fill ran.
    /// </summary>
    private IReadOnlyList<Placed> Prepare(Request request, CancellationToken token)
    {
        counter.Add(request.Cost);
        if (CannotHoldBlock(request))
            return Array.Empty<Placed>();
        var type = request.Type;
        var poses = Propose(request, spacing, DrawingOf(type), token, out _);
        return poses == null ? Array.Empty<Placed>() : Bind(type, poses, orientations[type.Index], token);
    }

    /// <summary>
    /// True when the rectangle cannot hold three copies of the type: it is narrower than the part
    /// at every rotation, or smaller than three parts. Fill could return at most two members there,
    /// which never form a block, so its proposal is known to be empty without running it.
    /// </summary>
    private bool CannotHoldBlock(Request request)
    {
        var rectangle = request.Rectangle;
        return footprints.TryGetValue(request.Type.Index, out var footprint)
            && (System.Math.Min(rectangle.Length, rectangle.Width) < footprint.Width
                || rectangle.Area() < MinCopies * footprint.Area);
    }

    /// <summary>
    /// The type's Lattice pattern in a box of the given size, up to <paramref name="quantity"/>
    /// copies, for a band; unlike <see cref="Get"/> it is not limited in attempts per type. The
    /// pattern is filled one grid unit wider than the spacing: Fill sets parts exactly the spacing
    /// apart, which the stricter certification can reject for curved parts (a stack of circles),
    /// and losing the band then loses its densest pattern.
    ///
    /// With a <paramref name="prefetch"/> drawing (see <see cref="PrefetchDrawing"/>) the pattern
    /// is built ahead of need, which may run in parallel with other prefetches, even of the same
    /// type, each with its own drawing. A prefetch charges no work and adds no orientations: the
    /// pattern's first ordinary request charges it and binds its poses to the catalog's
    /// orientations, so the work meter and the orientations read as if the pattern were built then.
    /// </summary>
    public IReadOnlyList<Placed> Band(PartType type, int quantity, Box rectangle, CancellationToken token,
        Drawing? prefetch = null)
    {
        token.ThrowIfCancellationRequested();
        var request = RequestFor(type, quantity, rectangle);
        if (request.IsEmpty)
            return Array.Empty<Placed>();
        var entry = BandFor(request, token, prefetch);
        if (prefetch != null)
            return entry.Members ?? Bind(type, entry.Poses!, orientations[type.Index].ToList(), token);
        lock (bands)
        {
            // The first ordinary request pays for the pattern, whichever request built it.
            if (!entry.Charged)
            {
                counter.Add(entry.Cost);
                entry.Charged = true;
            }
            if (entry.Members != null)
                return entry.Members;
        }
        // Ordinary requests of one type run on one thread at a time.
        var bound = Bind(type, entry.Poses!, orientations[type.Index], token);
        lock (bands)
            entry.Members = bound;
        return bound;
    }

    /// <summary>
    /// X extent of the fill a <see cref="Band"/> request already made got back and its
    /// certification rejected (Fill can set copies of neighboring columns closer than the
    /// spacing); zero when the request is unknown or nothing was rejected. A band search asks
    /// again just short of it, where Fill must choose another pattern.
    /// </summary>
    public double RejectedBandLength(PartType type, int quantity, Box rectangle)
    {
        lock (bands)
            return bands.TryGetValue(RequestFor(type, quantity, rectangle).Key, out var entry) ? entry.Rejected : 0;
    }

    /// <summary>The band's entry, its pattern proposed here when the catalog has none; charges
    /// nothing and binds nothing.</summary>
    private BandEntry BandFor(Request request, CancellationToken token, Drawing? prefetch)
    {
        var key = request.Key;
        BandEntry? entry;
        lock (bands)
            bands.TryGetValue(key, out entry);
        if (entry != null)
            return entry;
        // Types may build bands in parallel: each type's drawing and orientation list are its
        // own. Prefetches of one type fill their own drawings and only read the orientations.
        var rejected = 0.0;
        var poses = CannotHoldBlock(request)
            ? null
            : Propose(request, bandSpacing, prefetch ?? DrawingOf(request.Type), token, out rejected);
        lock (bands)
        {
            if (!bands.TryGetValue(key, out entry))
                bands[key] = entry = new BandEntry(poses, request.Cost, rejected);
            return entry;
        }
    }

    /// <summary>
    /// A private drawing of the type for one prefetching thread: Fill sorts a drawing's cached
    /// best-fit list in place, so two fills of one drawing must not run at once. It draws the same
    /// best-fit candidates from the geometry pool; release it with <see cref="ReleasePrefetch"/>.
    /// </summary>
    public static Drawing PrefetchDrawing(PartType type) => DrawingJobMapper.CreateDrawing(type.Part);

    public static void ReleasePrefetch(Drawing drawing) => BestFitCache.Invalidate(drawing);

    private Drawing DrawingOf(PartType type) => drawingFor?.Invoke(type.Part) ?? OwnDrawing(type);

    private Drawing OwnDrawing(PartType type)
    {
        lock (drawings)
        {
            if (!drawings.TryGetValue(type.Index, out var drawing))
                drawings[type.Index] = drawing = DrawingJobMapper.CreateDrawing(type.Part);
            return drawing;
        }
    }

    public void Dispose()
    {
        foreach (var drawing in drawings.Values)
            BestFitCache.Invalidate(drawing);
        drawings.Clear();
    }

    /// <summary>
    /// The certified poses of the request's Fill of <paramref name="drawing"/> at
    /// <paramref name="fillSpacing"/>; null when Fill fails or certification rejects it, then
    /// with the X extent of the rejected fill in <paramref name="rejected"/> (zero otherwise).
    /// </summary>
    private NestJobPlacement[]? Propose(Request request, double fillSpacing, Drawing drawing, CancellationToken token,
        out double rejected)
    {
        rejected = 0;
        var (type, quantity, rectangle) = request;
        try
        {
            var members = PrivatePlateFill.Run(drawing, type.Part.Rotation, fillSpacing,
                rectangle.Length, rectangle.Width, quantity, token, overlapMemo: overlapMemo);
            token.ThrowIfCancellationRequested();
            var kept = members.Take(quantity).ToArray();
            var poses = Certify(type, kept, spacing, token);
            if (poses == null && kept.Length >= MinCopies)
            {
                var extent = kept.Max(p => p.BoundingBox.Right) - kept.Min(p => p.BoundingBox.Left);
                rejected = double.IsFinite(extent) ? System.Math.Max(0, extent) : 0;
            }
            return poses;
        }
        catch (Exception ex) when (IsProposalFailure(ex))
        {
            return null;
        }
    }

    /// <summary>Whether the exception is a proposal that failed rather than a solve that must stop.</summary>
    private static bool IsProposalFailure(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;

    /// <summary>The members certified and bound to <paramref name="orientations"/>; empty when they
    /// cannot form a block.</summary>
    internal static IReadOnlyList<Placed> Resolve(PartType type, IReadOnlyList<Part> members,
        List<Orientation> orientations, double spacing, CancellationToken token = default)
    {
        var poses = Certify(type, members, spacing, token);
        return poses == null ? Array.Empty<Placed>() : Bind(type, poses, orientations, token);
    }

    /// <summary>
    /// The members' poses, quantized and ordered, when every pair keeps the spacing; null when
    /// they cannot form a block.
    /// </summary>
    private static NestJobPlacement[]? Certify(PartType type, IReadOnlyList<Part> members, double spacing,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (members.Count < MinCopies)
            return null;
        var geometry = JobPartGeometry.TryRead(type.Part.Geometry);
        if (geometry == null)
            return null;
        var poses = QuantizedPoses(type, members);
        if (poses.Any(p => !IsAllowed(type, p)))
            return null;
        return KeepSpacing(geometry, poses, spacing, token) ? poses : null;
    }

    /// <summary>
    /// The members' poses in a fixed order. Canonical rebinding is already performed by FillItem;
    /// quantization removes sub-grid arithmetic differences from equivalent Fill proposals, and
    /// the poses certified are the quantized ones.
    /// </summary>
    private static NestJobPlacement[] QuantizedPoses(PartType type, IReadOnlyList<Part> members) =>
        members.Select(p => new NestJobPlacement(type.Part.Id, 0,
            System.Math.Round(p.Location.X, PositionDecimals), System.Math.Round(p.Location.Y, PositionDecimals),
            System.Math.Round(Angle.NormalizeRad(p.Rotation), RotationDecimals)))
            .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Rotation).ToArray();

    private static bool IsAllowed(PartType type, NestJobPlacement pose) =>
        double.IsFinite(pose.X) && double.IsFinite(pose.Y) && double.IsFinite(pose.Rotation)
        && type.Part.Rotation.Allows(pose.Rotation);

    /// <summary>Whether every pair of the poses keeps the spacing.</summary>
    private static bool KeepSpacing(JobPartGeometry geometry, NestJobPlacement[] poses, double spacing,
        CancellationToken token)
    {
        // Each member outlined once, not once per pair it is tested in.
        var prepared = poses.Select(p => NestLayoutCheck.Prepare(geometry, p, spacing)).ToArray();
        for (var i = 0; i < poses.Length; i++)
            for (var j = i + 1; j < poses.Length; j++)
            {
                token.ThrowIfCancellationRequested();
                if (!NestLayoutCheck.Clears(prepared[i], prepared[j]))
                    return false;
            }
        return true;
    }

    /// <summary>
    /// The certified poses as members relative to the first, each at the orientation of its
    /// rotation, which is added to <paramref name="orientations"/> when missing; empty when one
    /// cannot be made.
    /// </summary>
    private static IReadOnlyList<Placed> Bind(PartType type, NestJobPlacement[] poses, List<Orientation> orientations,
        CancellationToken token)
    {
        try
        {
            var result = new List<Placed>();
            foreach (var pose in poses)
            {
                token.ThrowIfCancellationRequested();
                var orientation = OrientationAt(type, orientations, pose.Rotation);
                if (orientation == null)
                    return Array.Empty<Placed>();
                result.Add(new Placed(orientation, pose.X - poses[0].X, pose.Y - poses[0].Y));
            }
            return result;
        }
        catch (Exception ex) when (IsProposalFailure(ex))
        {
            return Array.Empty<Placed>();
        }
    }

    /// <summary>The orientation at the rotation, made and added to <paramref name="orientations"/>
    /// when missing; null when it cannot be made.</summary>
    private static Orientation? OrientationAt(PartType type, List<Orientation> orientations, double rotation)
    {
        var orientation = orientations.FirstOrDefault(o => o.Rotation == rotation);
        if (orientation != null)
            return orientation;
        orientation = PartCatalog.CreateOrientation(type, orientations.Max(o => o.Index) + 1, rotation);
        if (orientation != null)
            orientations.Add(orientation);
        return orientation;
    }
}

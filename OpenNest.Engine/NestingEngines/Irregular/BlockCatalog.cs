#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Per-solve, per-spacing Fill proposals. Only the members occupy material.</summary>
internal sealed class BlockCatalog : IDisposable
{
    private readonly double spacing;
    private readonly WorkCounter counter;
    private readonly long budget;
    private readonly Dictionary<int, Drawing> drawings = new();
    private readonly Func<NestJobPart, Drawing>? drawingFor;
    private readonly Dictionary<int, List<Orientation>> orientations = new();
    private readonly Dictionary<int, (double Width, double Area)> footprints = new();
    private readonly Dictionary<int, int> attempts = new();
    internal int PreparationCount => attempts.Values.Sum();
    private readonly Dictionary<(int Type, int Quantity, double Length, double Width), IReadOnlyList<Placed>> cache = new();
    private readonly Dictionary<(int Type, int Quantity, double Length, double Width), BandEntry> bands = new();

    /// <summary>
    /// A built band pattern: its members, or while only prefetched, its certified poses awaiting
    /// orientations. The work it cost is charged on its first ordinary request.
    /// </summary>
    private sealed class BandEntry(IReadOnlyList<Placed>? members, NestJobPlacement[]? poses, long cost)
    {
        public IReadOnlyList<Placed>? Members { get; set; } = members;
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
        this.counter = counter;
        this.budget = budget;
        this.drawingFor = drawingFor;
        foreach (var type in types)
        {
            var poses = type.Orientations.ToList();
            if (pairs.TryGetValue(type.Index, out var found))
                poses.AddRange(found.SelectMany(p => new[] { p.A, p.B }));
            orientations[type.Index] = poses.Distinct().ToList();
            if (type.Orientations.Count > 0)
                footprints[type.Index] = Footprint(type.Orientations[0]);
        }
    }

    /// <summary>
    /// Lower bounds on the true part's width at any rotation and on its area: the outline's
    /// convex-hull width and area, less what flattening within the outline's tolerance (and a
    /// margin for Fill's boundary rounding) could hide.
    /// </summary>
    private static (double Width, double Area) Footprint(Orientation orientation)
    {
        var outline = orientation.Outline;
        var hull = ConvexHull.Compute(outline.Select(p => new Vector(p.x, p.y)).ToList()).Vertices;
        var width = double.PositiveInfinity;
        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var length = a.DistanceTo(b);
            if (length <= Tolerance.Epsilon)
                continue;
            var across = hull.Max(p => System.Math.Abs((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) / length);
            width = System.Math.Min(width, across);
        }
        var perimeter = 0.0;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            perimeter += System.Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
        }
        const double margin = 0.001;
        return (double.IsFinite(width) ? width - 2 * orientation.Tolerance - margin : 0,
            System.Math.Abs(Clipper.Area(outline)) - perimeter * (orientation.Tolerance + margin));
    }

    /// <summary>
    /// True when the rectangle cannot hold three copies of the type: it is narrower than the part
    /// at every rotation, or smaller than three parts. Fill could return at most two members there,
    /// which never form a block, so its proposal is known to be empty without running it.
    /// </summary>
    private bool CannotHoldBlock(PartType type, Box rectangle) =>
        footprints.TryGetValue(type.Index, out var footprint)
        && (System.Math.Min(rectangle.Length, rectangle.Width) < footprint.Width
            || rectangle.Area() < 3 * footprint.Area);

    internal static IReadOnlyList<Box> Rectangles(Box work, IReadOnlyList<Placed> placed, double spacing)
    {
        var free = new PathsD { new PathD
        {
            new(work.Left, work.Bottom), new(work.Right, work.Bottom),
            new(work.Right, work.Top), new(work.Left, work.Top),
        } };
        foreach (var part in placed)
        {
            // Physical occupied material, not a reference-point region for any moving part.
            // The catalog currently prepares solid outlines; future profile preparation owns holes.
            var blocked = Clipper.InflatePaths(new PathsD { part.Orientation.Outline },
                spacing + part.Orientation.Tolerance + 0.001, JoinType.Miter, EndType.Polygon,
                2, NoFitCache.Precision);
            free = Clipper.Difference(free, Clipper.TranslatePaths(blocked, part.X, part.Y),
                FillRule.NonZero, NoFitCache.Precision);
        }
        return MaximalRectangles.InRegion(free).OrderByDescending(b => b.Area())
            .ThenBy(b => b.Left).ThenBy(b => b.Bottom).ThenBy(b => b.Length).Take(2).ToArray();
    }

    public IReadOnlyList<Placed> Get(PartType type, int quantity, Box rectangle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (quantity <= 2 || type.Orientations.Count == 0 || rectangle.Length <= 0 || rectangle.Width <= 0)
            return Array.Empty<Placed>();
        var key = (type.Index, quantity, rectangle.Length, rectangle.Width);
        if (cache.TryGetValue(key, out var cached))
            return cached;
        if (rectangle.Area() < 3 * type.Area || attempts.GetValueOrDefault(type.Index) >= 8 || counter.Value >= budget)
            return Array.Empty<Placed>();
        attempts[type.Index] = attempts.GetValueOrDefault(type.Index) + 1;
        var result = Build(type, quantity, rectangle, token);
        cache[key] = result;
        return result;
    }

    /// <summary>
    /// The type's Lattice pattern in a box of the given size, up to <paramref name="quantity"/>
    /// copies, for a band; unlike <see cref="Get"/> it is not limited in attempts per type. The
    /// pattern is filled one grid unit wider than the spacing: Fill sets parts exactly the spacing
    /// apart, which the stricter certification below can reject for curved parts (a stack of
    /// circles), and losing the band then loses its densest pattern.
    /// </summary>
    public IReadOnlyList<Placed> Band(PartType type, int quantity, Box rectangle, CancellationToken token) =>
        Band(type, quantity, rectangle, token, prefetch: null);

    /// <summary>
    /// <see cref="Band(PartType, int, Box, CancellationToken)"/>, or with a <paramref name="prefetch"/>
    /// drawing (see <see cref="PrefetchDrawing"/>) a build ahead of need that may run in parallel
    /// with other prefetches, even of the same type, each with its own drawing. A prefetch charges
    /// no work and adds no orientations: the pattern's first ordinary request charges it and binds
    /// its poses to the catalog's orientations, so the work meter and the orientations read as if
    /// the pattern were built then.
    /// </summary>
    public IReadOnlyList<Placed> Band(PartType type, int quantity, Box rectangle, CancellationToken token,
        Drawing? prefetch)
    {
        token.ThrowIfCancellationRequested();
        if (quantity <= 2 || type.Orientations.Count == 0 || rectangle.Length <= 0 || rectangle.Width <= 0)
            return Array.Empty<Placed>();
        var key = (type.Index, quantity, rectangle.Length, rectangle.Width);
        BandEntry? entry;
        lock (bands)
            bands.TryGetValue(key, out entry);
        if (entry == null)
        {
            // Types may build bands in parallel: each type's drawing and orientation list are its
            // own. Prefetches of one type fill their own drawings and only read the orientations.
            var (members, poses) = CannotHoldBlock(type, rectangle)
                ? (Array.Empty<Placed>(), null)
                : Propose(type, quantity, rectangle, token,
                    spacing + System.Math.Pow(10, -NoFitCache.Precision), prefetch ?? DrawingOf(type));
            lock (bands)
                if (!bands.TryGetValue(key, out entry))
                    bands[key] = entry = new BandEntry(members, poses, Cost(type, quantity));
        }
        if (prefetch != null)
            return entry.Members ?? Bind(type, entry.Poses!, orientations[type.Index].ToList(), token);
        lock (bands)
        {
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
    /// A private drawing of the type for one prefetching thread: Fill sorts a drawing's cached
    /// best-fit list in place, so two fills of one drawing must not run at once. It draws the same
    /// best-fit candidates from the geometry pool; release it with <see cref="ReleasePrefetch"/>.
    /// </summary>
    public static Drawing PrefetchDrawing(PartType type) => DrawingJobMapper.CreateDrawing(type.Part);

    public static void ReleasePrefetch(Drawing drawing) => BestFitCache.Invalidate(drawing);

    /// <summary>Work charged for one Fill proposal; see <see cref="Build"/>.</summary>
    private static long Cost(PartType type, int quantity)
    {
        var vertices = type.Orientations.Count > 0 ? type.Orientations[0].Outline.Count : 1;
        return (long)quantity * quantity * System.Math.Max(1, vertices);
    }

    private IReadOnlyList<Placed> Build(PartType type, int quantity, Box rectangle, CancellationToken token)
    {
        // PrivatePlateFill.Run below is a full independent NFP-based pack whose own operations
        // never touch this solve's effort meter; charge it here using the same factors - demand
        // and outline complexity - that drive its real cost, so the shared budget actually sees it.
        // A rectangle that cannot hold a block is charged and counted as if filled, so the meter,
        // and every later decision it steers, reads the same as when the Fill ran.
        counter.Add(Cost(type, quantity));
        if (CannotHoldBlock(type, rectangle))
            return Array.Empty<Placed>();
        var (members, poses) = Propose(type, quantity, rectangle, token, spacing, DrawingOf(type));
        return members ?? Bind(type, poses!, orientations[type.Index], token);
    }

    private Drawing DrawingOf(PartType type) => drawingFor?.Invoke(type.Part) ?? OwnDrawing(type);

    /// <summary>
    /// The Fill proposal's certified poses, or its members when there are none to bind: empty
    /// when Fill fails or certification rejects it.
    /// </summary>
    private (IReadOnlyList<Placed>? Members, NestJobPlacement[]? Poses) Propose(PartType type, int quantity,
        Box rectangle, CancellationToken token, double fillSpacing, Drawing drawing)
    {
        try
        {
            var members = PrivatePlateFill.Run(drawing, type.Part.Rotation, fillSpacing,
                rectangle.Length, rectangle.Width, quantity, token);
            token.ThrowIfCancellationRequested();
            var poses = Certify(type, members.Take(quantity).ToArray(), spacing, token);
            return poses == null ? (Array.Empty<Placed>(), null) : (null, poses);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return (Array.Empty<Placed>(), null);
        }
    }

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
        if (members.Count <= 2)
            return null;
        var geometry = JobPartGeometry.TryRead(type.Part.Geometry);
        if (geometry == null)
            return null;
        // Canonical rebinding is already performed by FillItem. Quantization removes sub-grid
        // arithmetic differences from equivalent Fill proposals; certify the resulting poses.
        var poses = members.Select(p => new NestJobPlacement(type.Part.Id, 0,
            System.Math.Round(p.Location.X, 8), System.Math.Round(p.Location.Y, 8),
            System.Math.Round(Angle.NormalizeRad(p.Rotation), 10)))
            .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Rotation).ToArray();
        if (poses.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Rotation)
            || !type.Part.Rotation.Allows(p.Rotation)))
            return null;
        // Each member outlined once, not once per pair it is tested in.
        var prepared = poses.Select(p => NestLayoutCheck.Prepare(geometry, p, spacing)).ToArray();
        for (var i = 0; i < poses.Length; i++)
            for (var j = i + 1; j < poses.Length; j++)
            {
                token.ThrowIfCancellationRequested();
                if (!NestLayoutCheck.Clears(prepared[i], prepared[j]))
                    return null;
            }
        return poses;
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
                var orientation = orientations.FirstOrDefault(o => o.Rotation == pose.Rotation);
                if (orientation == null)
                {
                    orientation = PartCatalog.CreateOrientation(type, orientations.Max(o => o.Index) + 1, pose.Rotation);
                    if (orientation == null)
                        return Array.Empty<Placed>();
                    orientations.Add(orientation);
                }
                result.Add(new Placed(orientation, pose.X - poses[0].X, pose.Y - poses[0].Y));
            }
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return Array.Empty<Placed>();
        }
    }
}

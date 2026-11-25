#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Raster;

/// <summary>One requested part type with its readable material.</summary>
internal sealed record RasterType(int Index, NestJobPart Part, JobPartGeometry? Geometry, double MaterialArea)
{
    public string Id => Part.Id;
    public int Priority => Part.Priority;
}

/// <summary>
/// Two raster grids for one part spacing: a coarse grid whose no-fit maps propose positions over
/// the whole sheet, and a fine grid, <see cref="Ratio"/> times finer, that settles each proposal
/// in a small window by direct mask tests and is the only authority on what fits. Every requested
/// type gets one mask per candidate rotation on both grids.
/// </summary>
internal sealed class RasterGrid
{
    /// <summary>Coarse cells per fine cell along each axis.</summary>
    public const int Ratio = 4;

    /// <summary>Fine cells per part spacing (or per <see cref="ZeroSpacingBasis"/> without one).</summary>
    private const double CellsPerSpacing = 8;

    /// <summary>Stand-in spacing for sizing cells when the job has none.</summary>
    private const double ZeroSpacingBasis = 0.25;

    /// <summary>Most fine cells across the largest part, and across the largest sheet: caps the
    /// work on large parts and the memory of a sheet's occupancy map.</summary>
    private const double MaxCellsPerPart = 2400, MaxCellsPerSheet = 8000;

    /// <summary>Extra fine-mask dilation in cells; above 1/sqrt 2 so disjoint masks keep the spacing
    /// (see <see cref="RasterMask.Build"/>).</summary>
    private const double FineBias = 0.75;

    /// <summary>Coarse-mask dilation in coarse cells, less than half the spacing: the coarse
    /// grid only proposes, so it leans optimistic.</summary>
    private const double CoarseBias = -0.25;

    /// <summary>Fine window reach around a coarse proposal, in coarse cells.</summary>
    private const double Reach = 1.5;

    private readonly ConcurrentDictionary<(RasterMask, RasterMask), Lazy<NoFitMap>> noFits = new();

    private RasterGrid(double spacing, double fineCell)
    {
        Spacing = spacing;
        FineCell = fineCell;
        FineDilate = System.Math.Max(0, spacing / 2 + FineBias * fineCell);
        Cell = fineCell * Ratio;
        Dilate = System.Math.Min(FineDilate, System.Math.Max(0, spacing / 2 + CoarseBias * Cell));
        Span = (int)System.Math.Ceiling(Reach * Ratio);
    }

    public double Spacing { get; }
    public double Cell { get; }
    public double Dilate { get; }
    public double FineCell { get; }
    public double FineDilate { get; }
    public int Span { get; }

    /// <summary>Coarse masks per type index, each with its <see cref="RasterMask.Fine"/> twin.</summary>
    public IReadOnlyList<RasterMask>[] Masks { get; private set; } = Array.Empty<IReadOnlyList<RasterMask>>();

    public NoFitMap NoFit(RasterMask a, RasterMask b) =>
        noFits.GetOrAdd((a, b), key => new Lazy<NoFitMap>(() => NoFitMap.Build(key.Item1, key.Item2))).Value;

    /// <summary>Builds the grid for every stock of one part spacing.</summary>
    public static RasterGrid Create(double spacing, IReadOnlyList<NestPlateStock> stocks,
        IReadOnlyList<RasterType> types, bool[] usable, CancellationToken token)
    {
        var largest = types.Where(t => usable[t.Index] && t.Geometry != null)
            .Select(t => System.Math.Max(t.Geometry!.Bounds.Length, t.Geometry.Bounds.Width))
            .DefaultIfEmpty(1).Max();
        var sheet = stocks.Max(s => System.Math.Max(s.WorkArea.Length, s.WorkArea.Width));
        var basis = spacing > 0 ? spacing : ZeroSpacingBasis;
        var fine = System.Math.Max(basis / CellsPerSpacing,
            System.Math.Max(largest / MaxCellsPerPart, sheet / MaxCellsPerSheet));
        var grid = new RasterGrid(spacing, fine);

        var poses = new List<(int Type, double Angle, int Kinds)>();
        foreach (var t in types)
            if (usable[t.Index] && t.Geometry != null)
                poses.AddRange(Angles(t).Select(a => (t.Index, a.Angle, a.Kinds)));
        var built = new RasterMask?[poses.Count];
        Parallel.For(0, poses.Count, new ParallelOptions { CancellationToken = token }, i =>
        {
            var (type, angle, kinds) = poses[i];
            var outline = Outline(types[type].Geometry!, angle);
            if (outline == null)
                return;
            var coarse = RasterMask.Build(type, angle, outline, grid.Cell, grid.Dilate);
            var fineMask = RasterMask.Build(type, angle, outline, grid.FineCell, grid.FineDilate);
            if (coarse == null || fineMask == null)
                return;
            coarse.Kinds = fineMask.Kinds = kinds;
            coarse.Fine = fineMask;
            built[i] = coarse;
        });
        var masks = built.Where(m => m != null).Select(m => m!).ToList();
        var maxWidth = masks.Count == 0 ? 1 : masks.Max(m => m.W);
        Parallel.ForEach(masks, new ParallelOptions { CancellationToken = token }, m => m.PrepareForNoFit(maxWidth));
        grid.Masks = types.Select(t => (IReadOnlyList<RasterMask>)masks.Where(m => m.Type == t.Index).ToList()).ToArray();
        return grid;
    }

    /// <summary>The material outline the layout check measures (perimeter arcs circumscribed,
    /// cutout arcs inscribed), rotated about the snapshot origin.</summary>
    private static SpacingOutline? Outline(JobPartGeometry geometry, double angle)
    {
        var shapes = new[] { geometry.Perimeter }.Concat(geometry.Cutouts);
        var entities = shapes.SelectMany(s => s.Entities).Select(e => e.Clone()).ToList();
        if (angle != 0)
            foreach (var entity in entities)
                entity.Rotate(angle);
        return SpacingOutline.Build(new ShapeProfile(entities), Vector.Zero, 0);
    }

    /// <summary>
    /// Candidate rotations. A part free to turn gets the quarter turns, the eighth turns and the
    /// turns that lay its longest convex-hull edges along the sheet's axes, each tagged with its
    /// <see cref="RotationKinds"/>; a limited part gets its legal angles (at most eight sweep
    /// samples), usable by every variant.
    /// </summary>
    private static List<(double Angle, int Kinds)> Angles(RasterType type)
    {
        var policy = type.Part.Rotation;
        var result = new List<(double Angle, int Kinds)>();
        if (policy.Kind != RotationPolicyKind.Automatic)
        {
            foreach (var angle in policy.EnumerateAngles(maxSamples: 8))
                result.Add((angle, RotationKinds.All));
            return result;
        }

        for (var i = 0; i < 4; i++)
            Add(System.Math.PI / 2 * i, RotationKinds.QuarterTurns);
        for (var i = 0; i < 4; i++)
            Add(System.Math.PI / 4 + System.Math.PI / 2 * i, RotationKinds.EighthTurns);

        var points = type.Geometry!.Perimeter.ToPolygonWithTolerance(0.01).Vertices;
        var hull = ConvexHull.Compute(points).Vertices;
        var edges = new List<(double Length, double Angle, int Order)>();
        for (var i = 0; i < hull.Count; i++)
        {
            var p = hull[i];
            var q = hull[(i + 1) % hull.Count];
            var length = p.DistanceTo(q);
            if (length > 0)
                edges.Add((length, PortableMath.Atan2(q.Y - p.Y, q.X - p.X), i));
        }
        foreach (var edge in edges.OrderByDescending(e => e.Length).ThenBy(e => e.Order).Take(HullEdges))
            for (var i = 0; i < 4; i++)
                Add(-edge.Angle + System.Math.PI / 2 * i, RotationKinds.HullAligned);
        return result;

        void Add(double angle, int kinds)
        {
            var turn = 2 * System.Math.PI;
            angle %= turn;
            if (angle < 0)
                angle += turn;
            foreach (var existing in result)
            {
                var diff = System.Math.Abs(existing.Angle - angle);
                if (System.Math.Min(diff, turn - diff) < SameAngle)
                    return;
            }
            result.Add((angle, kinds));
        }
    }

    /// <summary>Longest hull edges laid along the sheet's axes per part.</summary>
    private const int HullEdges = 2;

    /// <summary>Rotations closer than this (half a degree) count as one.</summary>
    private const double SameAngle = System.Math.PI / 360;
}

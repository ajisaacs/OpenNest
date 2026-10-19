#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>Where a perimeter can be pierced in open scrap, in plate coordinates.</summary>
/// <param name="Pierces">Points that keep the zone from every captured material, nearest the targets first.</param>
/// <param name="BestEffort">Set when no scrap near the part keeps the zone: the point with the most clearance.</param>
/// <param name="Clearance">The best-effort point's exact distance to the nearest part material.</param>
internal sealed record ScrapPierces(IReadOnlyList<Vector> Pierces, Vector? BestEffort, double Clearance)
{
    internal static ScrapPierces None { get; } = new(Array.Empty<Vector>(), null, 0);
}

/// <summary>
/// The pierce-zone fallback for perimeters. When no configured lead-in of a perimeter keeps
/// its pierce clear, the planner looks for a pierce in the open scrap around the part: the
/// window <see cref="WindowMargin"/> zone radii around the part, minus every part's clean
/// material grown by the zone radius plus a margin (<see cref="ClipperBridge"/>, conservative
/// flattening). Each target (a ranked entry of the perimeter) gets the clear point nearest it.
/// When nothing is clear, the pole of inaccessibility of the window, with the parts as holes,
/// is the best effort. These are proposals only: the planner emits a straight lead from each
/// to its nearest perimeter point and the lead validator stays the authority on it.
/// One instance serves one planning walk and remembers each part's scrap; the materials must not
/// change during it.
/// </summary>
internal sealed class ScrapPierceSearch
{
    /// <summary>The window reaches this many zone radii beyond the part's extent.</summary>
    internal const double WindowMargin = 4;

    private const int PoleSearchCells = 4096;
    private readonly LeadMaterialSnapshot[] materials;
    private readonly double radius;
    private readonly double tolerance;
    private readonly Region?[] regions;
    private readonly Dictionary<LeadMaterialSnapshot, Scrap> scrap = new(ReferenceEqualityComparer.Instance);

    internal ScrapPierceSearch(IReadOnlyList<LeadMaterialSnapshot> materials, double radius)
    {
        if (!(radius > 0) || !double.IsFinite(radius))
            throw new ArgumentOutOfRangeException(nameof(radius));
        this.materials = materials.ToArray();
        this.radius = radius;
        // Flattening and offset chords stay within a hundredth of the zone (Clipper keeps 1e-4).
        tolerance = System.Math.Max(radius / 100, System.Math.Pow(10, 1 - ClipperBridge.Precision));
        regions = new Region?[this.materials.Length];
    }

    /// <summary>
    /// Up to <paramref name="max"/> distinct clear pierces for <paramref name="own"/>'s
    /// perimeter, the one nearest each of <paramref name="targets"/> in turn, or a best effort
    /// when the window has none. Nothing when a material is incomplete.
    /// </summary>
    internal ScrapPierces Find(LeadMaterialSnapshot own, IReadOnlyList<Vector> targets, int max,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!own.IsComplete || materials.Any(m => !m.IsComplete) || targets.Count == 0 || max <= 0)
            return ScrapPierces.None;
        if (!scrap.TryGetValue(own, out var around))
            scrap[own] = around = Around(own, token);
        if (around.Clear.Count == 0)
            return around.BestEffort;
        var pierces = new List<Vector>();
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            var pierce = Nearest(around.Clear, target);
            if (pierces.All(p => p.DistanceTo(pierce) > tolerance))
                pierces.Add(pierce);
            if (pierces.Count == max)
                break;
        }
        return new(pierces, null, 0);
    }

    // The clear scrap around one part, or its best effort when there is none.
    private Scrap Around(LeadMaterialSnapshot own, CancellationToken token)
    {
        var ownRegion = Outline(own, includeHoles: false);
        var margin = WindowMargin * radius;
        var window = new Rect(ownRegion.Bounds.MinX - margin, ownRegion.Bounds.MinY - margin,
            ownRegion.Bounds.MaxX + margin, ownRegion.Bounds.MaxY + margin);
        var nearby = new PathsD(ownRegion.Paths);
        for (var i = 0; i < materials.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (ReferenceEquals(materials[i], own))
                continue;
            var region = regions[i] ??= Outline(materials[i], includeHoles: true);
            if (region.Bounds.Overlaps(window))
                nearby.AddRange(region.Paths);
        }
        var windowPath = new PathsD { Clipper.MakePath(new[] { window.MinX, window.MinY, window.MaxX, window.MinY,
            window.MaxX, window.MaxY, window.MinX, window.MaxY }) };

        // Clear pierce points: the window outside every material grown by the zone. The extra
        // tolerance covers the rounding of the difference itself; the validator checks exactly.
        var blocked = ClipperBridge.Offset(nearby, radius + tolerance, tolerance, circumscribe: true);
        var clip = new PathsD(blocked.Outers.Select(p => ClipperBridge.ToPath(p, positive: true))
            .Concat(blocked.Holes.Select(p => ClipperBridge.ToPath(p, positive: false))));
        var clear = Clipper.Difference(windowPath, clip, FillRule.NonZero, ClipperBridge.Precision)
            .Where(p => p.Count >= 3).ToList();
        token.ThrowIfCancellationRequested();
        if (clear.Count != 0)
            return new(clear, ScrapPierces.None);

        // No clear scrap: the pole of the scrap in the window (the window with the parts as
        // holes) that keeps the most clearance from part material.
        var free = Clipper.Difference(windowPath, nearby, FillRule.NonZero, ClipperBridge.Precision)
            .Where(p => p.Count >= 3).ToList();
        var holes = free.Where(p => !Clipper.IsPositive(p)).Select(ClipperBridge.ToPolygon).ToList();
        Vector? best = null;
        var bestClearance = double.NegativeInfinity;
        foreach (var path in free.Where(Clipper.IsPositive))
        {
            token.ThrowIfCancellationRequested();
            var outer = ClipperBridge.ToPolygon(path);
            var inside = holes.Where(h => outer.ContainsPoint(h.Vertices[0])).ToList();
            var pole = PolyLabel.Find(outer, inside, tolerance, PoleSearchCells);
            if (!double.IsFinite(pole.X) || !double.IsFinite(pole.Y) || !outer.ContainsPoint(pole)
                || inside.Any(h => h.ContainsPoint(pole)) || materials.Any(m => m.ContainsMaterial(pole, token)))
                continue;
            var budget = int.MaxValue;
            var clearance = materials.Min(m => LeadPathValidator.MaterialDistance(pole, m, ref budget, token));
            if (clearance > bestClearance)
            {
                best = pole;
                bestClearance = clearance;
            }
        }
        return new(clear, best == null ? ScrapPierces.None : new(Array.Empty<Vector>(), best, bestClearance));
    }

    // The closest point of the clear region's boundary; targets lie on material, outside it.
    private static Vector Nearest(List<PathD> paths, Vector target)
    {
        var best = default(Vector);
        var bestDistance = double.PositiveInfinity;
        foreach (var path in paths)
            for (var i = 0; i < path.Count; i++)
            {
                var a = new Vector(path[i].x, path[i].y);
                var b = new Vector(path[(i + 1) % path.Count].x, path[(i + 1) % path.Count].y);
                var point = new Line(a, b).ClosestPointTo(target);
                var distance = point.DistanceTo(target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }
        return best;
    }

    // Outlines flattened as conservatively as ClipperBridge.ToRegion: the perimeter never inside
    // its curves, holes never outside theirs. A part's own holes are not scrap for its perimeter.
    private Region Outline(LeadMaterialSnapshot material, bool includeHoles)
    {
        var paths = new PathsD();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (var ring = 0; ring < (includeHoles ? material.Rings.Count : 1); ring++)
        {
            var shape = new Shape();
            shape.Entities.AddRange(material.Rings[ring].Select(c => c.ToEntity()));
            var polygon = ClipperBridge.Flatten(shape, tolerance, circumscribe: ring == 0);
            paths.Add(ClipperBridge.ToPath(polygon, positive: ring == 0));
            if (ring != 0)
                continue;
            foreach (var vertex in polygon.Vertices)
            {
                minX = System.Math.Min(minX, vertex.X);
                minY = System.Math.Min(minY, vertex.Y);
                maxX = System.Math.Max(maxX, vertex.X);
                maxY = System.Math.Max(maxY, vertex.Y);
            }
        }
        return new(paths, new(minX, minY, maxX, maxY));
    }

    private sealed record Region(PathsD Paths, Rect Bounds);

    private sealed record Scrap(List<PathD> Clear, ScrapPierces BestEffort);

    private readonly record struct Rect(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Overlaps(Rect other) => MinX <= other.MaxX && other.MinX <= MaxX
            && MinY <= other.MaxY && other.MinY <= MaxY;
    }
}

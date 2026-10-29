#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// A closed outline the packer fills in place of a rectangular work area: a frame's cutout in
/// frame coordinates, with the parts already seated in it as obstacles. Each orientation's
/// starting free region is its inner-fit polygon: the reference points at which its clearance
/// footprint lies inside the outline eroded by the other half of the clearance, so a part
/// keeps the full clearance from the frame exactly as it does from another part. The inner
/// fit is computed once per orientation and shared by every packing of this boundary.
/// </summary>
/// <remarks>Bound to one <see cref="NoFitCache"/>: its footprints define the inner fits and the
/// obstacle no-fit polygons, so a packer filling this boundary must use that cache.</remarks>
internal sealed class PackBoundary
{
    /// <summary>Extra erosion beyond the half clearance, covering the outline's flattening and
    /// the round-offset chords at its reflex corners.</summary>
    private const double Margin = 0.002;

    private const double ArcTolerance = 0.001;

    private readonly PathsD eroded;
    private readonly Orientation[] obstacles;
    private readonly ConcurrentDictionary<(int, int), PathsD> innerFits = new();

    /// <param name="outline">Closed polygon inscribed in the true outline, so flattening never
    /// adds room.</param>
    /// <param name="obstacles">Polygons circumscribing material already inside the outline, with
    /// the chord deviation each was flattened with; every obstacle gets the full clearance.</param>
    /// <param name="nfps">The cache whose footprints this boundary is fitted to.</param>
    public PackBoundary(PathD outline, IReadOnlyList<(PathD Outline, double Tolerance)> obstacles, NoFitCache nfps)
    {
        Nfps = nfps;
        var box = Clipper.GetBounds(outline);
        Bounds = new Box(box.left, box.top, box.Width, box.Height); // Clipper RectD: top is the minimum Y.
        eroded = Clipper.InflatePaths(new PathsD { Positive(outline) }, -(nfps.Clearance / 2 + Margin),
            JoinType.Round, EndType.Polygon, 2.0, NoFitCache.Precision, ArcTolerance);
        eroded.RemoveAll(p => p.Count < 3);
        this.obstacles = new Orientation[obstacles.Count];
        for (var i = 0; i < obstacles.Count; i++)
        {
            var path = Positive(obstacles[i].Outline);
            var tolerance = obstacles[i].Tolerance;
            var bounds = Clipper.GetBounds(path);
            this.obstacles[i] = new Orientation
            {
                // Real part types are nonnegative, so obstacle footprints never alias them.
                TypeIndex = -1,
                Index = i,
                Rotation = 0,
                Outline = path,
                Tolerance = tolerance,
                MinX = bounds.left - tolerance,
                MinY = bounds.top - tolerance,
                MaxX = bounds.right + tolerance,
                MaxY = bounds.bottom + tolerance,
            };
        }
    }

    /// <summary>
    /// A sheet's material as the boundary: <paramref name="material"/> is its outline less its
    /// obstacles (outer paths positive, holes negative) in the placement frame, and parts keep
    /// <paramref name="edgeClearance"/> from every edge and obstacle, as they keep the edge
    /// spacing from a rectangular sheet's edges. The outline is eroded by the edge clearance
    /// less the half clearance the footprints already carry (grown when that is negative), plus
    /// the margin; <paramref name="bounds"/> is the packer's work area and front origin.
    /// </summary>
    public PackBoundary(PathsD material, double edgeClearance, Box bounds, NoFitCache nfps)
    {
        Nfps = nfps;
        Bounds = bounds;
        var inset = edgeClearance - nfps.Clearance / 2 + Margin;
        eroded = Clipper.InflatePaths(material, -inset, JoinType.Round, EndType.Polygon, 2.0,
            NoFitCache.Precision, ArcTolerance);
        eroded.RemoveAll(p => p.Count < 3);
        obstacles = System.Array.Empty<Orientation>();
    }

    public NoFitCache Nfps { get; }

    /// <summary>Bounds of the outline: the packer's work area and front origin.</summary>
    public Box Bounds { get; }

    public int ObstacleCount => obstacles.Length;

    public Orientation Obstacle(int index) => obstacles[index];

    /// <summary>Coarse filter against the outline's bounds; the inner fit decides.</summary>
    public bool Fits(double width, double height) =>
        width <= Bounds.Length + 1e-9 && height <= Bounds.Width + 1e-9;

    /// <summary>A fresh copy of the orientation's starting free region.</summary>
    public PathsD InnerFit(Orientation orientation) =>
        new(innerFits.GetOrAdd((orientation.TypeIndex, orientation.Index),
            _ => InnerFit(Nfps.Footprint(orientation), eroded)));

    /// <summary>
    /// Reference points t at which <paramref name="grown"/> lies inside one component of
    /// <paramref name="hole"/>: its anchor vertex is inside the component (t in component - anchor)
    /// and its outline never meets the component's boundary (t outside boundary + reflected
    /// outline, including the pose that contains the whole component).
    /// </summary>
    internal static PathsD InnerFit(PathD grown, PathsD hole)
    {
        if (grown.Count < 3)
            return new PathsD();
        var reflected = new PathD(grown.Select(p => new PointD(-p.x, -p.y)));
        var anchor = grown[0];
        var inside = new PathsD();
        PathsD? forbidden = null;
        foreach (var component in hole)
        {
            if (component.Count < 3)
                continue;
            inside.Add(Clipper.TranslatePath(component, -anchor.x, -anchor.y));
            // Clipper.MinkowskiSum rounds to two decimals; pass the job precision explicitly.
            var sweep = Clipper.Union(Minkowski.Sum(reflected, component, true, NoFitCache.Precision),
                new PathsD { Clipper.TranslatePath(reflected, component[0].x, component[0].y) },
                FillRule.NonZero, NoFitCache.Precision);
            forbidden = forbidden == null ? sweep : Clipper.Union(forbidden, sweep, FillRule.NonZero, NoFitCache.Precision);
        }
        return forbidden == null
            ? new PathsD()
            : Clipper.Difference(inside, forbidden, FillRule.NonZero, NoFitCache.Precision);
    }

    private static PathD Positive(PathD path)
    {
        if (path.Count >= 3 && !Clipper.IsPositive(path))
        {
            path = new PathD(path);
            path.Reverse();
        }
        return path;
    }
}

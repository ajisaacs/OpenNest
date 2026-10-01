#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Spacing-inflated footprints and the no-fit polygons between them, for one clearance value.
///
/// Every placed part owns a footprint: its outline grown by half the required clearance
/// (plus its own chord tolerance). Two parts respect the clearance exactly when their
/// footprints do not overlap, so the whole spacing rule reduces to NFP containment.
/// NFPs are translation-invariant, so each (orientation, orientation) pair is computed once
/// per job and reused by every sheet, stock trial and strategy variant.
/// </summary>
internal sealed class NoFitCache
{
    /// <summary>Clipper decimal precision; 1e-4 job units is far below any margin we keep.</summary>
    public const int Precision = NestTolerances.ClipperPrecision;

    private readonly double halfClearance;
    private readonly ConcurrentDictionary<(int, int), PathD> footprints = new();
    private readonly ConcurrentDictionary<(int, int, int, int), Lazy<Nfp>> nfps = new();

    public NoFitCache(double clearance)
    {
        halfClearance = clearance / 2;
    }

    public PathD Footprint(Orientation o) =>
        footprints.GetOrAdd((o.TypeIndex, o.Index), _ => BuildFootprint(o));

    /// <summary>NFP of <paramref name="moving"/> around <paramref name="fixedPart"/> placed at the origin.</summary>
    public Nfp Get(Orientation fixedPart, Orientation moving) =>
        nfps.GetOrAdd(
                (fixedPart.TypeIndex, fixedPart.Index, moving.TypeIndex, moving.Index),
                _ => new Lazy<Nfp>(() => Build(fixedPart, moving), LazyThreadSafetyMode.ExecutionAndPublication)
            )
            .Value;

    private PathD BuildFootprint(Orientation o)
    {
        // Miter joins (squared past the limit) always contain the exact round offset, so the
        // footprint is a superset of "every point within the clearance of the outline".
        var inflated = Clipper.InflatePaths(
            new PathsD { o.Outline },
            // Four additional grid units cover this engine's repeated footprint/NFP
            // Boolean operations. Keep its established contact points and packing quality.
            halfClearance + NestTolerances.SafeClearanceMargin(o.Tolerance) / 2
                + 4 * System.Math.Pow(10, -Precision),
            JoinType.Miter,
            EndType.Polygon,
            2.0,
            Precision,
            0.0
        );
        var best = inflated.OrderByDescending(p => System.Math.Abs(Clipper.Area(p))).First();
        if (!Clipper.IsPositive(best))
            best.Reverse();
        return best;
    }

    private Nfp Build(Orientation fixedPart, Orientation moving)
    {
        var a = Footprint(fixedPart);
        var b = Footprint(moving);
        var negB = new PathD(b.Count);
        foreach (var p in b)
            negB.Add(new PointD(-p.x, -p.y));

        PathsD region;
        if (IsConvex(a) && IsConvex(b))
        {
            region = new PathsD { ConvexSum(a, negB) };
        }
        else
        {
            region = ConcaveSum(a, negB);
            NfpHoleFilter.RemoveCoveredHoles(region, a, negB);
        }
        return new Nfp(region, Clipper.GetBounds(region));
    }

    /// <summary>Filled Minkowski sum, with boundary sweeps and containment unioned together.</summary>
    private static PathsD ConcaveSum(PathD a, PathD b)
    {
        // Minkowski.Sum rounds the boundary sweep before returning it. Unioning containment
        // covers afterward can leave hairline holes along those rounded edges. Submit the
        // raw edge-pair parallelograms and both covers together instead.
        var pieces = new PathsD
        {
            Clipper.TranslatePath(a, b[0].x, b[0].y), // Moving part inside the fixed part.
            Clipper.TranslatePath(b, a[0].x, a[0].y), // Moving part contains the fixed part.
        };
        var previousA = a[^1];
        foreach (var currentA in a)
        {
            var previousB = b[^1];
            foreach (var currentB in b)
            {
                var quad = new PathD
                {
                    new(previousA.x + previousB.x, previousA.y + previousB.y),
                    new(currentA.x + previousB.x, currentA.y + previousB.y),
                    new(currentA.x + currentB.x, currentA.y + currentB.y),
                    new(previousA.x + currentB.x, previousA.y + currentB.y),
                };
                if (!Clipper.IsPositive(quad))
                    quad.Reverse();
                pieces.Add(quad);
                previousB = currentB;
            }
            previousA = currentA;
        }
        return Clipper.Union(pieces, new PathsD(), FillRule.NonZero, Precision);
    }

    /// <summary>Minkowski sum of two convex CCW polygons by merging edges in angle order.</summary>
    private static PathD ConvexSum(PathD a, PathD b)
    {
        var ia = LowestIndex(a);
        var ib = LowestIndex(b);
        var result = new PathD(a.Count + b.Count);
        var current = new PointD(a[ia].x + b[ib].x, a[ia].y + b[ib].y);
        int i = 0, j = 0;
        while (i < a.Count || j < b.Count)
        {
            result.Add(current);
            var ea = i < a.Count ? Edge(a, ia + i) : default;
            var eb = j < b.Count ? Edge(b, ib + j) : default;
            // Both edge sequences start at the lowest vertex, so their angles rise through [0, 2pi).
            double order;
            if (i >= a.Count)
                order = -1;
            else if (j >= b.Count)
                order = 1;
            else
            {
                var difference = EdgeAngle(eb) - EdgeAngle(ea);
                order = System.Math.Abs(difference) < 1e-12 ? 0 : difference;
            }
            if (order > 0)
            {
                current = new PointD(current.x + ea.x, current.y + ea.y);
                i++;
            }
            else if (order < 0)
            {
                current = new PointD(current.x + eb.x, current.y + eb.y);
                j++;
            }
            else
            {
                current = new PointD(current.x + ea.x + eb.x, current.y + ea.y + eb.y);
                i++;
                j++;
            }
        }
        return result;
    }

    private static double EdgeAngle(PointD edge)
    {
        var angle = System.Math.Atan2(edge.y, edge.x);
        return angle < 0 ? angle + System.Math.PI * 2 : angle;
    }

    private static PointD Edge(PathD path, int index)
    {
        var from = path[index % path.Count];
        var to = path[(index + 1) % path.Count];
        return new PointD(to.x - from.x, to.y - from.y);
    }

    /// <summary>Lowest (then leftmost) vertex: the start of a CCW edge sequence sorted by angle.</summary>
    private static int LowestIndex(PathD path)
    {
        var best = 0;
        for (var i = 1; i < path.Count; i++)
            if (path[i].y < path[best].y || (path[i].y == path[best].y && path[i].x < path[best].x))
                best = i;
        return best;
    }

    private static bool IsConvex(PathD path)
    {
        var n = path.Count;
        if (n < 3)
            return false;
        for (var i = 0; i < n; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % n];
            var c = path[(i + 2) % n];
            var cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            if (cross < -1e-12)
                return false;
        }
        return true;
    }
}

/// <summary>Forbidden reference-point region (interior = overlap, boundary = touching) and its bounds.</summary>
internal sealed record Nfp(PathsD Region, RectD Bounds);

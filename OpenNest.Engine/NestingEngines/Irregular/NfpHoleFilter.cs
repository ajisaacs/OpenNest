#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Removes only NFP holes certified to lie wholly inside the filled Minkowski sum.</summary>
internal static class NfpHoleFilter
{
    internal static void RemoveCoveredHoles(PathsD region, PathD a, PathD b)
    {
        // Rounded Boolean operations can leave hairline holes inside forbidden material.
        // Size alone cannot distinguish those from a real pocket. A triangle T inside A,
        // translated by any vertex of B, is wholly inside A+B. If that same convex cell
        // strictly contains every vertex of a hole, it contains the entire hole as well.
        if (!region.Any(path => !Clipper.IsPositive(path)))
            return;
        RemoveCoveredBy(region, a, b);
        if (region.Any(path => !Clipper.IsPositive(path)))
            RemoveCoveredBy(region, b, a);
    }

    private static void RemoveCoveredBy(PathsD region, PathD source, PathD offsets)
    {
        var scale = System.Math.Pow(10, NoFitCache.Precision);
        var triangles = ConvexDecomposition.Triangulate(ClipperBridge.ToPolygon(source))
            .Select(triangle => Clipper.ScalePath64(new PathD(triangle.Vertices.Take(3)
                .Select(point => new PointD(point.X, point.Y))), scale))
            .ToList();
        if (!CertifiesSource(Clipper.ScalePath64(source, scale), triangles))
            return;
        var translations = Clipper.ScalePath64(offsets, scale);
        for (var index = region.Count - 1; index >= 0; index--)
        {
            var path = region[index];
            if (Clipper.IsPositive(path))
                continue;
            var ring = Clipper.ScalePath64(path, scale);
            if (IsCovered(ring, triangles, translations))
                region.RemoveAt(index);
        }
    }

    private static bool CertifiesSource(Path64 source, List<Path64> triangles)
    {
        // The shared triangulator uses doubles, so its ears are only candidates.
        // Positive lattice triangles whose oriented edges cancel to the source boundary
        // have the source's winding everywhere. For our simple CCW footprint that proves
        // every triangle lies inside it, without trusting floating-point ear decisions.
        try
        {
            var edges = new Dictionary<(long, long, long, long), int>();
            void AddBoundary(Path64 path, int direction)
            {
                for (var index = 0; index < path.Count; index++)
                {
                    var a = path[index];
                    var b = path[(index + 1) % path.Count];
                    if (a.X == b.X && a.Y == b.Y)
                        continue;
                    var forward = a.X < b.X || (a.X == b.X && a.Y < b.Y);
                    var key = forward ? (a.X, a.Y, b.X, b.Y) : (b.X, b.Y, a.X, a.Y);
                    edges.TryGetValue(key, out var balance);
                    edges[key] = checked(balance + (forward ? direction : -direction));
                }
            }
            AddBoundary(source, -1);
            foreach (var triangle in triangles)
            {
                if (triangle.Count != 3 || Cross(triangle[0], triangle[1], triangle[2], default) <= 0)
                    return false;
                AddBoundary(triangle, 1);
            }
            return triangles.Count > 0 && edges.Values.All(balance => balance == 0);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static Int128 Cross(Point64 a, Point64 b, Point64 point, Point64 offset) =>
        checked(((Int128)b.X - a.X) * ((Int128)point.Y - offset.Y - a.Y)
            - ((Int128)b.Y - a.Y) * ((Int128)point.X - offset.X - a.X));

    private static bool IsCovered(Path64 ring, List<Path64> triangles, Path64 translations)
    {
        foreach (var triangle in triangles)
            foreach (var offset in translations)
                if (StrictlyContains(triangle, offset, ring))
                    return true;
        return false;
    }

    private static bool StrictlyContains(Path64 triangle, Point64 offset, Path64 ring)
    {
        try
        {
            foreach (var point in ring)
                for (var edge = 0; edge < 3; edge++)
                {
                    var a = triangle[edge];
                    var b = triangle[(edge + 1) % 3];
                    // Work on the existing Clipper lattice. Promote before subtracting;
                    // double cross products can erase a thin ring's containment verdict.
                    if (Cross(a, b, point, offset) <= 0)
                        return false;
                }
            return ring.Count >= 3;
        }
        catch (OverflowException)
        {
            // An uncertifiable ring stays unchanged; uncertainty never fills a real pocket.
            return false;
        }
    }
}

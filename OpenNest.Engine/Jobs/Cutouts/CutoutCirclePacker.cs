#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.CirclePacking;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Circle-in-circle proposal for a round cutout: every insert is packed by its enclosing
/// circle (<see cref="Item"/>), so the hole and inserts are discs and placement is analytic.
/// Never changes demand, stock or a live plate.</summary>
/// <remarks>Two greedy passes over the copies, largest first, choose among candidate centers
/// tangent to two of {hole wall, placed discs} plus the hole's center. One grows from the
/// center, so a single insert sits concentric and keeps its own bore free for a later level;
/// the other lays rings against the wall first, which reproduces the ring-shaped optimal
/// packings of up to nine equal circles. The higher count wins, ties keep the centered pass.
/// Every pose is certified with <see cref="NestLayoutCheck.Clears"/> before it is returned.</remarks>
internal static class CutoutCirclePacker
{
    /// <summary>Inserts whose outline fills less of their enclosing circle stay with the lattice
    /// and NFP proposals; a gear (about 0.85) packs by its tip circle, a square (0.64) does not.</summary>
    internal const double MinFillRatio = 0.8;

    /// <summary>Extra gap beyond the spacing on every pair and at the wall, covering the layout
    /// check's flattening of the hole and of each insert.</summary>
    internal const double Margin = 2 * NestTolerances.ValidationOutline;

    // Greedy cost grows with the cube of the copy count; beyond this, a round hole's surplus
    // copies stay ordinary demand.
    private const int MaxCopies = 256;
    private const double Epsilon = 1e-9;

    private sealed record Disc(NestJobPart Part, Item Item, double Rotation, double Radius);

    /// <summary>The hole's center and radius when every entity of the cutout lies on one circle;
    /// otherwise null.</summary>
    internal static Circle? RoundHole(Shape cutout)
    {
        ArgumentNullException.ThrowIfNull(cutout);
        if (!cutout.IsClosed() || cutout.Entities.Count == 0)
            return null;
        Circle? first = null;
        foreach (var entity in cutout.Entities)
        {
            var circle = entity switch
            {
                Circle c => new Circle(c.Center, c.Radius),
                Arc a => new Circle(a.Center, a.Radius),
                _ => null,
            };
            if (circle == null || !double.IsFinite(circle.Radius) || circle.Radius <= 0)
                return null;
            first ??= circle;
            if (System.Math.Abs(circle.Radius - first.Radius) > Tolerance.Epsilon
                || circle.Center.DistanceTo(first.Center) > Tolerance.Epsilon)
                return null;
        }
        return first;
    }

    /// <summary>True when the hole is round and every insert's enclosing circle is a fair model
    /// of it, so this proposal is worth computing.</summary>
    internal static bool Applies(Shape cutout, IReadOnlyList<NestJobPart> inserts) =>
        RoundHole(cutout) != null && inserts.Count > 0
        && inserts.All(p => Item.TryCreate(p.Geometry) is { FillRatio: >= MinFillRatio });

    /// <summary>Frame-local poses inside one round cutout, or none when the cutout is not round
    /// or nothing fits. Instance indices count per requirement from zero.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(inserts);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(spacing) || spacing < 0)
            return Array.Empty<NestJobPlacement>();
        var frameGeometry = JobPartGeometry.TryRead(frame.Geometry);
        if (frameGeometry == null)
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var hole = RoundHole(frameGeometry.Cutouts[cutoutIndex]);
        if (hole == null)
            return Array.Empty<NestJobPlacement>();

        // The check flattens cutouts inscribed, so its hole may be an allowance smaller.
        var wall = hole.Radius - Item.FlattenAllowance - spacing - Margin;
        var discs = new List<Disc>();
        var geometries = new Dictionary<string, JobPartGeometry>(StringComparer.Ordinal);
        foreach (var part in inserts)
        {
            token.ThrowIfCancellationRequested();
            var geometry = JobPartGeometry.TryRead(part.Geometry);
            if (geometry == null || part.Quantity <= 0)
                continue;
            var item = Item.Create(geometry);
            var rotations = part.Rotation.EnumerateAngles(maxSamples: 1);
            if (rotations.Count == 0 || item.Radius > wall)
                continue;
            geometries[part.Id] = geometry;
            // No more copies than could cover the hole's area, so huge demand stays bounded.
            var byArea = (int)System.Math.Min(part.Quantity,
                System.Math.Floor(wall * wall / (item.Radius * item.Radius)) + 1);
            for (var copy = 0; copy < byArea && discs.Count < MaxCopies; copy++)
                discs.Add(new Disc(part, item, rotations[0], item.Radius));
        }
        if (discs.Count == 0)
            return Array.Empty<NestJobPlacement>();
        // Stable: equal radii keep caller requirement order.
        discs = discs.OrderByDescending(d => d.Radius).ToList();

        var gap = spacing + Margin;
        var centered = Greedy(discs, wall, gap, fromCenter: true, token);
        var walled = discs.Count > 1 ? Greedy(discs, wall, gap, fromCenter: false, token) : centered;
        var chosen = walled.Count > centered.Count ? walled : centered;

        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        var accepted = new List<(Disc Disc, Vector Center, NestJobPlacement Pose)>();
        foreach (var (disc, offset) in chosen)
        {
            token.ThrowIfCancellationRequested();
            var center = hole.Center + offset;
            var origin = center + Rotate(disc.Item.PartOffset, disc.Rotation);
            var pose = new NestJobPlacement(disc.Part.Id,
                accepted.Count(p => p.Pose.PartId == disc.Part.Id), origin.X, origin.Y, disc.Rotation);
            var geometry = geometries[disc.Part.Id];
            if (!NestLayoutCheck.Clears(frameGeometry, framePose, geometry, pose, spacing))
                continue;
            // Discs farther apart than their radii plus twice the spacing cannot touch the
            // check's inflated outlines; the analytic gap already proves them clear.
            if (accepted.Any(other => other.Center.DistanceTo(center)
                    < other.Disc.Radius + disc.Radius + 2 * spacing + Margin
                && !NestLayoutCheck.Clears(geometries[other.Disc.Part.Id], other.Pose, geometry, pose, spacing)))
                continue;
            accepted.Add((disc, center, pose));
        }
        token.ThrowIfCancellationRequested();
        return accepted.Select(p => p.Pose).ToArray();
    }

    /// <summary>Places each disc in order at its best feasible candidate; a disc with no
    /// feasible candidate is skipped so a smaller one may still fit. Centers are relative to
    /// the hole's center; <paramref name="wall"/> bounds center distance plus radius.</summary>
    private static List<(Disc Disc, Vector Center)> Greedy(IReadOnlyList<Disc> discs, double wall,
        double gap, bool fromCenter, CancellationToken token)
    {
        var placed = new List<(Disc Disc, Vector Center)>();
        foreach (var disc in discs)
        {
            token.ThrowIfCancellationRequested();
            var r = disc.Radius;
            var ring = wall - r;
            Vector? best = null;
            foreach (var candidate in Candidates(placed, ring, r, gap, fromCenter))
            {
                if (!Feasible(candidate) || (best is { } current && !Better(candidate, current)))
                    continue;
                best = candidate;
            }
            if (best is { } chosen)
                placed.Add((disc, chosen));

            bool Feasible(Vector p)
            {
                if (p.DistanceTo(0, 0) > ring + Epsilon)
                    return false;
                foreach (var (other, center) in placed)
                    if (center.DistanceTo(p) < other.Radius + r + gap - Epsilon)
                        return false;
                return true;
            }
        }
        return placed;

        bool Better(Vector candidate, Vector current)
        {
            // Centered pass: nearest the center; wall pass: nearest the wall. Then the lowest
            // angle counterclockwise from straight down, so rings close up in order.
            var radial = candidate.DistanceTo(0, 0) - current.DistanceTo(0, 0);
            if (System.Math.Abs(radial) > Epsilon)
                return fromCenter ? radial < 0 : radial > 0;
            return Turn(candidate) < Turn(current) - Epsilon;
        }
    }

    private static IEnumerable<Vector> Candidates(List<(Disc Disc, Vector Center)> placed, double ring,
        double r, double gap, bool fromCenter)
    {
        yield return new Vector(0, 0);
        if (placed.Count == 0)
        {
            if (!fromCenter)
                yield return new Vector(0, -ring);
            yield break;
        }
        for (var i = 0; i < placed.Count; i++)
        {
            var (a, ca) = placed[i];
            var da = a.Radius + r + gap;
            foreach (var p in Intersections(new Vector(0, 0), ring, ca, da))
                yield return p;
            for (var j = i + 1; j < placed.Count; j++)
            {
                var (b, cb) = placed[j];
                foreach (var p in Intersections(ca, da, cb, b.Radius + r + gap))
                    yield return p;
            }
        }
    }

    private static IEnumerable<Vector> Intersections(Vector c0, double r0, Vector c1, double r1)
    {
        var d = c0.DistanceTo(c1);
        if (d < Epsilon || d > r0 + r1 || d < System.Math.Abs(r0 - r1))
            yield break;
        var along = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
        var across = System.Math.Sqrt(System.Math.Max(0, r0 * r0 - along * along));
        var ux = (c1.X - c0.X) / d;
        var uy = (c1.Y - c0.Y) / d;
        var mx = c0.X + ux * along;
        var my = c0.Y + uy * along;
        yield return new Vector(mx - uy * across, my + ux * across);
        if (across > Epsilon)
            yield return new Vector(mx + uy * across, my - ux * across);
    }

    private static double Turn(Vector p)
    {
        if (p.DistanceTo(0, 0) < Epsilon)
            return -1;
        var angle = System.Math.Atan2(p.X, -p.Y);
        return angle < 0 ? angle + 2 * System.Math.PI : angle;
    }

    private static Vector Rotate(Vector v, double angle)
    {
        var sine = System.Math.Sin(angle);
        var cosine = System.Math.Cos(angle);
        return new Vector(v.X * cosine - v.Y * sine, v.X * sine + v.Y * cosine);
    }
}

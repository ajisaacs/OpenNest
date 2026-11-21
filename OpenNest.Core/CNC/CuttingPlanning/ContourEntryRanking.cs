using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Pure deterministic ordering of the automatic entry catalog toward the next cut:
/// circles rank by outgoing distance; other contours use facing sides, tier and travel,
/// then a stable geometric key. It adds no
/// candidates, mutates nothing, runs no lead checks and applies no cap — feasibility
/// filtering and the bounded selection belong to S07/S08, the wiring to S09.
/// </summary>
internal static class ContourEntryRanking
{
    /// <summary>
    /// Orders <paramref name="candidates"/> for one contour in local coordinates. With a
    /// <paramref name="target"/> (the next cut's look-ahead point): the number of matched
    /// facing sides of the candidate bounding rectangle descending (a corner on both facing
    /// sides is ideal), then rank tier ascending, then arrival-&gt;entry + entry-&gt;target
    /// ascending, then the stable geometric key. With no target (the last part): tier first,
    /// then distance to <paramref name="arrival"/> — never toward the plate origin. A part's
    /// outside is cut after its cutouts, so <paramref name="cutouts"/> (their centers, when
    /// given) replace <paramref name="arrival"/>: the tool comes from the nearest of them. The
    /// input list is returned untouched as a new list; entity order is never meaningful.
    /// </summary>
    internal static IReadOnlyList<ContourEntryCandidate> RankTowardNextCut(
        this IReadOnlyList<ContourEntryCandidate> candidates,
        Vector? target = null,
        Vector? arrival = null,
        IReadOnlyList<Vector> cutouts = null)
    {
        if (target.HasValue) PostVerificationGeometry.Validate(target.Value);
        if (arrival.HasValue) PostVerificationGeometry.Validate(arrival.Value);
        if (candidates.Count == 0)
            return new List<ContourEntryCandidate>();

        // Whole circles have no corners. A diagonal compass point is equally near two
        // bounding-box sides, but must not gain the two-side bonus of a real corner.
        // Rank outgoing travel first so arrival cannot pull the start away from the next
        // cut. Retain every compass/polar alternative for feasibility and rapid checks;
        // configured angle rounding remains the emitter's responsibility.
        if (target is { } next && candidates.Any(c => c.Kind == AutomaticEntryKind.CircleCompass)
            && candidates.All(c => c.Kind is AutomaticEntryKind.CircleCompass or AutomaticEntryKind.TargetFacing))
            return candidates.OrderBy(c => c.Choice.Point.DistanceTo(next))
                .ThenBy(c => arrival is { } from ? c.Choice.Point.DistanceTo(from) : 0.0)
                .ThenBy(c => c.GeometryKey.X)
                .ThenBy(c => c.GeometryKey.Y)
                .ToList();

        // Candidate bounding rectangle in the contour's local coordinates; every candidate
        // lies on the contour, so distances to the four side lines order side proximity.
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            var p = candidate.Choice.Point;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        // Facing sides from the target relative to the center: horizontal right when the target
        // is right of center, left when left of it; vertical likewise. A target level with the
        // center (a neighbor in the same row or column) faces neither side on that axis, so
        // rounding cannot send the entry to the far end of a long part.
        var centerX = minX + (maxX - minX) * 0.5;
        var centerY = minY + (maxY - minY) * 0.5;
        var horizontal = target == null ? 0 : Side(target.Value.X, centerX, maxX - minX);
        var vertical = target == null ? 0 : Side(target.Value.Y, centerY, maxY - minY);
        var from = cutouts is { Count: > 0 } ? cutouts : arrival is { } a ? new[] { a } : null;
        if (from != null)
            foreach (var point in from)
                PostVerificationGeometry.Validate(point);

        return candidates
            .Select(c => (Candidate: c, Score: Score(c, minX, minY, maxX, maxY, horizontal, vertical, target, from)))
            .OrderByDescending(x => x.Score.Facing)
            .ThenBy(x => x.Score.Tier)
            .ThenBy(x => x.Score.Travel)
            .ThenBy(x => x.Candidate.GeometryKey.X)
            .ThenBy(x => x.Candidate.GeometryKey.Y)
            .Select(x => x.Candidate)
            .ToList();
    }

    /// <summary>
    /// The ranking tier — coarser than the preference kind: outside corners first, then
    /// straight midpoints and tangent joints as peers, then every fallback kind.
    /// </summary>
    internal static int RankTier(this AutomaticEntryKind kind) => kind switch
    {
        AutomaticEntryKind.ConvexCorner => 0,
        AutomaticEntryKind.StraightMidpoint or AutomaticEntryKind.TangentJoint => 1,
        _ => 2,
    };

    private static (int Facing, int Tier, double Travel) Score(
        ContourEntryCandidate candidate,
        double minX,
        double minY,
        double maxX,
        double maxY,
        int horizontal,
        int vertical,
        Vector? target,
        IReadOnlyList<Vector> from)
    {
        var p = candidate.Choice.Point;
        var facing = 0;
        if (target != null)
        {
            // Nearest side(s) of the candidate bounding rectangle (a corner belongs to two
            // sides within tolerance, a fillet's joint within its reach of the corner it
            // rounds); count how many of them are facing sides.
            var left = p.X - minX;
            var right = maxX - p.X;
            var bottom = p.Y - minY;
            var top = maxY - p.Y;
            var min = System.Math.Min(System.Math.Min(left, right), System.Math.Min(bottom, top));
            var near = min + PostVerificationGeometry.Epsilon + candidate.CornerReach;
            if (horizontal > 0 && right <= near) facing++;
            if (horizontal < 0 && left <= near) facing++;
            if (vertical > 0 && top <= near) facing++;
            if (vertical < 0 && bottom <= near) facing++;
        }
        var travel = (from != null ? from.Min(f => f.DistanceTo(p)) : 0.0)
            + (target != null ? p.DistanceTo(target.Value) : 0.0);
        return (facing, candidate.Kind.RankTier(), travel);
    }

    // +1 when the target lies past the center along an axis, -1 when before it, 0 when level:
    // within the comparison tolerance, scaled to the candidates' span so rounding of placed
    // coordinates far from the origin still counts as level.
    private static int Side(double target, double center, double span)
    {
        var tolerance = PostVerificationGeometry.Epsilon * System.Math.Max(1.0, span);
        return target > center + tolerance ? 1 : target < center - tolerance ? -1 : 0;
    }
}

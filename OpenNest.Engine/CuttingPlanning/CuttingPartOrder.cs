using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Proposes a whole-part visiting order as an open traveling-salesman path from the start point
/// over part centers: nearest neighbor, then 2-opt and Or-opt improvement, never placing a part
/// before one of its prerequisites. It is only a proposal; the forward search still checks every
/// rapid and lead along it. A part may be left from somewhere other than where it is reached (a
/// cut-off is entered at its start and left at its far end): each leg then runs from one part's
/// exit to the next part's center, and a reversed run is costed along its own directed legs.
/// A slender part may instead carry visit points: the tool reaches it at one of them and leaves
/// from the same one, whichever suits the parts on either side, so a long strip beside a row is
/// costed from its near end rather than from a center far from where it is cut.
/// </summary>
internal static class CuttingPartOrder
{
    private const double Epsilon = 1e-9;

    /// <summary>Upper bound on improvement passes, so a large plate cannot loop for long.</summary>
    internal const int MaxPasses = 50;

    /// <summary>Upper bound on rounds of placing slender parts at their visit points.</summary>
    internal const int MaxVisitRounds = 8;

    /// <param name="parts">The part ordinals to order; prerequisites outside this set count as done.</param>
    /// <param name="centers">One representative point per part ordinal.</param>
    /// <param name="start">The modeled tool position before the first of these parts.</param>
    /// <param name="prerequisites">Ordinals that must come before each part; must be acyclic.</param>
    /// <param name="endpoint">Optional fixed final position after the last of these parts.</param>
    /// <param name="exits">Optional point each part is left from, per part ordinal; null when every
    /// part is left from its center.</param>
    /// <param name="visits">Optional visit points per part ordinal; see the other overload.</param>
    internal static int[] Plan(IReadOnlyList<int> parts, IReadOnlyList<Vector> centers, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token, Vector? endpoint = null,
        IReadOnlyList<Vector> exits = null, IReadOnlyList<IReadOnlyList<Vector>> visits = null)
    {
        var local = new Dictionary<int, int>();
        for (var i = 0; i < parts.Count; i++)
            local.Add(parts[i], i);
        var localCenters = parts.Select(p => centers[p]).ToArray();
        var localPrerequisites = parts
            .Select(p => (IReadOnlyCollection<int>)prerequisites[p].Where(local.ContainsKey).Select(q => local[q]).ToArray())
            .ToArray();
        var localExits = exits == null ? null : parts.Select(p => exits[p]).ToArray();
        var localVisits = visits == null ? null : parts.Select(p => visits[p]).ToArray();
        return Plan(localCenters, start, localPrerequisites, token, endpoint, localExits, localVisits)
            .Select(i => parts[i]).ToArray();
    }

    /// <param name="centers">One representative point per part ordinal.</param>
    /// <param name="start">The modeled tool position before the first part.</param>
    /// <param name="prerequisites">Ordinals that must come before each part; must be acyclic.</param>
    /// <param name="endpoint">
    /// Optional fixed final position AFTER the last visited point (an open path with a closed
    /// terminal edge). The terminal edge joins EVERY improvement delta, not just final
    /// scoring. Null keeps the previous whole-part open-route semantics exactly.
    /// </param>
    /// <param name="exits">
    /// Optional point each part is left from; null (every part left from its center) keeps the
    /// symmetric route exactly.
    /// </param>
    /// <param name="visits">
    /// Optional points per part ordinal where a slender part may be reached and left; null or empty
    /// for an ordinary part, which keeps its center and exit. Such a part is entered and left at the
    /// same point. After the center tour, each is put at its best visit point for its neighbors and
    /// moved to the gap where that detour is shortest, and the tour is improved again with those
    /// points. Null keeps the center tour exactly.
    /// </param>
    internal static int[] Plan(IReadOnlyList<Vector> centers, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token, Vector? endpoint = null,
        IReadOnlyList<Vector> exits = null, IReadOnlyList<IReadOnlyList<Vector>> visits = null)
    {
        var count = centers.Count;
        var order = NearestNeighbor(centers, exits ?? centers, start, prerequisites, token);
        var position = new int[count];
        Improve(order, centers, exits, start, prerequisites, position, token, endpoint);
        if (visits == null || !visits.Any(points => points is { Count: > 0 }))
            return order;

        var arrive = centers.ToArray();
        var leave = (exits ?? centers).ToArray();
        for (var round = 0; round < MaxVisitRounds; round++)
        {
            token.ThrowIfCancellationRequested();
            var changed = Settle(order, arrive, leave, visits, start, endpoint);
            changed |= Relocate(order, arrive, leave, visits, start, prerequisites, position, token, endpoint);
            if (!changed)
                break;
            Improve(order, arrive, leave, start, prerequisites, position, token, endpoint);
        }
        return order;
    }

    private static void Improve(int[] order, IReadOnlyList<Vector> centers, IReadOnlyList<Vector> exits, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, int[] position, CancellationToken token, Vector? endpoint)
    {
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            token.ThrowIfCancellationRequested();
            var improved = TwoOpt(order, centers, exits, start, prerequisites, position, token, endpoint);
            improved |= OrOpt(order, centers, exits ?? centers, start, prerequisites, position, token, endpoint);
            if (!improved)
                break;
        }
    }

    // The visit point cheapest between where the tool comes from and where it goes next (null at
    // the open end), with the length of that detour.
    private static (Vector Point, double Cost) BestVisit(IReadOnlyList<Vector> points, Vector from, Vector? to)
    {
        var best = points[0];
        var bestCost = double.MaxValue;
        foreach (var point in points)
        {
            var cost = from.DistanceTo(point) + (to is { } next ? point.DistanceTo(next) : 0);
            if (cost < bestCost - Epsilon)
            {
                best = point;
                bestCost = cost;
            }
        }
        return (best, bestCost);
    }

    // Puts each slender part at its best visit point for the parts now on either side of it.
    private static bool Settle(int[] order, Vector[] arrive, Vector[] leave,
        IReadOnlyList<IReadOnlyList<Vector>> visits, Vector start, Vector? endpoint)
    {
        var changed = false;
        for (var k = 0; k < order.Length; k++)
        {
            var part = order[k];
            if (visits[part] is not { Count: > 0 } points)
                continue;
            var from = k == 0 ? start : leave[order[k - 1]];
            var to = k + 1 < order.Length ? arrive[order[k + 1]] : endpoint;
            var current = from.DistanceTo(arrive[part]) + (to is { } next ? arrive[part].DistanceTo(next) : 0);
            var (point, cost) = BestVisit(points, from, to);
            if (cost >= current - Epsilon)
                continue;
            arrive[part] = leave[part] = point;
            changed = true;
        }
        return changed;
    }

    // Moves each slender part, at its best visit point, to the gap where its detour is shortest,
    // when that beats its detour where it is and keeps every prerequisite earlier.
    private static bool Relocate(int[] order, Vector[] arrive, Vector[] leave,
        IReadOnlyList<IReadOnlyList<Vector>> visits, Vector start, IReadOnlyList<IReadOnlyCollection<int>> prerequisites,
        int[] position, CancellationToken token, Vector? endpoint)
    {
        var changed = false;
        var count = order.Length;
        for (var part = 0; part < count; part++)
        {
            if (visits[part] is not { Count: > 0 } points)
                continue;
            token.ThrowIfCancellationRequested();
            var at = Array.IndexOf(order, part);
            var rest = order.Where(p => p != part).ToArray();
            var current = Detour(points, rest, at).Cost;
            var gaps = Enumerable.Range(0, count).Where(gap => gap != at)
                .Select(gap => (Gap: gap, Detour: Detour(points, rest, gap)))
                .Where(g => g.Detour.Cost < current - Epsilon)
                .OrderBy(g => g.Detour.Cost).ThenBy(g => g.Gap);
            foreach (var (gap, detour) in gaps)
            {
                var candidate = rest.Take(gap).Append(part).Concat(rest.Skip(gap)).ToArray();
                if (!Valid(candidate, prerequisites, position))
                    continue;
                Array.Copy(candidate, order, count);
                arrive[part] = leave[part] = detour.Point;
                changed = true;
                break;
            }
        }
        return changed;

        // The detour through the part inserted before rest[gap] (after the last at the end), net
        // of the leg it replaces.
        (Vector Point, double Cost) Detour(IReadOnlyList<Vector> points, int[] rest, int gap)
        {
            var from = gap == 0 ? start : leave[rest[gap - 1]];
            Vector? to = gap < rest.Length ? arrive[rest[gap]] : endpoint;
            var (point, cost) = BestVisit(points, from, to);
            return (point, cost - (to is { } next ? from.DistanceTo(next) : 0));
        }
    }

    private static int[] NearestNeighbor(IReadOnlyList<Vector> centers, IReadOnlyList<Vector> exits, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token)
    {
        var count = centers.Count;
        var placed = new bool[count];
        var order = new int[count];
        var current = start;
        for (var step = 0; step < count; step++)
        {
            token.ThrowIfCancellationRequested();
            var best = -1;
            var bestDistance = double.MaxValue;
            for (var part = 0; part < count; part++)
            {
                if (placed[part] || prerequisites[part].Any(p => !placed[p]))
                    continue;
                var distance = current.DistanceTo(centers[part]);
                if (distance < bestDistance - Epsilon)
                {
                    best = part;
                    bestDistance = distance;
                }
            }
            if (best < 0)
                throw new InvalidOperationException("Part prerequisites form a cycle.");
            placed[best] = true;
            order[step] = best;
            current = exits[best];
        }
        return order;
    }

    // Reverses order[i..j] when that shortens the open path and keeps every prerequisite earlier.
    // With exits, a leg runs from one part's exit to the next part's center, so the reversed run's
    // own legs change length too; without them they cancel and are left out, exactly as before.
    private static bool TwoOpt(int[] order, IReadOnlyList<Vector> centers, IReadOnlyList<Vector> exits, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, int[] position, CancellationToken token,
        Vector? endpoint = null)
    {
        var improved = false;
        var count = order.Length;
        var leave = exits ?? centers;
        // The terminal edge belongs to every delta: reversing the route's tail swaps which
        // endpoint-side center faces the fixed final position.
        double Tail(Vector from) => endpoint is { } e ? from.DistanceTo(e) : 0;
        for (var i = 0; i < count - 1; i++)
        {
            token.ThrowIfCancellationRequested();
            // The run's internal legs, forward and reversed, grown one part at a time.
            double forward = 0, reversed = 0;
            for (var j = i + 1; j < count; j++)
            {
                if (exits != null)
                {
                    forward += leave[order[j - 1]].DistanceTo(centers[order[j]]);
                    reversed += leave[order[j]].DistanceTo(centers[order[j - 1]]);
                }
                var before = Point(i - 1).DistanceTo(centers[order[i]]) + forward
                    + (j + 1 < count ? leave[order[j]].DistanceTo(centers[order[j + 1]]) : Tail(leave[order[j]]));
                var after = Point(i - 1).DistanceTo(centers[order[j]]) + reversed
                    + (j + 1 < count ? leave[order[i]].DistanceTo(centers[order[j + 1]]) : Tail(leave[order[i]]));
                if (after >= before - Epsilon || !CanReverse(order, i, j, prerequisites, position))
                    continue;
                Array.Reverse(order, i, j - i + 1);
                improved = true;
                // The run is reversed now; its legs are re-measured from scratch.
                forward = reversed = 0;
                for (var k = i + 1; k <= j && exits != null; k++)
                {
                    forward += leave[order[k - 1]].DistanceTo(centers[order[k]]);
                    reversed += leave[order[k]].DistanceTo(centers[order[k - 1]]);
                }
            }
        }
        return improved;

        Vector Point(int index) => index < 0 ? start : leave[order[index]];
    }

    // A reversal breaks a prerequisite only when both parts lie inside the reversed span.
    private static bool CanReverse(int[] order, int i, int j, IReadOnlyList<IReadOnlyCollection<int>> prerequisites,
        int[] position)
    {
        for (var k = 0; k < order.Length; k++)
            position[order[k]] = k;
        for (var k = i; k <= j; k++)
            foreach (var prerequisite in prerequisites[order[k]])
                if (position[prerequisite] >= i && position[prerequisite] <= j)
                    return false;
        return true;
    }

    // Moves a run of one to three parts to a later or earlier gap when that shortens the path.
    private static bool OrOpt(int[] order, IReadOnlyList<Vector> centers, IReadOnlyList<Vector> exits, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, int[] position, CancellationToken token,
        Vector? endpoint = null)
    {
        var improved = false;
        var count = order.Length;
        for (var length = 1; length <= 3; length++)
            for (var i = 0; i + length <= count; i++)
            {
                token.ThrowIfCancellationRequested();
                var last = i + length - 1;
                var removal = Gap(i - 1, i) + Gap(last, last + 1) - Gap(i - 1, last + 1);
                // Insert between order[gap - 1] and order[gap], outside the run.
                for (var gap = 0; gap <= count; gap++)
                {
                    if (gap >= i && gap <= last + 1)
                        continue;
                    var insertion = Gap(gap - 1, i) + Gap(last, gap) - Gap(gap - 1, gap);
                    if (insertion >= removal - Epsilon)
                        continue;
                    var candidate = Move(order, i, length, gap);
                    if (!Valid(candidate, prerequisites, position))
                        continue;
                    Array.Copy(candidate, order, count);
                    improved = true;
                    break;
                }
            }
        return improved;

        // Path length between order[a] and order[b] (a == -1 is the start; b == count is the open
        // end — the fixed endpoint when one was supplied, so the terminal edge is in every delta).
        double Gap(int a, int b)
        {
            var from = a < 0 ? start : exits[order[a]];
            if (b >= count)
                return endpoint is { } e ? from.DistanceTo(e) : 0;
            if (b < 0)
                return 0;
            return from.DistanceTo(centers[order[b]]);
        }
    }

    private static int[] Move(int[] order, int start, int length, int gap)
    {
        var run = order.Skip(start).Take(length).ToArray();
        var rest = order.Take(start).Concat(order.Skip(start + length)).ToList();
        var insertAt = gap > start ? gap - length : gap;
        rest.InsertRange(insertAt, run);
        return rest.ToArray();
    }

    internal static bool Valid(IReadOnlyList<int> order, IReadOnlyList<IReadOnlyCollection<int>> prerequisites,
        int[] position)
    {
        for (var k = 0; k < order.Count; k++)
            position[order[k]] = k;
        for (var k = 0; k < order.Count; k++)
            foreach (var prerequisite in prerequisites[order[k]])
                if (position[prerequisite] > k)
                    return false;
        return true;
    }
}

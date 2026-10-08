using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Proposes a whole-part visiting order as an open travelling-salesman path from the start point
/// over part centres: nearest neighbour, then 2-opt and Or-opt improvement, never placing a part
/// before one of its prerequisites. It is only a proposal; the forward search still checks every
/// rapid and lead along it.
/// </summary>
internal static class CuttingPartOrder
{
    private const double Epsilon = 1e-9;

    /// <summary>Upper bound on improvement passes, so a large plate cannot loop for long.</summary>
    internal const int MaxPasses = 50;

    /// <param name="parts">The part ordinals to order; prerequisites outside this set count as done.</param>
    /// <param name="centres">One representative point per part ordinal.</param>
    /// <param name="start">The modeled tool position before the first of these parts.</param>
    /// <param name="prerequisites">Ordinals that must come before each part; must be acyclic.</param>
    internal static int[] Plan(IReadOnlyList<int> parts, IReadOnlyList<Vector> centres, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token)
    {
        var local = new Dictionary<int, int>();
        for (var i = 0; i < parts.Count; i++)
            local.Add(parts[i], i);
        var localCentres = parts.Select(p => centres[p]).ToArray();
        var localPrerequisites = parts
            .Select(p => (IReadOnlyCollection<int>)prerequisites[p].Where(local.ContainsKey).Select(q => local[q]).ToArray())
            .ToArray();
        return Plan(localCentres, start, localPrerequisites, token).Select(i => parts[i]).ToArray();
    }

    /// <param name="centres">One representative point per part ordinal.</param>
    /// <param name="start">The modeled tool position before the first part.</param>
    /// <param name="prerequisites">Ordinals that must come before each part; must be acyclic.</param>
    /// <param name="endpoint">
    /// Optional fixed final position AFTER the last visited point (an open path with a closed
    /// terminal edge). The terminal edge joins EVERY improvement delta, not just final
    /// scoring. Null keeps the previous whole-part open-route semantics exactly.
    /// </param>
    internal static int[] Plan(IReadOnlyList<Vector> centres, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token, Vector? endpoint = null)
    {
        var count = centres.Count;
        var order = NearestNeighbour(centres, start, prerequisites, token);
        var position = new int[count];
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            token.ThrowIfCancellationRequested();
            var improved = TwoOpt(order, centres, start, prerequisites, position, token, endpoint);
            improved |= OrOpt(order, centres, start, prerequisites, position, token, endpoint);
            if (!improved)
                break;
        }
        return order;
    }

    private static int[] NearestNeighbour(IReadOnlyList<Vector> centres, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, CancellationToken token)
    {
        var count = centres.Count;
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
                var distance = current.DistanceTo(centres[part]);
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
            current = centres[best];
        }
        return order;
    }

    // Reverses order[i..j] when that shortens the open path and keeps every prerequisite earlier.
    private static bool TwoOpt(int[] order, IReadOnlyList<Vector> centres, Vector start,
        IReadOnlyList<IReadOnlyCollection<int>> prerequisites, int[] position, CancellationToken token,
        Vector? endpoint = null)
    {
        var improved = false;
        var count = order.Length;
        // The terminal edge belongs to every delta: reversing the route's tail swaps which
        // endpoint-side centre faces the fixed final position.
        double Tail(Vector from) => endpoint is { } e ? from.DistanceTo(e) : 0;
        for (var i = 0; i < count - 1; i++)
        {
            token.ThrowIfCancellationRequested();
            for (var j = i + 1; j < count; j++)
            {
                var before = Point(i - 1).DistanceTo(centres[order[i]])
                    + (j + 1 < count ? centres[order[j]].DistanceTo(centres[order[j + 1]]) : Tail(centres[order[j]]));
                var after = Point(i - 1).DistanceTo(centres[order[j]])
                    + (j + 1 < count ? centres[order[i]].DistanceTo(centres[order[j + 1]]) : Tail(centres[order[i]]));
                if (after >= before - Epsilon || !CanReverse(order, i, j, prerequisites, position))
                    continue;
                Array.Reverse(order, i, j - i + 1);
                improved = true;
            }
        }
        return improved;

        Vector Point(int index) => index < 0 ? start : centres[order[index]];
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
    private static bool OrOpt(int[] order, IReadOnlyList<Vector> centres, Vector start,
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
            var from = a < 0 ? start : centres[order[a]];
            if (b >= count)
                return endpoint is { } e ? from.DistanceTo(e) : 0;
            if (b < 0)
                return 0;
            return from.DistanceTo(centres[order[b]]);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Proposes a whole-part order for plates with parts nested inside other parts' cutouts. A host
/// is cut after everything inside it, so the tool reaches it from whichever inner part was cut
/// last; from the middle of a crowded cutout every straight approach crosses a finished
/// neighbour. Each host's inner parts are therefore toured as one run that ends beside the
/// host's own contours, immediately before the host. Plates without nested parts get exactly
/// <see cref="CuttingPartOrder"/>'s order. Like that order, this is only a proposal.
/// </summary>
internal static class CuttingNestedOrder
{
    /// <param name="Centres">One representative point per part ordinal.</param>
    /// <param name="Prerequisites">Ordinals that must come before each part, learned rules included.</param>
    /// <param name="Inside">Per part ordinal, the parts nested inside its cutouts at any depth.</param>
    /// <param name="HostEntry">The point on a host's contours nearest a tool position.</param>
    internal sealed record Context(IReadOnlyList<Vector> Centres,
        IReadOnlyList<IReadOnlyCollection<int>> Prerequisites, IReadOnlyList<IReadOnlyCollection<int>> Inside,
        Func<int, Vector, Vector> HostEntry, CancellationToken Token);

    /// <param name="parts">The part ordinals to order; prerequisites outside this set count as done.</param>
    /// <param name="start">The modeled tool position before the first of these parts.</param>
    /// <param name="endpoint">Optional position the run should end beside.</param>
    internal static int[] Plan(IReadOnlyList<int> parts, Vector start, Context context, Vector? endpoint = null)
    {
        context.Token.ThrowIfCancellationRequested();
        var set = parts.ToHashSet();
        var rootOf = new Dictionary<int, int>();
        foreach (var host in parts)
            foreach (var inner in context.Inside[host])
                if (set.Contains(inner) && !context.Inside[inner].Contains(host))
                    rootOf[inner] = host;
        if (rootOf.Count == 0)
            return Flat();
        // Follow nested hosts up to the outermost one present.
        foreach (var inner in rootOf.Keys.ToArray())
            while (rootOf.TryGetValue(rootOf[inner], out var outer))
                rootOf[inner] = outer;
        var roots = parts.Where(p => !rootOf.ContainsKey(p)).ToArray();
        // A rule between parts of two groups orders the groups.
        var groupRules = context.Centres.Select(_ => new HashSet<int>()).ToArray();
        foreach (var part in parts)
        {
            var group = rootOf.GetValueOrDefault(part, part);
            foreach (var prerequisite in context.Prerequisites[part])
                if (set.Contains(prerequisite) && rootOf.GetValueOrDefault(prerequisite, prerequisite) != group)
                    groupRules[group].Add(rootOf.GetValueOrDefault(prerequisite, prerequisite));
        }
        // A finished host is a large obstacle and can only be left across its own outside
        // edge, so the parts beside it go first. Rules that say otherwise win.
        var hostsLast = groupRules.Select(rules => new HashSet<int>(rules)).ToArray();
        var hosts = rootOf.Values.ToHashSet();
        foreach (var host in hosts)
            foreach (var root in roots)
                if (!hosts.Contains(root))
                    hostsLast[host].Add(root);
        int[] rootOrder;
        try
        {
            rootOrder = CuttingPartOrder.Plan(roots, context.Centres, start, hostsLast, context.Token, endpoint);
        }
        catch (InvalidOperationException)
        {
            try
            {
                rootOrder = CuttingPartOrder.Plan(roots, context.Centres, start, groupRules, context.Token, endpoint);
            }
            catch (InvalidOperationException)
            {
                return Flat(); // Rules that cross groups both ways cannot keep the groups whole.
            }
        }
        var order = new List<int>(parts.Count);
        var position = start;
        foreach (var root in rootOrder)
        {
            var inner = parts.Where(p => rootOf.TryGetValue(p, out var host) && host == root).ToArray();
            if (inner.Length != 0)
                order.AddRange(Plan(inner, position, context, context.HostEntry(root, position)));
            order.Add(root);
            position = context.Centres[root];
        }
        var placed = new HashSet<int>();
        foreach (var part in order)
        {
            if (context.Prerequisites[part].Any(p => set.Contains(p) && !placed.Contains(p)))
                return Flat();
            placed.Add(part);
        }
        return order.ToArray();

        int[] Flat() => CuttingPartOrder.Plan(parts, context.Centres, start, context.Prerequisites, context.Token, endpoint);
    }
}

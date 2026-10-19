using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Whole-part cutting prerequisites for one captured plate. A cutoff precedes every part its
/// nominal span crosses (an orphaned cutoff, whose definition is missing, precedes every part),
/// a part proven to lie inside a cutout of another part precedes that host, and a cut-first part
/// precedes every other part that need not precede it. Built on the
/// caller thread from owned values; ambiguous containment refuses instead of guessing from bounds.
/// </summary>
internal sealed class CuttingDependencyGraph
{
    private readonly int[][] prerequisites;

    private CuttingDependencyGraph(int[][] prerequisites) => this.prerequisites = prerequisites;

    internal static CuttingDependencyGraph Empty(int count) =>
        new(Enumerable.Range(0, count).Select(_ => Array.Empty<int>()).ToArray());

    /// <summary>Exact prerequisites, for tests and callers that already proved them.</summary>
    internal static CuttingDependencyGraph FromPrerequisites(IReadOnlyList<int[]> prerequisites)
    {
        var copy = prerequisites.Select(p => p.Distinct().Order().ToArray()).ToArray();
        if (copy.SelectMany(p => p).Any(i => i < 0 || i >= copy.Length))
            throw new ArgumentException("Prerequisite ordinal out of range.");
        return new(copy);
    }

    internal int Count => prerequisites.Length;

    internal IReadOnlyList<int> PrerequisitesOf(int ordinal) => prerequisites[ordinal];

    internal bool IsReady(int ordinal, IEnumerable<int> done)
    {
        if (prerequisites[ordinal].Length == 0)
            return true;
        var finished = done as ICollection<int> ?? done.ToHashSet();
        return prerequisites[ordinal].All(finished.Contains);
    }

    /// <summary>First (part, missing prerequisite) in a proposed complete order, or null.</summary>
    internal (int Part, int Prerequisite)? FirstViolation(IReadOnlyList<int> order)
    {
        var position = Enumerable.Repeat(-1, prerequisites.Length).ToArray();
        for (var i = 0; i < order.Count; i++)
            position[order[i]] = i;
        foreach (var part in order)
            foreach (var prerequisite in prerequisites[part])
                if (position[prerequisite] < 0 || position[prerequisite] > position[part])
                    return (part, prerequisite);
        return null;
    }

    /// <summary>An ordinal on a prerequisite cycle, or null when the graph is acyclic.</summary>
    internal int? FindCycle()
    {
        var remaining = prerequisites.Select(p => p.Length).ToArray();
        var dependents = Enumerable.Range(0, prerequisites.Length).Select(_ => new List<int>()).ToArray();
        for (var part = 0; part < prerequisites.Length; part++)
            foreach (var prerequisite in prerequisites[part])
                dependents[prerequisite].Add(part);
        var ready = new Queue<int>(Enumerable.Range(0, prerequisites.Length).Where(i => remaining[i] == 0));
        while (ready.Count != 0)
            foreach (var dependent in dependents[ready.Dequeue()])
                if (--remaining[dependent] == 0)
                    ready.Enqueue(dependent);
        var cyclic = Array.FindIndex(remaining, r => r != 0);
        return cyclic < 0 ? null : cyclic;
    }

    internal static CuttingDependencyGraph Build(IReadOnlyList<DependencyNode> nodes, Box plate,
        CancellationToken token, Action<CuttingDependencyException> uncertain = null)
    {
        var edges = nodes.Select(_ => new SortedSet<int>()).ToArray();
        for (var cut = 0; cut < nodes.Count; cut++)
        {
            if (!nodes[cut].IsCutOff)
                continue;
            if (nodes[cut].SeparatesSheet)
            {
                // Cutting a line that splits the sheet comes after every part and every other
                // cutoff, so the head never travels back across the separated edge. Such a line
                // is continuous, so it passes through no part and needs no crossing edge.
                for (var other = 0; other < nodes.Count; other++)
                    if (!nodes[other].SeparatesSheet)
                        edges[cut].Add(other);
                continue;
            }
            var definition = nodes[cut].CutOff;
            for (var part = 0; part < nodes.Count; part++)
            {
                token.ThrowIfCancellationRequested();
                if (!nodes[part].IsCutOff && (definition == null || CutOffCrosses(definition.Axis,
                    definition.Position, definition.StartLimit, definition.EndLimit, nodes[part].PlacedBounds, plate)))
                    edges[part].Add(cut);
            }
        }
        for (var inner = 0; inner < nodes.Count; inner++)
            for (var host = 0; host < nodes.Count; host++)
            {
                token.ThrowIfCancellationRequested();
                // Bounds only select candidates; containment itself is proven on native material.
                if (inner != host && !nodes[inner].IsCutOff && !nodes[host].IsCutOff
                    && NestedPartContainment.Within(nodes[inner].CleanBounds, nodes[host].HostBounds))
                {
                    try
                    {
                        if (InsideCutout(nodes, inner, host, token))
                            edges[host].Add(inner);
                    }
                    catch (CuttingDependencyException exception) when (uncertain != null
                        && exception.Status == CuttingPlanStatus.UnsupportedGeometry)
                    {
                        uncertain(exception);
                    }
                    catch (Exception exception) when (uncertain != null
                        && exception is ArgumentException or NotSupportedException)
                    {
                        uncertain(Ambiguous(inner, host, exception.Message));
                    }
                }
            }
        // Cut-first parts (test coupons) precede every other part, except one that must already
        // precede the coupon (nested in its cutout): containment wins over the preference.
        for (var first = 0; first < nodes.Count; first++)
        {
            if (!nodes[first].CutFirst)
                continue;
            var before = Ancestors(edges, first);
            for (var part = 0; part < nodes.Count; part++)
                if (!nodes[part].IsCutOff && !nodes[part].CutFirst && !before.Contains(part))
                    edges[part].Add(first);
        }
        var graph = new CuttingDependencyGraph(edges.Select(e => e.ToArray()).ToArray());
        if (graph.FindCycle() is { } cyclic)
            throw new CuttingDependencyException(CuttingPlanStatus.ConstraintConflict, cyclic, null,
                $"Part {cyclic + 1} is on a cutting dependency cycle.");
        return graph;
    }

    // Every ordinal that must come before the given one, directly or transitively.
    private static HashSet<int> Ancestors(IReadOnlyList<SortedSet<int>> edges, int ordinal)
    {
        var found = new HashSet<int>();
        var pending = new Stack<int>(edges[ordinal]);
        while (pending.Count != 0)
        {
            var next = pending.Pop();
            if (found.Add(next))
                foreach (var prerequisite in edges[next])
                    pending.Push(prerequisite);
        }
        return found;
    }

    /// <summary>
    /// Whether a cutoff's nominal line crosses a part's bounds within the cutoff's span. Uses the
    /// nominal line, not its trimmed cutting segments (which deliberately skip the parts). Bounds
    /// conservatively include edge contacts and concave recesses; limits prevent unrelated
    /// dependencies beyond the cutoff's span. Negative coordinates need no special case.
    /// </summary>
    internal static bool CutOffCrosses(CutOffAxis axis, Vector cutOffPosition, double? startLimit,
        double? endLimit, Box part, Box plate)
    {
        var vertical = axis == CutOffAxis.Vertical;
        var position = vertical ? cutOffPosition.X : cutOffPosition.Y;
        var acrossMin = vertical ? part.Left : part.Bottom;
        var acrossMax = vertical ? part.Right : part.Top;
        var alongMin = vertical ? part.Bottom : part.Left;
        var alongMax = vertical ? part.Top : part.Right;
        var start = startLimit ?? (vertical ? plate.Bottom : plate.Left);
        var end = endLimit ?? (vertical ? plate.Top : plate.Right);
        return !(position < acrossMin - Tolerance.Epsilon
            || position > acrossMax + Tolerance.Epsilon
            || System.Math.Max(start, end) < alongMin - Tolerance.Epsilon
            || System.Math.Min(start, end) > alongMax + Tolerance.Epsilon);
    }

    // True when the inner part's perimeter lies strictly inside one cutout of the host. Any
    // boundary contact or crossing between them, or incomplete material, is ambiguous.
    private static bool InsideCutout(IReadOnlyList<DependencyNode> nodes, int inner, int host,
        CancellationToken token) =>
        NestedPartContainment.InsideCutout(nodes[inner].Material, nodes[host].Material, token, out var ambiguity)
            ?? throw Ambiguous(inner, host, ambiguity);

    private static CuttingDependencyException Ambiguous(int inner, int host, string reason) =>
        new(CuttingPlanStatus.UnsupportedGeometry, inner, host,
            $"Containment of part {inner + 1} relative to part {host + 1} cannot be established: {reason}.");
}

/// <summary>Owned copy of a cutoff definition's nominal line.</summary>
internal sealed record CutOffDefinition(CutOffAxis Axis, Vector Position, double? StartLimit, double? EndLimit);

/// <summary>Owned per-placement dependency input; material is captured only when a pair needs it.</summary>
internal sealed class DependencyNode
{
    private readonly Func<LeadMaterialSnapshot> materialFactory;
    private LeadMaterialSnapshot material;

    internal DependencyNode(bool isCutOff, CutOffDefinition cutOff, Box placedBounds, Box cleanBounds,
        Func<LeadMaterialSnapshot> materialFactory, bool separatesSheet = false, bool cutFirst = false)
    {
        IsCutOff = isCutOff;
        CutFirst = !isCutOff && cutFirst;
        SeparatesSheet = isCutOff && separatesSheet;
        CutOff = cutOff;
        PlacedBounds = Copy(placedBounds);
        CleanBounds = cleanBounds == null ? null : Copy(cleanBounds);
        HostBounds = cleanBounds == null ? PlacedBounds : NestedPartContainment.Union(PlacedBounds, CleanBounds);
        this.materialFactory = materialFactory;
    }

    internal bool IsCutOff { get; }
    /// <summary>A cutoff whose placed program is one continuous cut across the whole sheet.</summary>
    internal bool SeparatesSheet { get; }
    /// <summary>A part whose drawing asks to be cut before the plate's other parts.</summary>
    internal bool CutFirst { get; }
    internal CutOffDefinition CutOff { get; }
    internal Box PlacedBounds { get; }
    internal Box CleanBounds { get; }
    internal Box HostBounds { get; }
    internal LeadMaterialSnapshot Material => material ??= materialFactory();

    private static Box Copy(Box box) => new(box.X, box.Y, box.Length, box.Width);
}

/// <summary>A dependency refusal naming the part and, when relevant, the other part.</summary>
internal sealed class CuttingDependencyException(CuttingPlanStatus status, int ordinal, int? other, string message)
    : Exception(message)
{
    internal CuttingPlanStatus Status { get; } = status;
    internal int Ordinal { get; } = ordinal;
    internal int? Other { get; } = other;
}

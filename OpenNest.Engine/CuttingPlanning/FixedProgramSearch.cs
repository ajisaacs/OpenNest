using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>Bounded forward depth-first search, nearest feasible branch then source ordinal.</summary>
internal static class FixedProgramSearch
{
    internal sealed record Outcome(CuttingPlanStatus Status, IReadOnlyList<int> Order,
        IReadOnlyList<PostVerificationFinding> Findings, int Expansions);

    internal static Outcome Run(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        var stack = new Stack<Frame>();
        stack.Push(Create([], snapshot.StartPoint, new ReleasedContourState()));
        var rejected = new HashSet<PostVerificationFinding>();
        var expansions = 0;
        while (stack.Count != 0)
        {
            token.ThrowIfCancellationRequested();
            var frame = stack.Peek();
            if (frame.Order.Length == snapshot.Placements.Count)
                return new(CuttingPlanStatus.Ready, frame.Order, [], expansions);
            if (frame.Next == frame.Candidates.Length)
            {
                stack.Pop();
                continue;
            }
            if (expansions == snapshot.ExpansionBudget)
                return new(CuttingPlanStatus.NoSolutionWithinBudget, [], rejected.ToArray(), expansions);
            var candidate = frame.Candidates[frame.Next++];
            expansions++;
            var placement = snapshot.Placements[candidate];
            var checker = frame.Checker.Copy();
            var findings = checker.Check(placement.Execution, frame.Position, placement.SourceOrdinal + 1,
                placement.IsCutOff, token);
            if (findings.Count != 0)
            {
                rejected.UnionWith(findings);
                continue;
            }
            stack.Push(Create([.. frame.Order, candidate], placement.Execution.DeparturePoint, checker));
        }
        return new(CuttingPlanStatus.ConstraintConflict, [], rejected.ToArray(), expansions);

        Frame Create(int[] order, Vector position, ReleasedContourState checker) => new(order, position, checker,
            Enumerable.Range(0, snapshot.Placements.Count).Where(index => !order.Contains(index)
                    && snapshot.Dependencies.IsReady(index, order)
                    && (!snapshot.PreservePartOrder || index == order.Length))
                .OrderBy(index => snapshot.Placements[index].Execution.RapidDistanceFrom(position))
                .ThenBy(index => snapshot.Placements[index].SourceOrdinal).ToArray());
    }

    private sealed class Frame(int[] order, Vector position, ReleasedContourState checker, int[] candidates)
    {
        internal int[] Order { get; } = order;
        internal Vector Position { get; } = position;
        internal ReleasedContourState Checker { get; } = checker;
        internal int[] Candidates { get; } = candidates;
        internal int Next { get; set; }
    }
}

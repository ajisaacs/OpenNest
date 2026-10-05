using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>One forward DFS over whole-part selection and emitted contour prefixes.</summary>
internal static class JointCuttingPlanSearch
{
    internal sealed record Outcome(CuttingPlanStatus Status, IReadOnlyList<FixedProgramPlacement> Order,
        IReadOnlyList<CuttingPlanFinding> Findings, int Expansions);

    internal static Outcome Run(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        var expansions = 0;
        var rejected = new List<CuttingPlanFinding>();
        var materials = snapshot.Placements.Where(p => !p.IsCutOff).Select(p => p.Material).ToArray();
        var stack = new Stack<Frame>();
        stack.Push(new(new([], snapshot.StartPoint, new ReleasedContourState(), null)));
        try
        {
            while (stack.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var frame = stack.Peek();
                var node = frame.Node;
                if (node.Order.Length == snapshot.Placements.Count)
                    return new(CuttingPlanStatus.Ready, node.Order, [], expansions);
                frame.Children ??= Expand(node).OrderBy(c => c.Distance)
                    .ThenBy(c => c.Ordinal).ThenBy(c => c.Contour).ThenBy(c => c.Entry).ToArray();
                if (frame.Next == frame.Children.Length)
                {
                    stack.Pop();
                    continue;
                }
                stack.Push(new(frame.Children[frame.Next++].Node));
            }
            // Entries are capped; exhaustion is not a proof over all possible entries.
            var status = snapshot.Placements.Any(p => p.Prepared != null)
                ? CuttingPlanStatus.NoSolutionWithinBudget
                : rejected.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict;
            return new(status, [], rejected.Distinct().ToArray(), expansions);
        }
        catch (BudgetExceededException)
        {
            return new(CuttingPlanStatus.NoSolutionWithinBudget, [], rejected.Distinct().ToArray(), expansions);
        }
        catch (OperationCanceledException)
        {
            return new(CuttingPlanStatus.Cancelled, [], [], expansions);
        }

        IEnumerable<Edge> Expand(Node node)
        {
            var sources = node.Active is { } active ? new[] { active.Source }
                : snapshot.Placements.Where(p => !node.Order.Any(o => o.SourceOrdinal == p.SourceOrdinal)
                    && snapshot.Dependencies.IsReady(p.SourceOrdinal, node.Order.Select(o => o.SourceOrdinal).ToArray())
                    && (!snapshot.PreservePartOrder || p.SourceOrdinal == node.Order.Length));
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                if (source.Prepared == null)
                {
                    CountExpansion(source);
                    var checker = node.Checker.Copy();
                    if (Check(source, source.Execution, node.Position, checker))
                        yield return new(new([.. node.Order, source], source.Execution.DeparturePoint, checker, null),
                            source.Execution.RapidDistanceFrom(node.Position), source.SourceOrdinal, -1, -1);
                    continue;
                }
                var prepared = source.Prepared;
                var choices = node.Active?.Choices ?? [];
                var arrival = node.Active?.Arrival ?? node.Position;
                var before = node.Active?.Before ?? node.Checker;
                var contours = choices.Length == prepared.Count - 1 ? new[] { prepared.PerimeterOrdinal }
                    : Enumerable.Range(0, prepared.PerimeterOrdinal).Where(c => !choices.Any(e => e.ContourOrdinal == c));
                foreach (var contour in contours)
                {
                    token.ThrowIfCancellationRequested();
                    var entries = prepared.Entries(contour, node.Position - source.Location, snapshot.MaxEntries, token);
                    for (var entry = 0; entry < entries.Count; entry++)
                    {
                        CountExpansion(source); // Before emission/native queries, including rejected candidates.
                        var prefix = choices.Append(entries[entry]).ToArray();
                        Program program;
                        OwnedExecution execution;
                        try
                        {
                            program = prefix.Length == prepared.Count ? prepared.Emit(prefix) : prepared.EmitPrefix(prefix);
                            execution = ExecutionMotionReader.Read(program, source.Location, arrival, token);
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or NotSupportedException)
                        {
                            rejected.Add(Finding(source, PostVerificationKind.Incomplete,
                                $"Contour {contour}, entry {entry} emission refused: {ex.Message}"));
                            continue; // Retain emitter semantics, never repair/shorten invalid lead styles.
                        }
                        // Prefix includes all earlier cuts and scribes. Replay from BEFORE the whole part.
                        var checker = before.Copy();
                        if (!Check(source, execution, arrival, checker)) continue;
                        var distance = execution.RapidDistanceFrom(arrival);
                        var next = prefix.Length == prepared.Count
                            ? new Node([.. node.Order, source.Propose(program, execution, prefix, token)], execution.DeparturePoint, checker, null)
                            : new Node(node.Order, execution.DeparturePoint, checker,
                                new(source, prefix, arrival, before, distance));
                        yield return new(next, distance - (node.Active?.Distance ?? 0), source.SourceOrdinal, contour, entry);
                    }
                }
            }
        }

        void CountExpansion(FixedProgramPlacement source)
        {
            token.ThrowIfCancellationRequested();
            if (expansions == snapshot.ExpansionBudget)
            {
                rejected.Add(Finding(source, null, $"Expansion budget {snapshot.ExpansionBudget} reached before the next candidate."));
                throw new BudgetExceededException();
            }
            expansions++;
            snapshot.ExpansionObserver?.Invoke(expansions);
            token.ThrowIfCancellationRequested();
        }

        bool Check(FixedProgramPlacement source, OwnedExecution execution, Vector arrival, ReleasedContourState checker)
        {
            var findings = checker.Check(execution, arrival, source.SourceOrdinal + 1, source.IsCutOff, token);
            rejected.AddRange(Map(snapshot, findings));
            // A fixed cutoff has no material or leads to certify; its rapids are still checked.
            var lead = source.IsCutOff ? new LeadPathValidationResult(true, true, null)
                : LeadPathValidator.Check(execution, source.Material, materials, token);
            if (!lead.IsComplete || !lead.IsClear)
                rejected.Add(Finding(source, lead.IsComplete ? null : PostVerificationKind.Incomplete, lead.Reason));
            return findings.Count == 0 && lead.IsComplete && lead.IsClear;
        }
    }

    internal static CuttingPlanFinding Finding(FixedProgramPlacement source, PostVerificationKind? kind, string message) =>
        new(source.SourceOrdinal, source.SourcePart, null, null, kind, message);

    internal static IEnumerable<CuttingPlanFinding> Map(CuttingPlanSnapshot snapshot, IEnumerable<PostVerificationFinding> findings) =>
        findings.Select(f =>
        {
            var source = f.PartNumber is { } p ? snapshot.Placements[p - 1] : null;
            var other = f.OtherPartNumber is { } o ? snapshot.Placements[o - 1] : null;
            return new CuttingPlanFinding(source?.SourceOrdinal, source?.SourcePart,
                other?.SourceOrdinal, other?.SourcePart, f.Kind, f.Message);
        });

    private sealed class BudgetExceededException : Exception;
    private sealed record ActivePart(FixedProgramPlacement Source, ContourChoice[] Choices, Vector Arrival,
        ReleasedContourState Before, double Distance);
    private sealed record Node(FixedProgramPlacement[] Order, Vector Position, ReleasedContourState Checker, ActivePart Active);
    private sealed record Edge(Node Node, double Distance, int Ordinal, int Contour, int Entry);
    private sealed class Frame(Node node)
    {
        internal Node Node { get; } = node;
        internal Edge[] Children { get; set; }
        internal int Next { get; set; }
    }
}

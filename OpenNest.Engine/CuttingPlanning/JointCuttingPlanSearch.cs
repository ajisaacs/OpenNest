using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Plans whole parts and their emitted contour prefixes along a part order. A preserved order is
/// followed with full backtracking. Otherwise the order comes from <see cref="CuttingPartOrder"/>;
/// when a part on it cannot be reached without crossing parts already cut, the search learns
/// "cut this part before those", keeps the parts cut before them and re-plans the rest.
/// </summary>
internal static class JointCuttingPlanSearch
{
    /// <summary>
    /// Expansions per entry and contour that a reordering attempt may spend without getting further
    /// along its order before it gives up and learns from the part that blocked it.
    /// </summary>
    internal const int StallExpansionsPerEntry = 8;

    internal sealed record Outcome(CuttingPlanStatus Status, IReadOnlyList<FixedProgramPlacement> Order,
        IReadOnlyList<CuttingPlanFinding> Findings, int Expansions);

    internal static Outcome Run(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        var walk = new Walk(snapshot, token);
        var count = snapshot.Placements.Count;
        try
        {
            if (snapshot.PreservePartOrder)
            {
                var kept = walk.Follow(Enumerable.Range(0, count).ToArray(), null, null);
                return kept.Order != null ? walk.Ready(kept.Order) : walk.Exhausted();
            }

            var centres = snapshot.Placements.Select(Centre).ToArray();
            var prerequisites = Enumerable.Range(0, count)
                .Select(i => new HashSet<int>(snapshot.Dependencies.PrerequisitesOf(i))).ToArray();
            var maxContours = snapshot.Placements.Max(p => p.Prepared?.Count ?? 1);
            var stall = StallExpansionsPerEntry * snapshot.MaxEntries * maxContours;
            var sequence = CuttingPartOrder.Plan(Enumerable.Range(0, count).ToArray(), centres,
                snapshot.StartPoint, prerequisites, token);
            Node resume = null;
            while (true)
            {
                var attempt = walk.Follow(sequence, stall, resume);
                if (attempt.Order != null)
                    return walk.Ready(attempt.Order);
                if (!Learn(attempt, prerequisites))
                    return walk.Exhausted();
                // Back up to just before the earliest part the blocked approach crossed, keep the
                // parts cut before it, and re-plan the rest from where the tool is at that point.
                var back = attempt.Crossed.Min(part => Array.IndexOf(sequence, part));
                resume = attempt.BoundaryAt(back);
                sequence = [.. sequence.Take(back), .. CuttingPartOrder.Plan(sequence[back..], centres,
                    resume.Position, prerequisites, token)];
            }
        }
        catch (BudgetExceededException)
        {
            return new(CuttingPlanStatus.NoSolutionWithinBudget, [], walk.Rejected, walk.Expansions);
        }
        catch (OperationCanceledException)
        {
            return new(CuttingPlanStatus.Cancelled, [], [], walk.Expansions);
        }
    }

    // "Cut the blocked part before every part whose cut contour its approach crossed", unless that
    // would contradict an order already required. False when nothing new was learned.
    private static bool Learn(Attempt attempt, HashSet<int>[] prerequisites)
    {
        if (attempt.Blocked is not int blocked)
            return false;
        var learned = false;
        foreach (var crossed in attempt.Crossed.Order())
        {
            if (crossed == blocked || prerequisites[crossed].Contains(blocked) || Precedes(crossed, blocked, prerequisites))
                continue;
            prerequisites[crossed].Add(blocked);
            learned = true;
        }
        return learned;
    }

    // True when 'first' must already come before 'second' through the prerequisite chain.
    private static bool Precedes(int first, int second, HashSet<int>[] prerequisites)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>(prerequisites[second]);
        while (pending.Count != 0)
        {
            var part = pending.Pop();
            if (part == first)
                return true;
            if (seen.Add(part))
                foreach (var prerequisite in prerequisites[part])
                    pending.Push(prerequisite);
        }
        return false;
    }

    // The centre of the part's placed cut material, for ordering only.
    private static Vector Centre(FixedProgramPlacement placement)
    {
        var cuts = placement.Execution.Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
            .Select(m => m.Curve.ToEntity().BoundingBox).ToList();
        return cuts.Count == 0 ? placement.Execution.DeparturePoint : cuts.GetBoundingBox().Center;
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

    /// <summary>
    /// The forward DFS over the next part on a given order and its emitted contour prefixes.
    /// Attempts share one expansion budget and one list of rejected findings.
    /// </summary>
    private sealed class Walk(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        private readonly List<CuttingPlanFinding> rejected = [];
        private readonly LeadMaterialSnapshot[] materials =
            snapshot.Placements.Where(p => !p.IsCutOff).Select(p => p.Material).ToArray();

        internal int Expansions { get; private set; }

        internal IReadOnlyList<CuttingPlanFinding> Rejected => rejected.Distinct().ToArray();

        internal Outcome Ready(IReadOnlyList<FixedProgramPlacement> order) =>
            new(CuttingPlanStatus.Ready, order, [], Expansions);

        // Entries are capped; exhaustion is not a proof over all possible entries.
        internal Outcome Exhausted()
        {
            var status = snapshot.Placements.Any(p => p.Prepared != null)
                ? CuttingPlanStatus.NoSolutionWithinBudget
                : rejected.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict;
            return new(status, [], Rejected, Expansions);
        }

        /// <summary>
        /// Follows <paramref name="sequence"/> from <paramref name="resume"/> (or the start point).
        /// With a stall limit the attempt ends once that many expansions pass without getting
        /// further along the order; it never backtracks behind its starting node.
        /// </summary>
        internal Attempt Follow(int[] sequence, int? stall, Node resume)
        {
            var root = resume ?? new Node([], snapshot.StartPoint, new ReleasedContourState(), null, null);
            var attempt = new Attempt(sequence);
            var progressExpansions = Expansions;
            var stack = new Stack<Frame>();
            stack.Push(new(root));
            while (stack.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var frame = stack.Peek();
                var node = frame.Node;
                if (node.Order.Length == snapshot.Placements.Count)
                {
                    attempt.Order = node.Order;
                    return attempt;
                }
                if (attempt.Advance(node))
                    progressExpansions = Expansions;
                else if (stall is int limit && Expansions - progressExpansions > limit)
                    return attempt;
                frame.Children ??= Expand(node, sequence, attempt).OrderBy(c => c.Distance)
                    .ThenBy(c => c.Ordinal).ThenBy(c => c.Contour).ThenBy(c => c.Entry).ToArray();
                if (frame.Next == frame.Children.Length)
                {
                    stack.Pop();
                    continue;
                }
                stack.Push(new(frame.Children[frame.Next++].Node));
            }
            return attempt;
        }

        private IEnumerable<Edge> Expand(Node node, int[] sequence, Attempt attempt)
        {
            IEnumerable<FixedProgramPlacement> sources;
            if (node.Active is { } active)
                sources = [active.Source];
            else
            {
                var finished = node.Order.Select(o => o.SourceOrdinal).ToHashSet();
                var next = snapshot.Placements[sequence[node.Order.Length]];
                sources = snapshot.Dependencies.IsReady(next.SourceOrdinal, finished) ? [next] : [];
            }
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                if (source.Prepared == null)
                {
                    CountExpansion(source);
                    var checker = node.Checker.Copy();
                    if (Check(source, source.Execution, node.Position, checker, attempt))
                        yield return new(new([.. node.Order, source], source.Execution.DeparturePoint, checker, null, node),
                            source.Execution.RapidDistanceFrom(node.Position), source.SourceOrdinal, -1, -1);
                    continue;
                }
                var prepared = source.Prepared;
                var choices = node.Active?.Choices ?? [];
                var arrival = node.Active?.Arrival ?? node.Position;
                var before = node.Active?.Before ?? node.Checker;
                var boundary = node.Active?.Boundary ?? node;
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
                        if (!Check(source, execution, arrival, checker, attempt)) continue;
                        var distance = execution.RapidDistanceFrom(arrival);
                        var next = prefix.Length == prepared.Count
                            ? new Node([.. node.Order, source.Propose(program, execution, prefix, token)],
                                execution.DeparturePoint, checker, null, boundary)
                            : new Node(node.Order, execution.DeparturePoint, checker,
                                new(source, prefix, arrival, before, distance, boundary), null);
                        yield return new(next, distance - (node.Active?.Distance ?? 0), source.SourceOrdinal, contour, entry);
                    }
                }
            }
        }

        private void CountExpansion(FixedProgramPlacement source)
        {
            token.ThrowIfCancellationRequested();
            if (Expansions == snapshot.ExpansionBudget)
            {
                rejected.Add(Finding(source, null, $"Expansion budget {snapshot.ExpansionBudget} reached before the next candidate."));
                throw new BudgetExceededException();
            }
            Expansions++;
            snapshot.ExpansionObserver?.Invoke(Expansions);
            token.ThrowIfCancellationRequested();
        }

        private bool Check(FixedProgramPlacement source, OwnedExecution execution, Vector arrival,
            ReleasedContourState checker, Attempt attempt)
        {
            var findings = checker.Check(execution, arrival, source.SourceOrdinal + 1, source.IsCutOff, token);
            rejected.AddRange(Map(snapshot, findings));
            attempt.NoteCrossings(source.SourceOrdinal, findings);
            // A fixed cutoff has no material or leads to certify; its rapids are still checked.
            var lead = source.IsCutOff ? new LeadPathValidationResult(true, true, null)
                : LeadPathValidator.Check(execution, source.Material, materials, token);
            if (!lead.IsComplete || !lead.IsClear)
                rejected.Add(Finding(source, lead.IsComplete ? null : PostVerificationKind.Incomplete, lead.Reason));
            return findings.Count == 0 && lead.IsComplete && lead.IsClear;
        }
    }

    /// <summary>One pass along an order: how far it got, which part stopped it and what that part crossed.</summary>
    private sealed class Attempt(int[] sequence)
    {
        private long progress = -1;

        internal IReadOnlyList<FixedProgramPlacement> Order { get; set; }

        /// <summary>The deepest whole-part boundary reached; its Previous chain leads back to the root.</summary>
        private Node Deepest { get; set; }

        /// <summary>The ordinal of the part the attempt could not get past, or null.</summary>
        internal int? Blocked => Deepest == null || Deepest.Order.Length >= sequence.Length ? null
            : sequence[Deepest.Order.Length];

        /// <summary>Parts whose completed contours the blocked part's motions crossed (it can be among them).</summary>
        internal HashSet<int> Crossed { get; } = [];

        // Records a node that gets further along the order than any before. True when it does.
        internal bool Advance(Node node)
        {
            var depth = (long)node.Order.Length * (int.MaxValue + 1L) + (node.Active?.Choices.Length ?? 0);
            if (depth <= progress)
                return false;
            progress = depth;
            if (node.Active == null)
            {
                Deepest = node;
                Crossed.Clear();
            }
            return true;
        }

        internal void NoteCrossings(int ordinal, IEnumerable<PostVerificationFinding> findings)
        {
            if (Blocked != ordinal)
                return;
            foreach (var finding in findings)
                if (finding.Kind == PostVerificationKind.RapidCrossing && finding.OtherPartNumber is int other)
                    Crossed.Add(other - 1);
        }

        /// <summary>The part boundary at <paramref name="depth"/> on the way to the deepest one.</summary>
        internal Node BoundaryAt(int depth)
        {
            var node = Deepest;
            while (node.Order.Length > depth)
                node = node.Previous;
            return node;
        }
    }

    private sealed class BudgetExceededException : Exception;
    private sealed record ActivePart(FixedProgramPlacement Source, ContourChoice[] Choices, Vector Arrival,
        ReleasedContourState Before, double Distance, Node Boundary);
    /// <summary>A search state; Previous links a whole-part boundary to the boundary before it.</summary>
    private sealed record Node(FixedProgramPlacement[] Order, Vector Position, ReleasedContourState Checker,
        ActivePart Active, Node Previous);
    private sealed record Edge(Node Node, double Distance, int Ordinal, int Contour, int Entry);
    private sealed class Frame(Node node)
    {
        internal Node Node { get; } = node;
        internal Edge[] Children { get; set; }
        internal int Next { get; set; }
    }
}

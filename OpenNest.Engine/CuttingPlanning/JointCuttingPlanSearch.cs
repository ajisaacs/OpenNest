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
/// "cut this part before those", keeps the parts cut before them and re-plans the rest. Once
/// nothing new can be learned, the remaining budget goes to a full search over every ready part.
/// </summary>
internal static class JointCuttingPlanSearch
{
    /// <summary>
    /// Expansions per entry and contour that a reordering attempt may spend without getting further
    /// along its order before it gives up and learns from the part that blocked it.
    /// </summary>
    internal const int StallExpansionsPerEntry = 8;

    /// <summary>LeadPrechecks counts S07 adapter evaluations — bounded work tracked separately from expansions.</summary>
    internal sealed record Outcome(CuttingPlanStatus Status, IReadOnlyList<FixedProgramPlacement> Order,
        IReadOnlyList<CuttingPlanFinding> Findings, int Expansions, int LeadPrechecks = 0);

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
                {
                    // "Cut before" rules are a heuristic: a part blocked straight after another can
                    // still be reachable via a third. Search every order with what budget remains.
                    var full = walk.Follow(null, null, null);
                    return full.Order != null ? walk.Ready(full.Order) : walk.Exhausted();
                }
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
            return new(CuttingPlanStatus.NoSolutionWithinBudget, [], walk.Rejected, walk.Expansions, walk.LeadPrechecks);
        }
        catch (OperationCanceledException)
        {
            return new(CuttingPlanStatus.Cancelled, [], [], walk.Expansions, walk.LeadPrechecks);
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
        // One feasibility adapter per source part per captured planning attempt: verdicts
        // memoize per owned choice for the attempt, never statically or across attempts.
        private readonly Dictionary<int, ContourEntryFeasibility> feasibility = [];
        private readonly HashSet<int> reportedNoFit = [];

        internal int Expansions { get; private set; }

        /// <summary>Lead precheck evaluations, tracked apart from DFS expansions: they are work, not free.</summary>
        internal int LeadPrechecks { get; private set; }

        internal IReadOnlyList<CuttingPlanFinding> Rejected => rejected.Distinct().ToArray();

        internal Outcome Ready(IReadOnlyList<FixedProgramPlacement> order) =>
            new(CuttingPlanStatus.Ready, order, [], Expansions, LeadPrechecks);

        // Entries are capped; exhaustion is not a proof over all possible entries.
        internal Outcome Exhausted()
        {
            var status = snapshot.Placements.Any(p => p.Prepared != null)
                ? CuttingPlanStatus.NoSolutionWithinBudget
                : rejected.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict;
            return new(status, [], Rejected, Expansions, LeadPrechecks);
        }

        /// <summary>
        /// Follows <paramref name="sequence"/> from <paramref name="resume"/> (or the start point);
        /// a null sequence tries every dependency-ready part, nearest first.
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
                frame.Children ??= OrderedChildren(node, sequence, attempt);
                if (frame.Next == frame.Children.Length)
                {
                    stack.Pop();
                    continue;
                }
                stack.Push(new(frame.Children[frame.Next++].Node));
            }
            return attempt;
        }

        /// <summary>
        /// The NEXT cut's centre that the outside entry should face, or null for the last
        /// part. Supplied order: the next not-yet-finished part in that order (the sequence
        /// is re-read after every learned-order replan). Sequence-free fallback: nearest
        /// dependency-ready remaining part once the current part counts as finished, stable
        /// ordinal ties. Never the current or a finished part. Global coordinates.
        /// </summary>
        private Vector? LookAheadCentre(Node node, int[] sequence, FixedProgramPlacement source)
        {
            var finished = node.Order.Select(o => o.SourceOrdinal).ToHashSet();
            finished.Add(source.SourceOrdinal);
            if (sequence != null)
                for (var i = node.Order.Length + 1; i < sequence.Length; i++)
                    if (!finished.Contains(sequence[i]))
                        return Centre(snapshot.Placements[sequence[i]]);
            var from = Centre(source);
            Vector? best = null;
            var bestDistance = double.PositiveInfinity;
            foreach (var candidate in snapshot.Placements
                         .Where(p => !finished.Contains(p.SourceOrdinal)
                             && snapshot.Dependencies.IsReady(p.SourceOrdinal, finished))
                         .OrderBy(p => p.SourceOrdinal))
            {
                var distance = Centre(candidate).DistanceTo(from);
                if (distance < bestDistance - 1e-9)
                {
                    bestDistance = distance;
                    best = Centre(candidate);
                }
            }
            return best;
        }

        /// <summary>
        /// Nearest-first BETWEEN source parts (fallback search keeps its tour), but inside
        /// one part and contour stage the automatic rank leads — OrderBy(Distance) alone
        /// would undo the look-ahead facing. Legacy (unranked) children keep distance order.
        /// </summary>
        private Edge[] OrderedChildren(Node node, int[] sequence, Attempt attempt)
        {
            var edges = Expand(node, sequence, attempt).ToList();
            if (edges.Count <= 1)
                return edges.ToArray();
            // Stable source order: the minimum incremental rapid per source, ties ordinal.
            var sourceOrder = edges.GroupBy(e => e.Ordinal)
                .OrderBy(g => g.Min(e => e.Distance)).ThenBy(g => g.Key)
                .SelectMany((g, rank) => g.Select(e => (Edge: e, Rank: rank)))
                .ToDictionary(x => x.Edge, x => x.Rank);
            return edges
                .OrderBy(e => sourceOrder[e])
                .ThenBy(e => e.Contour)
                .ThenBy(e => e.Rank)
                .ThenBy(e => e.Distance)
                .ThenBy(e => e.Entry)
                .ToArray();
        }

        private IEnumerable<Edge> Expand(Node node, int[] sequence, Attempt attempt)
        {
            IEnumerable<FixedProgramPlacement> sources;
            if (node.Active is { } active)
                sources = [active.Source];
            else
            {
                var finished = node.Order.Select(o => o.SourceOrdinal).ToHashSet();
                sources = sequence == null
                    ? snapshot.Placements.Where(p => !finished.Contains(p.SourceOrdinal)
                        && snapshot.Dependencies.IsReady(p.SourceOrdinal, finished))
                    : snapshot.Dependencies.IsReady(sequence[node.Order.Length], finished)
                        ? [snapshot.Placements[sequence[node.Order.Length]]] : [];
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
                    // No-hole parts: the single outside contour gets the automatic entry
                    // pipeline, facing the next cut (S03-S08). Holed parts keep the legacy
                    // nearest-entry path until S12 wires hole look-ahead.
                    var automatic = prepared.Count == 1;
                    IReadOnlyList<ContourChoice> entries;
                    if (!automatic)
                        entries = prepared.Entries(contour, node.Position - source.Location, snapshot.MaxEntries, token);
                    else
                    {
                        CountExpansion(source); // The catalogue/validator pipeline is search work, counted here.
                        entries = AutomaticEntries(node, sequence, source, contour);
                        if (entries.Count == 0)
                            continue; // the S08 finding (when complete) is already recorded
                    }
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
                        yield return new(next, distance - (node.Active?.Distance ?? 0), source.SourceOrdinal,
                            contour, entry, automatic ? entry : int.MinValue);
                    }
                }
            }
        }

        /// <summary>
        /// The S03-S08 pipeline for one outside contour, in prepared LOCAL coordinates
        /// converted exactly once: rank the (fallback-complemented) catalogue toward the
        /// next cut, lazily filter through the shared validator adapter, cap at MaxEntries
        /// with side coverage. Empty ONLY when the finite catalogue was fully evaluated and
        /// nothing fits — then the honest part/contour finding is recorded once.
        /// </summary>
        private IReadOnlyList<ContourChoice> AutomaticEntries(Node node, int[] sequence,
            FixedProgramPlacement source, int contour)
        {
            var prepared = source.Prepared!;
            var arrival = node.Position - source.Location;
            // One global->local conversion of target and arrival; geometry is already rotated.
            Vector? local = LookAheadCentre(node, sequence, source) is { } target
                ? target - source.Location : null;
            var preferred = prepared.AutomaticEntryCandidates(contour, token);
            var catalogue = preferred.Count > 0
                ? preferred
                : prepared.AutomaticEntryCandidatesWithFallbacks(contour, local, token);
            var ordered = catalogue.RankTowardNextCut(local, arrival);
            var adapter = feasibility.TryGetValue(source.SourceOrdinal, out var known)
                ? known
                : feasibility[source.SourceOrdinal] = new ContourEntryFeasibility(
                    prepared, source.Location, source.Material, materials);
            var before = adapter.EvaluationCount;
            var selection = ContourEntrySelection.Select(ordered,
                candidate => adapter.Check(candidate.Choice, token: token), snapshot.MaxEntries, token);
            LeadPrechecks += adapter.EvaluationCount - before;
            if (selection.Shortfall == ContourSelectionShortfall.Incomplete && selection.UncertainChoices.Count == 0)
                rejected.Add(Finding(source, PostVerificationKind.Incomplete,
                    $"Contour {contour}: {selection.Reason}"));
            else if (selection.Choices.Count == 0 && selection.UncertainChoices.Count == 0
                && reportedNoFit.Add(source.SourceOrdinal * 1000 + contour))
                rejected.Add(Finding(source, null,
                    $"No tested lead-in fits on part {source.SourceOrdinal}, contour {contour}: {selection.Reason}"));
            // Uncertain candidates are NOT refused by the precheck: they reach the emitted-
            // prefix Check and complete replay, which remain the authority on them.
            return selection.Choices.Count == 0
                ? selection.UncertainChoices
                : [.. selection.Choices, .. selection.UncertainChoices];
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
        internal int? Blocked => sequence == null || Deepest == null || Deepest.Order.Length >= sequence.Length ? null
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
    /// <summary>Rank is the automatic selection slot (entry order) inside its contour stage; int.MinValue for legacy children.</summary>
    private sealed record Edge(Node Node, double Distance, int Ordinal, int Contour, int Entry, int Rank = int.MinValue);
    private sealed class Frame(Node node)
    {
        internal Node Node { get; } = node;
        internal Edge[] Children { get; set; }
        internal int Next { get; set; }
    }
}

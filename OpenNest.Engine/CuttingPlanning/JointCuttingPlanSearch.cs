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
/// Plans whole parts and their emitted contour prefixes along a part order. Multi-part holed
/// requests first try one ranked hole chain per endpoint, then retain full backtracking.
/// A preserved order stays fixed. Otherwise the order comes from <see cref="CuttingPartOrder"/>;
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
    private const int MaximumRecoveryHoles = 16;

    /// <summary>LeadPrechecks counts S07 adapter evaluations — bounded work tracked separately from expansions.</summary>
    internal sealed record Outcome(CuttingPlanStatus Status, IReadOnlyList<FixedProgramPlacement> Order,
        IReadOnlyList<CuttingPlanFinding> Findings, int Expansions, int LeadPrechecks = 0);

    internal static Outcome Run(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        var walk = new Walk(snapshot, token);
        var count = snapshot.Placements.Count;
        var maxContours = snapshot.Placements.Max(p => p.Prepared?.Count ?? 1);
        var stall = StallExpansionsPerEntry * snapshot.MaxEntries * maxContours;
        var preferEndpoints = count > 1 && maxContours > 1;
        try
        {
            if (snapshot.PreservePartOrder)
            {
                var keptSequence = Enumerable.Range(0, count).ToArray();
                if (preferEndpoints)
                {
                    var preferred = walk.Follow(keptSequence, stall, null, preferredOnly: true);
                    if (preferred.Order != null)
                        return walk.Ready(preferred.Order);
                    var recovered = walk.Follow(keptSequence, stall, null, contourRecovery: true);
                    if (recovered.Order != null)
                        return walk.Ready(recovered.Order);
                }
                var kept = walk.Follow(keptSequence, null, null);
                return kept.Order != null ? walk.Ready(kept.Order) : walk.Exhausted();
            }

            var centres = snapshot.Placements.Select(Centre).ToArray();
            var prerequisites = Enumerable.Range(0, count)
                .Select(i => new HashSet<int>(snapshot.Dependencies.PrerequisitesOf(i))).ToArray();

            var nesting = NestedOrder(snapshot, centres, prerequisites, token);
            // A large aligned grid with no required order is toured row by row; anything else,
            // and every replan after a learned rule, gets the general order.
            var extents = snapshot.Placements.Select(CutExtent).ToArray();
            var sequence = (prerequisites.All(p => p.Count == 0) && extents.All(e => e != null)
                    && !snapshot.Placements.Any(p => p.IsCutOff)
                    ? CuttingGridOrder.Plan(extents, snapshot.StartPoint, token) : null)
                ?? CuttingNestedOrder.Plan(Enumerable.Range(0, count).ToArray(), snapshot.StartPoint, nesting);
            Node resume = null;
            while (true)
            {
                // A pass that teaches an order rule ends the round: the heavier passes would
                // only repeat the same blocked approach along the same order.
                Attempt attempt = null;
                foreach (var (preferredOnly, contourRecovery) in preferEndpoints
                    ? new[] { (true, false), (false, true), (false, false) } : [(false, false)])
                {
                    var pass = walk.Follow(sequence, stall, resume, preferredOnly, contourRecovery, prerequisites);
                    if (pass.Order != null)
                        return walk.Ready(pass.Order);
                    if (Learn(pass, prerequisites))
                    {
                        attempt = pass;
                        break;
                    }
                }
                if (attempt == null)
                {
                    // "Cut before" rules are a heuristic: a part blocked straight after another can
                    // still be reachable via a third. Search every order with what budget remains.
                    if (preferEndpoints)
                    {
                        var recovery = walk.Follow(null, stall, null, contourRecovery: true);
                        if (recovery.Order != null)
                            return walk.Ready(recovery.Order);
                    }
                    var full = walk.Follow(null, null, null);
                    return full.Order != null ? walk.Ready(full.Order) : walk.Exhausted();
                }
                // Back up to just before the earliest part the blocked approach crossed, keep the
                // parts cut before it, and re-plan the rest from where the tool is at that point.
                var back = attempt.Crossed.Min(part => Array.IndexOf(sequence, part));
                resume = attempt.BoundaryAt(back);
                sequence = [.. sequence.Take(back), .. CuttingNestedOrder.Plan(sequence[back..], resume.Position, nesting)];
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

    // Captured containment (never learned rules) and each host's placed cut contours.
    private static CuttingNestedOrder.Context NestedOrder(CuttingPlanSnapshot snapshot, Vector[] centres,
        HashSet<int>[] prerequisites, CancellationToken token)
    {
        var inside = snapshot.Placements.Select((placement, host) => placement.IsCutOff ? []
            : snapshot.Dependencies.PrerequisitesOf(host).Where(p => !snapshot.Placements[p].IsCutOff).ToArray()).ToArray();
        var contours = new Dictionary<int, Entity[]>();
        return new(centres, prerequisites, inside, (host, position) =>
        {
            if (!contours.TryGetValue(host, out var entities))
                contours[host] = entities = snapshot.Placements[host].Execution.Motions
                    .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
                    .Select(m => m.Curve.ToEntity()).ToArray();
            return entities.Length == 0 ? centres[host]
                : entities.Select(e => e.ClosestPointTo(position)).MinBy(point => point.DistanceTo(position));
        }, token);
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

    // True when a crossing of the blocked part's approach would teach a new order rule.
    private static bool CanLearn(Attempt attempt, HashSet<int>[] prerequisites) =>
        attempt.Blocked is int blocked && attempt.Crossed.Any(crossed => crossed != blocked
            && !prerequisites[crossed].Contains(blocked) && !Precedes(crossed, blocked, prerequisites));

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
    private static Vector Centre(FixedProgramPlacement placement) =>
        CutExtent(placement)?.Center ?? placement.Execution.DeparturePoint;

    // The extent of the part's placed cut material, or null when it has none.
    private static Box CutExtent(FixedProgramPlacement placement)
    {
        var cuts = placement.Execution.Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
            .Select(m => m.Curve.ToEntity().BoundingBox).ToList();
        return cuts.Count == 0 ? null : cuts.GetBoundingBox();
    }

    internal static CuttingPlanFinding Finding(FixedProgramPlacement source, PostVerificationKind? kind, string message) =>
        new(source.SourceOrdinal, source.SourcePart, null, null, kind, message);

    internal static IEnumerable<CuttingPlanFinding> Map(CuttingPlanSnapshot snapshot, IEnumerable<PostVerificationFinding> findings) =>
        findings.Select(f =>
        {
            var source = f.PartNumber is { } p ? snapshot.Placements[p - 1] : null;
            var other = f.OtherPartNumber is { } o ? snapshot.Placements[o - 1] : null;
            return new CuttingPlanFinding(source?.SourceOrdinal, source?.SourcePart,
                other?.SourceOrdinal, other?.SourcePart, f.Kind, f.Message)
            { Witness = f };
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
        // Lead/material verdicts for exact curves, shared by prechecks and prefix checks of this
        // walk: each prefix repeats its earlier contours' leads. Replay never uses it.
        private readonly LeadContactMemo leadContacts = new();
        private readonly HashSet<int> reportedNoFit = [];
        private readonly HashSet<int> reportedNoScrap = [];
        // The perimeter pierce-zone fallback's scrap geometry, built on first use.
        private ScrapPierceSearch scrapPierces;
        private readonly Dictionary<int, Entity[]> hostContours = [];

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
        /// With <paramref name="prerequisites"/>, an approach into a cutoff that crosses parts
        /// already cut ends the attempt at once when that teaches "cut it before them".
        /// </summary>
        internal Attempt Follow(int[] sequence, int? stall, Node resume, bool preferredOnly = false, bool contourRecovery = false,
            HashSet<int>[] prerequisites = null)
        {
            var root = resume ?? new Node([], snapshot.StartPoint, new ReleasedContourState(reportMissingLeadIns: false), null, null);
            var attempt = new Attempt(sequence);
            var progressExpansions = Expansions;
            var stack = new Stack<Frame>();
            stack.Push(new(root));
            try
            {
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
                    // A cutoff is a fixed program cut the same way wherever it goes, so replanning
                    // the parts before it can only change where its approach starts. Cutting it
                    // earlier never makes its own cut harder; learn that rather than stall here.
                    if (prerequisites != null && sequence != null && attempt.Blocked is int blocked
                        && snapshot.Placements[blocked] is { IsCutOff: true, SeparatesSheet: false }
                        && CanLearn(attempt, prerequisites))
                        return attempt;
                    if (attempt.Advance(node))
                        progressExpansions = Expansions;
                    else if (stall is int limit && Expansions - progressExpansions > limit)
                    {
                        if (sequence != null || !contourRecovery)
                            return attempt;
                        // Diversify the first whole part rather than spending the remainder
                        // on endpoints of a stalled root's already completed prefix.
                        while (stack.Count > 1)
                            stack.Pop().Children?.Dispose();
                        attempt.Restart();
                        progressExpansions = Expansions;
                        continue;
                    }
                    frame.Children ??= OrderedChildren(node, sequence, attempt, preferredOnly, contourRecovery).GetEnumerator();
                    if (!frame.Children.MoveNext())
                    {
                        stack.Pop().Children.Dispose();
                        continue;
                    }
                    stack.Push(new(frame.Children.Current.Node));
                }
                return attempt;
            }
            finally
            {
                // Ready, stalled, cancelled and budget-exhausted searches can all leave
                // suspended siblings. Release their captured prefixes on every exit.
                foreach (var frame in stack)
                    frame.Children?.Dispose();
            }
        }

        /// <summary>
        /// The NEXT cut's centre that the outside entry should face, or null for the last
        /// part. Supplied order: the next not-yet-finished part in that order (the sequence
        /// is re-read after every learned-order replan). Sequence-free fallback: nearest
        /// dependency-ready remaining part once the current part counts as finished, stable
        /// ordinal ties. Never the current or a finished part. Global coordinates.
        /// </summary>
        private Vector? LookAheadCentre(Node node, int[] sequence, FixedProgramPlacement source) =>
            NextCut(node, sequence, source) is { } next ? Facing(source, next) : null;

        private FixedProgramPlacement NextCut(Node node, int[] sequence, FixedProgramPlacement source)
        {
            var finished = node.Order.Select(o => o.SourceOrdinal).ToHashSet();
            finished.Add(source.SourceOrdinal);
            if (sequence != null)
                for (var i = node.Order.Length + 1; i < sequence.Length; i++)
                    if (!finished.Contains(sequence[i]))
                        return snapshot.Placements[sequence[i]];
            var from = Centre(source);
            FixedProgramPlacement best = null;
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
                    best = candidate;
                }
            }
            return best;
        }

        // Where the tool may have to go next, in global coordinates: the look-ahead point and,
        // for an ordinary next part, the corners of its cut extent. Its pierce can be anywhere
        // on it, so an outside entry that sees all of them leaves the most ways open.
        private Vector[] SightTargets(Node node, int[] sequence, FixedProgramPlacement source)
        {
            if (NextCut(node, sequence, source) is not { } next)
                return [];
            var facing = Facing(source, next);
            var cuts = next.Execution.Motions
                .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
                .Select(m => m.Curve.ToEntity().BoundingBox).ToList();
            if (cuts.Count == 0 || facing != Centre(next))
                return [facing];
            var box = cuts.GetBoundingBox();
            return [facing, new(box.Left, box.Bottom), new(box.Right, box.Bottom), new(box.Left, box.Top), new(box.Right, box.Top)];
        }

        // The next cut as seen from the current part. A host's centre lies in the middle of
        // its cutout, among the parts nested there; a part inside it leaves toward the
        // nearest of the host's own contours instead.
        private Vector Facing(FixedProgramPlacement source, FixedProgramPlacement next)
        {
            if (source.IsCutOff || next.IsCutOff
                || !snapshot.Dependencies.PrerequisitesOf(next.SourceOrdinal).Contains(source.SourceOrdinal))
                return Centre(next);
            if (!hostContours.TryGetValue(next.SourceOrdinal, out var contours))
                hostContours[next.SourceOrdinal] = contours = next.Execution.Motions
                    .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
                    .Select(m => m.Curve.ToEntity()).ToArray();
            var from = Centre(source);
            return contours.Length == 0 ? Centre(next)
                : contours.Select(c => c.ClosestPointTo(from)).MinBy(point => point.DistanceTo(from));
        }

        /// <summary>
        /// Nearest-first BETWEEN source parts (fallback search keeps its tour), but inside
        /// one part and contour stage the automatic rank leads — OrderBy(Distance) alone
        /// would undo the look-ahead facing. Legacy (unranked) children keep distance order.
        /// </summary>
        private IEnumerable<Edge> OrderedChildren(Node node, int[] sequence, Attempt attempt, bool preferredOnly, bool contourRecovery)
        {
            // A prescribed next part (or an active part's next contour) has a single
            // source. Expand already yields its contour/entry rank order, so emit and
            // validate a sibling only when DFS reaches it. Free whole-part selection
            // still needs every source's distances before it can rank them. Recovery
            // instead visits dependency-ready sources lazily by their material centres.
            if (sequence != null || node.Active != null || contourRecovery)
                return Expand(node, sequence, attempt, preferredOnly, contourRecovery);
            var edges = Expand(node, sequence, attempt, preferredOnly, contourRecovery).ToList();
            if (edges.Count <= 1)
                return edges.ToArray();
            // Stable source order: the minimum incremental rapid per source, ties ordinal.
            var sourceOrder = edges.GroupBy(e => e.Ordinal)
                .OrderBy(g => g.Min(e => e.Distance)).ThenBy(g => g.Key)
                .SelectMany((g, rank) => g.Select(e => (Edge: e, Rank: rank)))
                .ToDictionary(x => x.Edge, x => x.Rank);
            return edges
                .OrderBy(e => sourceOrder[e])
                .ThenBy(e => e.ContourRank)
                .ThenBy(e => e.Contour)
                .ThenBy(e => e.Rank)
                .ThenBy(e => e.Distance)
                .ThenBy(e => e.Entry)
                .ToArray();
        }

        private IEnumerable<Edge> Expand(Node node, int[] sequence, Attempt attempt, bool preferredOnly, bool contourRecovery)
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
            if (contourRecovery)
                sources = sources.OrderBy(p => Centre(p).DistanceTo(node.Position)).ThenBy(p => p.SourceOrdinal);
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
                if (contourRecovery && prepared.Count > 1 && prepared.PerimeterOrdinal <= MaximumRecoveryHoles && node.Active == null)
                {
                    foreach (var edge in RecoverPart(node, sequence, source, attempt))
                        yield return edge;
                    continue;
                }
                if (prepared.Count > 1 && node.Active == null)
                {
                    // Selecting an endpoint does not cut it. Each endpoint owns a separate
                    // branch, whose holes are still emitted first and checked normally.
                    CountExpansion(source);
                    var perimeters = AutomaticEntries(node, sequence, source, prepared.PerimeterOrdinal);
                    for (var index = 0; index < perimeters.Count; index++)
                    {
                        CountExpansion(source);
                        var state = new ActivePart(source, [], node.Position, node.Checker, 0, node,
                            perimeters[index]);
                        yield return new(new(node.Order, node.Position, node.Checker, state, null),
                            Centre(source).DistanceTo(node.Position), source.SourceOrdinal, -1, index, index);
                    }
                    continue;
                }
                var preference = node.Active?.Preference;
                if (node.Active?.Perimeter is { } outside && preference == null)
                    preference = PlanHoles(node.Active, outside);
                var choices = node.Active?.Choices ?? [];
                var arrival = node.Active?.Arrival ?? node.Position;
                var before = node.Active?.Before ?? node.Checker;
                var boundary = node.Active?.Boundary ?? node;
                var contours = choices.Length == prepared.Count - 1 ? new[] { prepared.PerimeterOrdinal }
                    : Enumerable.Range(0, prepared.PerimeterOrdinal).Where(c => !choices.Any(e => e.ContourOrdinal == c));
                // First try one ranked hole chain per endpoint. A later-part failure then
                // changes the endpoint before replaying all earlier hole combinations.
                // The retained pass below still searches every entry and hole order.
                if (preference != null)
                    contours = contours.OrderBy(c => Array.IndexOf(preference.Route, c)).ThenBy(c => c);
                if (preferredOnly && preference != null)
                    contours = contours.Take(1);
                foreach (var contour in contours)
                {
                    token.ThrowIfCancellationRequested();
                    IReadOnlyList<ContourChoice> entries;
                    var contourRank = 0;
                    if (node.Active?.Perimeter is { } perimeter)
                    {
                        if (contour == prepared.PerimeterOrdinal)
                            entries = [perimeter];
                        else
                        {
                            contourRank = Array.IndexOf(preference.Route, contour);
                            var downstream = preference.Route.Skip(contourRank + 1)
                                .FirstOrDefault(c => !choices.Any(e => e.ContourOrdinal == c), -1);
                            var target = downstream < 0 ? preference.PerimeterPierce
                                : preference.Pierces[downstream];
                            // The coarse pass already checked one owned, lead-feasible entry
                            // for this hole. Do not prepare every alternate before trying it.
                            // The subsequent full pass still enumerates and checks them all.
                            if (preferredOnly && preference.Entries.TryGetValue(contour, out var first))
                                entries = [first];
                            else
                            {
                                entries = SelectEntries(source, contour, target, node.Position - source.Location);
                                if (preference.Entries.TryGetValue(contour, out var preferred))
                                    entries = entries.OrderBy(e => e.Point.DistanceTo(preferred.Point)
                                        <= PostVerificationGeometry.Epsilon ? 0 : 1).ToArray();
                            }
                        }
                    }
                    else
                    {
                        CountExpansion(source);
                        entries = AutomaticEntries(node, sequence, source, contour);
                    }
                    if (entries.Count == 0)
                        continue;
                    var entryCount = preferredOnly && node.Active?.Perimeter != null ? System.Math.Min(1, entries.Count) : entries.Count;
                    for (var entry = 0; entry < entryCount; entry++)
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
                        if (!Check(source, execution, arrival, checker, attempt, prefix)) continue;
                        var distance = execution.RapidDistanceFrom(arrival);
                        var next = prefix.Length == prepared.Count
                            ? new Node([.. node.Order, source.Propose(program, execution, prefix, token)],
                                execution.DeparturePoint, checker, null, boundary)
                            : new Node(node.Order, execution.DeparturePoint, checker,
                                new(source, prefix, arrival, before, distance, boundary,
                                    node.Active?.Perimeter, preference), null);
                        yield return new(next, distance - (node.Active?.Distance ?? 0), source.SourceOrdinal,
                            contour, entry, entry, contourRank);
                    }
                }
            }
        }

        // Recovery only: solve directed contour transitions rather than assuming
        // that a centre-to-centre hole tour can cross the already released material.
        // One complete checked chain per outside endpoint; original DFS remains fallback.
        private IEnumerable<Edge> RecoverPart(Node node, int[] sequence,
            FixedProgramPlacement source, Attempt attempt)
        {
            var prepared = source.Prepared;
            var target = LookAheadCentre(node, sequence, source) - source.Location;
            // Placement origins are authored program coordinates, not a downstream cut.
            // Use the same material-centre look-ahead as the retained entry search.
            var ranked = prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal, token: token)
                .OrderBy(c => c.Choice.Point.DistanceTo(target ?? node.Position - source.Location)).ToArray();
            var perimeters = ranked.Where(c => Evaluate(source, c).Status == ContourFeasibilityStatus.Clear)
                .Select(c => c.Choice).ToArray();
            if (perimeters.Length == 0)
                perimeters = [.. ScrapPierceEntries(source, ranked, out _)];
            var holes = Enumerable.Range(0, prepared.PerimeterOrdinal)
                .Select(c => prepared.AutomaticEntryCandidatesWithFallbacks(c, token: token)
                    .Where(e => Evaluate(source, e).Status == ContourFeasibilityStatus.Clear).Select(e => e.Choice).ToArray()).ToArray();
            var motions = new Dictionary<ContourChoice, OwnedExecution>();
            var stop = Expansions + (long)StallExpansionsPerEntry * snapshot.MaxEntries * prepared.Count;
            // The part's scribes and other open cuts come once, before every contour; check
            // them from the arrival, then each contour from where the one before it left off.
            var openingProgram = prepared.EmitPrefix([]);
            var start = node.Position;
            var released = node.Checker;
            if (openingProgram.Length != 0)
            {
                CountExpansion(source);
                var opening = ExecutionMotionReader.Read(openingProgram, source.Location, node.Position, token);
                released = node.Checker.Copy();
                var findings = released.Check(opening, node.Position, source.SourceOrdinal + 1, token);
                rejected.AddRange(Map(snapshot, findings));
                attempt.NoteCrossings(source.SourceOrdinal, findings);
                if (findings.Count != 0) yield break;
                start = opening.DeparturePoint;
            }
            OwnedExecution Motion(ContourChoice choice)
            {
                if (motions.TryGetValue(choice, out var known)) return known;
                CountExpansion(source);
                return motions[choice] = WithoutScribes(ExecutionMotionReader.Read(
                    prepared.EmitCandidateForValidation(choice), source.Location, null, token));
            }
            var centres = prepared.HoleCentres(token);
            // Ranking hops reads every hole entry's motion: build them before the outside
            // entries share what is left of the allowance.
            foreach (var entry in holes.SelectMany(entries => entries))
            {
                if (Expansions >= stop) yield break;
                Motion(entry);
            }
            for (var endpoint = 0; endpoint < perimeters.Length; endpoint++)
            {
                token.ThrowIfCancellationRequested();
                if (Expansions >= stop) yield break;
                var outside = perimeters[endpoint];
                // Share what is left between the outside entries still to try: one that no hole
                // tour can reach must not spend the allowance of the entries after it.
                var limit = System.Math.Min(stop, Expansions + System.Math.Max(
                    (stop - Expansions) / (perimeters.Length - endpoint), (long)StallExpansionsPerEntry * prepared.Count));
                var chain = new List<ContourChoice>();
                // This dominance heuristic is confined to recovery: emitted
                // seams can differ for the same released subset. Never prune full DFS.
                var failed = new HashSet<(int Mask, long X, long Y)>();
                if (!Solve(0, start, released)) continue;
                var program = prepared.Emit(chain);
                var execution = ExecutionMotionReader.Read(program, source.Location, node.Position, token);
                var checker = node.Checker.Copy();
                CountExpansion(source);
                if (!Check(source, execution, node.Position, checker, attempt, chain)) continue;
                var proposal = source.Propose(program, execution, chain, token);
                yield return new(new([.. node.Order, proposal], execution.DeparturePoint, checker, null, node),
                    execution.RapidDistanceFrom(node.Position), source.SourceOrdinal, -1, endpoint, endpoint);

                bool Solve(int mask, Vector position, ReleasedContourState before)
                {
                    token.ThrowIfCancellationRequested();
                    if (Expansions >= limit) return false;
                    var key = (mask, BitConverter.DoubleToInt64Bits(position.X),
                        BitConverter.DoubleToInt64Bits(position.Y));
                    if (failed.Contains(key)) return false;
                    if (mask == (1 << prepared.PerimeterOrdinal) - 1)
                    {
                        CountExpansion(source);
                        var findings = before.Copy().Check(Motion(outside), position, source.SourceOrdinal + 1, token);
                        rejected.AddRange(Map(snapshot, findings));
                        attempt.NoteCrossings(source.SourceOrdinal, findings);
                        if (findings.Count != 0) { failed.Add(key); return false; }
                        chain.Add(outside);
                        return true;
                    }
                    var remaining = Enumerable.Range(0, holes.Length).Where(c => (mask & (1 << c)) == 0).ToArray();
                    // Nearest hole first, and its entries that leave toward the holes still to
                    // cut first: a hop must not run back across the hole just cut.
                    var candidates = remaining
                        .Where(c => holes[c].Length != 0)
                        .OrderBy(c => holes[c].Min(e => Motion(e).Motions[0].End.DistanceTo(position)))
                        .ThenBy(c => c)
                        .SelectMany(c => holes[c]
                            .OrderBy(e => Onward(Motion(e).DeparturePoint, c, remaining))
                            .ThenBy(e => Motion(e).Motions[0].End.DistanceTo(position)));
                    foreach (var choice in candidates)
                    {
                        token.ThrowIfCancellationRequested();
                        if (Expansions >= limit) return false;
                        CountExpansion(source);
                        var execution = Motion(choice);
                        var after = before.Copy();
                        var findings = after.Check(execution, position, source.SourceOrdinal + 1, token);
                        rejected.AddRange(Map(snapshot, findings));
                        attempt.NoteCrossings(source.SourceOrdinal, findings);
                        if (findings.Count != 0) continue;
                        chain.Add(choice);
                        if (Solve(mask | (1 << choice.ContourOrdinal), execution.DeparturePoint, after)) return true;
                        chain.RemoveAt(chain.Count - 1);
                    }
                    failed.Add(key);
                    return false;
                }

                // How far a departure is from the nearest hole still to cut, or else the outside entry.
                double Onward(Vector departure, int hole, int[] remaining)
                {
                    var onward = double.PositiveInfinity;
                    foreach (var other in remaining)
                        if (other != hole && centres[other] is { } centre)
                            onward = System.Math.Min(onward, departure.DistanceTo(centre + source.Location));
                    return double.IsPositiveInfinity(onward) ? departure.DistanceTo(Motion(outside).Motions[0].End) : onward;
                }
            }
        }

        // An isolated contour probe repeats the part's scribes first; keep the contour alone.
        private static OwnedExecution WithoutScribes(OwnedExecution execution)
        {
            var last = -1;
            for (var index = 0; index < execution.Motions.Count; index++)
                if (execution.Motions[index].Layer == LayerType.Scribe)
                    last = index;
            return last < 0 ? execution : new(execution.Motions.Skip(last + 1));
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
            return SelectEntries(source, contour, local, arrival, SightTargets(node, sequence, source));
        }

        private ContourEntryFeasibility Adapter(FixedProgramPlacement source) =>
            feasibility.TryGetValue(source.SourceOrdinal, out var known) ? known
                : feasibility[source.SourceOrdinal] = new ContourEntryFeasibility(
                    source.Prepared, source.Location, source.Material, materials, leadContacts);

        // With a pierce diameter, a lead can also fail because its pierce is too close to material.
        private string PierceZoneHint() => snapshot.PierceZoneRadius <= 0 ? ""
            : FormattableString.Invariant($" Each lead-in pierce must also keep {snapshot.PierceZoneRadius:0.####} ")
                + FormattableString.Invariant($"from all part material for the {snapshot.PierceZoneRadius * 2:0.####} ")
                + "pierce diameter: lengthen the lead-in or reduce the pierce diameter.";

        private ContourFeasibilityVerdict Evaluate(FixedProgramPlacement source, ContourEntryCandidate candidate) =>
            Evaluate(source, candidate.Choice);

        private ContourFeasibilityVerdict Evaluate(FixedProgramPlacement source, ContourChoice choice)
        {
            CountExpansion(source);
            var adapter = Adapter(source);
            var before = adapter.EvaluationCount;
            var verdict = adapter.Check(choice, token: token);
            LeadPrechecks += adapter.EvaluationCount - before;
            return verdict;
        }

        /// <summary>
        /// The pierce-zone fallback for a perimeter none of whose configured lead-ins fits: clear
        /// entries pierced in open scrap near the part's ranked entries (see
        /// <see cref="ScrapPierceSearch"/>), each checked by the lead validator like any other
        /// planner-chosen entry. Nothing without a pierce diameter or for a pinned perimeter.
        /// When no scrap near the part keeps the zone, the shortfall is reported once, with the
        /// clearest point found. <paramref name="reason"/> is the first refusal of a tried pierce.
        /// </summary>
        private IReadOnlyList<ContourChoice> ScrapPierceEntries(FixedProgramPlacement source,
            IReadOnlyList<ContourEntryCandidate> ranked, out string reason)
        {
            reason = null;
            var prepared = source.Prepared;
            if (snapshot.PierceZoneRadius <= 0 || prepared.IsPinned(prepared.PerimeterOrdinal)
                || source.Material is not { IsComplete: true })
                return [];
            CountExpansion(source);
            scrapPierces ??= new ScrapPierceSearch(materials, snapshot.PierceZoneRadius);
            var targets = ranked.Take(snapshot.MaxEntries).Select(c => c.Choice.Point + source.Location).ToArray();
            var found = scrapPierces.Find(source.Material, targets, snapshot.MaxEntries, token);
            var entries = new List<ContourChoice>();
            foreach (var pierce in found.BestEffort is { } best ? [best] : found.Pierces)
            {
                ContourChoice choice;
                try
                {
                    choice = prepared.ScrapPierceEntry(pierce - source.Location);
                }
                catch (ArgumentException ex)
                {
                    reason ??= ex.Message;
                    continue;
                }
                var verdict = Evaluate(source, choice);
                if (verdict.IsClear)
                    entries.Add(choice);
                else
                    reason ??= verdict.Reason;
            }
            if (entries.Count == 0 && found.BestEffort is { } shortfall
                && reportedNoScrap.Add(source.SourceOrdinal))
                rejected.Add(Finding(source, null, FormattableString.Invariant(
                        $"No open scrap within {ScrapPierceSearch.WindowMargin * snapshot.PierceZoneRadius:0.####} of the part ")
                    + FormattableString.Invariant(
                        $"keeps the {snapshot.PierceZoneRadius * 2:0.####} pierce diameter clear of part material: ")
                    + FormattableString.Invariant(
                        $"the clearest pierce, at ({shortfall.X:0.####}, {shortfall.Y:0.####}), keeps {found.Clearance:0.####} ")
                    + FormattableString.Invariant($"of the {snapshot.PierceZoneRadius:0.####} needed. ")
                    + "Space the parts farther apart or reduce the pierce diameter, then replan."));
            return entries;
        }

        private IReadOnlyList<ContourChoice> SelectEntries(FixedProgramPlacement source, int contour,
            Vector? target, Vector arrival, Vector[] sightTargets = null)
        {
            // Last contour has no downstream target, but still needs its exact native
            // closest-arrival fallback. Keep target null in the ranker: arrival is not
            // a next cut and must not acquire facing-side priority.
            var catalogue = source.Prepared.AutomaticEntryCandidatesWithFallbacks(contour, target ?? arrival, token);
            var ordered = catalogue.RankTowardNextCut(target, arrival);
            // With no lead-out the tool leaves the part from its outside entry. From inside a
            // slot or pocket the straight way to the next cut runs back over the part just
            // cut, so entries with the clearest way out lead. Order only: every entry stays.
            if (sightTargets is { Length: > 0 } && contour == source.Prepared.PerimeterOrdinal
                && source.Material is { IsComplete: true } material)
            {
                var perimeter = material.Rings[0];
                ordered = ordered.OrderBy(c => sightTargets.Count(sight => PostVerificationGeometry.Crosses(
                    c.Choice.Point + source.Location, sight, perimeter, token))).ToList();
            }
            // A hole is left where it was entered too: entries whose rapid to the next
            // pierce clears the hole itself lead.
            if (target is { } next && contour != source.Prepared.PerimeterOrdinal)
                ordered = ordered.DepartingClear(source.Prepared, next, token);
            var selection = ContourEntrySelection.Select(ordered,
                candidate => Evaluate(source, candidate), snapshot.MaxEntries, token);
            // No configured lead-in fits the perimeter: with a pierce diameter, try open scrap.
            string scrapReason = null;
            if (contour == source.Prepared.PerimeterOrdinal && selection.Choices.Count == 0
                && selection.UncertainChoices.Count == 0 && selection.Shortfall == ContourSelectionShortfall.Exhausted)
            {
                var scrap = ScrapPierceEntries(source, ordered, out scrapReason);
                if (scrap.Count != 0)
                    return scrap;
            }
            if (selection.Shortfall == ContourSelectionShortfall.Incomplete && selection.UncertainChoices.Count == 0)
                rejected.Add(Finding(source, PostVerificationKind.Incomplete,
                    $"Contour {contour}: {selection.Reason}"));
            else if (selection.Choices.Count == 0 && selection.UncertainChoices.Count == 0
                && reportedNoFit.Add(source.SourceOrdinal * 1000 + contour))
                rejected.Add(Finding(source, null,
                    $"No tested lead-in fits on cutting contour {contour + 1}: {selection.Reason} "
                    + "Try reducing the lead-in length in Cutting Settings...; if nearby parts obstruct the lead-in, "
                    + "space the parts farther apart." + PierceZoneHint()
                    + (scrapReason == null ? "" : $" A pierce in open scrap was also tried: {scrapReason}")
                    + " Replan to check the changes."));
            // Uncertain candidates are NOT refused by the precheck: they reach the emitted-
            // prefix Check and complete replay, which remain the authority on them.
            return selection.Choices.Concat(selection.UncertainChoices).Take(snapshot.MaxEntries).ToArray();
        }

        private HolePreference PlanHoles(ActivePart active, ContourChoice perimeter)
        {
            var source = active.Source;
            var prepared = source.Prepared;
            var holes = Enumerable.Range(0, prepared.PerimeterOrdinal).ToArray();
            var localArrival = active.Arrival - source.Location;
            var centres = CuttingHoleOrder.Centres(prepared, localArrival, token);
            var endpoint = perimeter.Point;
            var entries = new Dictionary<int, ContourChoice>();
            var pierces = holes.ToDictionary(h => h, h => centres[h]);
            var route = holes;
            try
            {
                CountExpansion(source);
                endpoint = PreferredContourEntries.Pierce(prepared, perimeter, token);
                route = CuttingHoleOrder.Plan(holes, centres, localArrival, endpoint, token).ToArray();
                var proposal = PreferredContourEntries.TryPlan(prepared, perimeter, route, centres,
                    localArrival, candidate => Evaluate(source, candidate), token);
                if (proposal.IsPreferred)
                    foreach (var choice in proposal.HoleChoices)
                    {
                        CountExpansion(source);
                        entries.Add(choice.ContourOrdinal, choice);
                        pierces[choice.ContourOrdinal] = PreferredContourEntries.Pierce(prepared, choice, token);
                    }
                else
                    rejected.Add(Finding(source, proposal.Shortfall == ContourSelectionShortfall.Incomplete
                        ? PostVerificationKind.Incomplete : null, proposal.Reason ?? "No preferred hole path."));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or NotSupportedException)
            {
                // A proposal is not a gate. Unknown emissions still reach prefix/final
                // replay through retained candidates; never label an uncertain probe clear.
                rejected.Add(Finding(source, PostVerificationKind.Incomplete,
                    $"Preferred hole path unavailable: {ex.Message}"));
            }
            return new(route, entries, pierces, endpoint);
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

        // choices: the contours a regenerated execution emits, so its pinned ones are known.
        private bool Check(FixedProgramPlacement source, OwnedExecution execution, Vector arrival,
            ReleasedContourState checker, Attempt attempt, IReadOnlyList<ContourChoice> choices = null)
        {
            // A fixed emission is accepted as given and never constrains the search: its own
            // violations neither reject this candidate nor teach an order rule. Final replay
            // reports them as warnings. Every planner-chosen motion stays strict.
            var isFixed = source.FixedMotions(execution, choices);
            var findings = checker.Check(execution, arrival, source.SourceOrdinal + 1, source.IsCutOff, isFixed, token).Strict;
            rejected.AddRange(Map(snapshot, findings));
            attempt.NoteCrossings(source.SourceOrdinal, findings);
            // A fixed cutoff has no material or leads to certify; its rapids are still checked.
            var lead = source.IsCutOff ? new LeadPathValidationResult(true, true, null)
                : LeadPathValidator.Check(execution, source.Material, materials, isFixed, [], token, leadContacts,
                    snapshot.PierceZoneRadius);
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

        internal void Restart()
        {
            progress = -1;
            Deepest = null;
            Crossed.Clear();
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
    // Every endpoint branch owns its preference; all retained entries/orders remain searchable.
    private sealed record HolePreference(int[] Route, IReadOnlyDictionary<int, ContourChoice> Entries,
        IReadOnlyDictionary<int, Vector> Pierces, Vector PerimeterPierce);
    private sealed record ActivePart(FixedProgramPlacement Source, ContourChoice[] Choices, Vector Arrival,
        ReleasedContourState Before, double Distance, Node Boundary,
        ContourChoice Perimeter = null, HolePreference Preference = null);
    /// <summary>A search state; Previous links a whole-part boundary to the boundary before it.</summary>
    private sealed record Node(FixedProgramPlacement[] Order, Vector Position, ReleasedContourState Checker,
        ActivePart Active, Node Previous);
    /// <summary>Rank is the automatic selection slot (entry order) inside its contour stage; int.MinValue for legacy children.</summary>
    private sealed record Edge(Node Node, double Distance, int Ordinal, int Contour, int Entry,
        int Rank = int.MinValue, int ContourRank = 0);
    private sealed class Frame(Node node)
    {
        internal Node Node { get; } = node;
        internal IEnumerator<Edge> Children { get; set; }
    }
}

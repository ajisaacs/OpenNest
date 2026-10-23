using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Direct XY completed-contour checker. This is not physical machine safety.</summary>
public sealed class ReleasedContourState
{
    private readonly List<Obstacle> obstacles = new();
    private readonly bool reportMissingLeadIns;

    public ReleasedContourState() : this(true) { }

    /// <param name="reportMissingLeadIns">Keep pre-post warnings by default. Planning
    /// may pierce directly on a contour and checks travel without requiring a lead.</param>
    public ReleasedContourState(bool reportMissingLeadIns)
    {
        this.reportMissingLeadIns = reportMissingLeadIns;
    }

    public ReleasedContourState Copy()
    {
        var copy = new ReleasedContourState(reportMissingLeadIns);
        copy.obstacles.AddRange(obstacles);
        return copy;
    }

    /// <summary>Consumes one whole owned program; part numbers are caller identity keys.</summary>
    public IReadOnlyList<PostVerificationFinding> Check(OwnedExecution execution, Vector? arrival,
        int partNumber, CancellationToken token = default) => Check(execution, arrival, partNumber, false, token);

    /// <summary>
    /// As the analyzer does for cutoffs: rapids are checked, but cutoff cuts need no lead-in and,
    /// being open, never become obstacles.
    /// </summary>
    public IReadOnlyList<PostVerificationFinding> Check(OwnedExecution execution, Vector? arrival,
        int partNumber, bool cutoff, CancellationToken token = default) =>
        Check(execution, arrival, partNumber, cutoff, null, token).Strict;

    /// <summary>
    /// As <see cref="Check(OwnedExecution, Vector?, int, bool, CancellationToken)"/>, but a
    /// finding whose offending motion <paramref name="isFixed"/> owns (by motion index) is
    /// returned apart: a fixed emission is accepted as given and only reported. The checker
    /// state advances exactly as it would without ownership.
    /// </summary>
    internal (IReadOnlyList<PostVerificationFinding> Strict, IReadOnlyList<PostVerificationFinding> Fixed) Check(
        OwnedExecution execution, Vector? arrival, int partNumber, bool cutoff, Func<int, bool> isFixed,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (arrival is { } point)
            PostVerificationGeometry.Validate(point);
        var moves = execution.Motions.ToArray();
        moves[0] = moves[0].WithStart(arrival);
        var findings = new List<PostVerificationFinding>();
        var fixedFindings = new List<PostVerificationFinding>();
        AnalyzeMoves(moves, cutoff, findings, 1, partNumber, token, isFixed, fixedFindings);
        return (findings.AsReadOnly(), fixedFindings.AsReadOnly());
    }

    internal void AnalyzeMoves(IReadOnlyList<ExecutionMotion> moves, bool cutoff,
        List<PostVerificationFinding> findings, int plate, int part, CancellationToken token,
        Func<int, bool> isFixed = null, List<PostVerificationFinding> fixedFindings = null,
        bool proveOpenCuts = false)
    {
        var current = new CutGroup();
        var unfinished = new List<CutGroup>();
        // Part-local cuts read only by the open-cut proof below: closed contours, break cuts
        // and leads no contour owns. Each lead belongs to the cut it enters or leaves.
        var closed = new List<CutGroup>();
        var otherOpen = new List<CutGroup>();
        var loose = new CutGroup();
        var leadIns = new List<PostVerificationGeometry.Curve>();
        CutGroup previous = null;
        var deferred = new List<(List<PostVerificationFinding> Target, int Index, PostVerificationFinding Finding)>();
        var hasLead = false;
        var contourNumber = 0;
        var inBreakCut = false;
        var reportedTab = false;
        for (var index = 0; index < moves.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var move = moves[index];
            var owned = isFixed?.Invoke(index) == true;
            if (!move.Rapid && move.Layer == LayerType.Break)
            {
                // A break cut is intentionally open and retained by its uncut tabs: it is never a
                // rapid obstacle and never an unexplained fragment, but it is still pierced.
                if (!inBreakCut)
                {
                    Finish();
                    if (reportMissingLeadIns && !cutoff && !hasLead)
                        Add(new(PostVerificationKind.MissingLeadIn, plate, part, null,
                            "A break cut is missing lead-in geometry.")
                        { Location = move.Curve.Start }, owned);
                    inBreakCut = true;
                    hasLead = false;
                    previous = new CutGroup();
                    previous.Leads.AddRange(leadIns);
                    leadIns.Clear();
                    otherOpen.Add(previous);
                }
                if (move.Curve.Length > PostVerificationGeometry.Epsilon)
                    previous.Curves.Add(move.Curve);
                continue;
            }
            inBreakCut = false;
            if (move.Rapid)
            {
                Finish();
                hasLead = false;
                loose.Leads.AddRange(leadIns);
                leadIns.Clear();
                previous = null;
                if (move.Start is not { } start || start.DistanceTo(move.End) <= PostVerificationGeometry.Epsilon)
                    continue;
                var reach = new PostVerificationGeometry.Extent(System.Math.Min(start.X, move.End.X),
                    System.Math.Min(start.Y, move.End.Y), System.Math.Max(start.X, move.End.X),
                    System.Math.Max(start.Y, move.End.Y));
                foreach (var obstacle in obstacles)
                {
                    token.ThrowIfCancellationRequested();
                    // A rapid well clear of a contour's extent can neither cross nor touch it.
                    if (reach.IsClearOf(obstacle.Extent))
                        continue;
                    if (PostVerificationGeometry.Crosses(start, move.End, obstacle.Curves, token))
                        Add(new(PostVerificationKind.RapidCrossing, plate, part, obstacle.Part,
                            $"Direct XY rapid crosses or touches completed untabbed contour {obstacle.Contour} " +
                            $"of part {obstacle.Part}.")
                        {
                            Location = (start + move.End) * 0.5,
                            RapidStart = start,
                            RapidEnd = move.End,
                            ContactPoints = PostVerificationGeometry.ContactPoints(start, move.End, obstacle.Curves, token)
                        }, owned);
                }
            }
            else if (move.Layer == LayerType.Leadin)
            {
                Finish();
                previous = null;
                hasLead |= move.Curve.Length > PostVerificationGeometry.Epsilon;
                if (move.Curve.Length > PostVerificationGeometry.Epsilon)
                    leadIns.Add(move.Curve);
            }
            else if (move.Layer is LayerType.Leadout or LayerType.Scribe)
            {
                if (move.Layer == LayerType.Leadout && move.Curve.Length > PostVerificationGeometry.Epsilon)
                {
                    // Withheld until the part ends, where the open-cut proof may show it cuts no gap.
                    if (current.Curves.Count > 0 && !PostVerificationGeometry.Closed(current.Curves))
                        Defer(new(PostVerificationKind.Incomplete, plate, part, null,
                            "A lead-out follows an open cutting contour and may cut through its retention gap. " +
                            "Rapid safety for that contour requires manual review."), owned);
                    (previous ?? loose).Leads.Add(move.Curve);
                }
                Finish();
                hasLead = false;
                if (move.Layer == LayerType.Scribe)
                    previous = null;
            }
            else if (move.Curve.Length > PostVerificationGeometry.Epsilon)
            {
                // A clean program's tab spans close their cutout but are not meant to be cut.
                if (move.Layer == LayerType.Tab && !reportedTab)
                {
                    reportedTab = true;
                    Add(new(PostVerificationKind.Incomplete, plate, part, null,
                        "A tabbed cutout still has its tabs as motions, so it may be cut free. " +
                        "Assign lead-ins or plan cutting to cut it in pieces around its tabs.")
                    { Location = move.Curve.Start }, owned);
                }
                if (current.Curves.Count == 0)
                {
                    contourNumber++;
                    if (reportMissingLeadIns && !cutoff && !hasLead)
                        Add(new(PostVerificationKind.MissingLeadIn, plate, part, null,
                            $"Cutting contour {contourNumber} is missing lead-in geometry.")
                        { Location = move.Curve.Start }, owned);
                    hasLead = false;
                    // A rapid can pause/reposition without leaving any material gap.
                    // Retain already-cut fragments, but do not turn them into obstacles
                    // until an actually continuous chain closes.
                    var resumed = unfinished.FindIndex(chain =>
                        chain.Curves[^1].End.DistanceTo(move.Curve.Start) <= PostVerificationGeometry.Epsilon);
                    if (resumed >= 0)
                    {
                        current = unfinished[resumed];
                        unfinished.RemoveAt(resumed);
                    }
                    current.Leads.AddRange(leadIns);
                    leadIns.Clear();
                }
                current.Curves.Add(move.Curve);
                current.Fixed |= owned;
                previous = current;
                // A completed contour becomes an obstacle immediately, not at part end.
                if (PostVerificationGeometry.Closed(current.Curves))
                    Finish();
            }
        }
        Finish();
        loose.Leads.AddRange(leadIns);
        // Open cuts that provably enclose nothing retain all their material, however many
        // pieces a tabbed cutout was cut in. Otherwise they need manual review. Planning proves
        // them too (its own tabbed cutouts are cut in pieces without lead-outs), but only
        // pre-post review lets the proof clear a lead-out: planning keeps refusing to certify
        // its own tabs followed by a lead-out.
        var retained = !cutoff && (unfinished.Count > 1 || deferred.Count > 0)
            && unfinished.All(LeadsClearOpenEnds)
            && OpenCutsEncloseNothing(closed, unfinished.Concat(otherOpen).Append(loose).ToList(), token);
        for (var i = deferred.Count - 1; i >= 0 && !(retained && proveOpenCuts); i--)
            deferred[i].Target.Insert(deferred[i].Index, deferred[i].Finding);
        // Only one open fragment per part can be the planner's own certified tab, so a
        // further fragment that a fixed emission cut makes this uncertainty that emission's.
        if (unfinished.Count > 1 && !retained)
            Add(new(PostVerificationKind.Incomplete, plate, part, null,
                "Multiple interrupted/open cutting fragments remain. Their combined cuts may release material; " +
                "they cannot be assumed to be retained by tabs. Review rapid travel manually."),
                unfinished.Any(chain => chain.Fixed));

        void Add(PostVerificationFinding finding, bool fixedEmission)
        {
            if (fixedEmission && fixedFindings != null)
                fixedFindings.Add(finding);
            else
                findings.Add(finding);
        }

        // Records the finding's place among the others, in case it is reported after all.
        void Defer(PostVerificationFinding finding, bool fixedEmission)
        {
            var target = fixedEmission && fixedFindings != null ? fixedFindings : findings;
            deferred.Add((target, target.Count, finding));
        }

        void Finish()
        {
            if (current.Curves.Count == 0)
                return;
            // A real uncut gap leaves the contour attached. CuttingParameters can be stale;
            // no flag or tab configuration is used as evidence of retention.
            if (!cutoff && PostVerificationGeometry.Closed(current.Curves))
            {
                obstacles.Add(new(part, contourNumber, current.Curves.ToArray()));
                closed.Add(current);
            }
            else if (!cutoff)
                unfinished.Add(current);
            current = new CutGroup();
        }
    }

    /// <summary>
    /// Whether one part's open cuts, with their leads, can release no material. A closed
    /// contour with its own leads already encloses whatever it releases and counts as one node.
    /// Open cuts may touch any cut only at their own ends, and must form no loop through those
    /// nodes or each other: a forest of open paths separates no region of the sheet. Kerf and
    /// tab strength are not modelled, exactly as for a single open contour.
    /// </summary>
    private const double JointTolerance = 1e-5;

    /// <summary>
    /// Whether every lead at an open end of <paramref name="group"/> stays out of the uncut gap
    /// beyond it: a lead-out never reaches ahead of the end it leaves, and a lead-in never
    /// behind the start it enters. A lead that curls into the gap can cut the tab with its kerf
    /// even where its centerline does not touch the next cut.
    /// </summary>
    private static bool LeadsClearOpenEnds(CutGroup group)
    {
        var start = group.Curves[0].Start;
        var end = group.Curves[^1].End;
        var behindStart = group.Curves[0].Tangent(false) * -1;
        var aheadOfEnd = group.Curves[^1].Tangent(true);
        foreach (var lead in group.Leads)
        {
            if (Touches(lead, start) && lead.Reach(start, behindStart) > PostVerificationGeometry.Epsilon)
                return false;
            if (Touches(lead, end) && lead.Reach(end, aheadOfEnd) > PostVerificationGeometry.Epsilon)
                return false;
        }
        return true;

        static bool Touches(PostVerificationGeometry.Curve lead, Vector point) =>
            lead.Start.DistanceTo(point) <= PostVerificationGeometry.Epsilon
            || lead.End.DistanceTo(point) <= PostVerificationGeometry.Epsilon;
    }

    private static bool OpenCutsEncloseNothing(IReadOnlyList<CutGroup> closed, IReadOnlyList<CutGroup> open,
        CancellationToken token)
    {
        var edges = open.SelectMany(group => group.Curves.Concat(group.Leads)).ToList();
        var nodes = new DisjointSet(closed.Count + 2 * edges.Count);
        int Node(int edge, bool end) => closed.Count + 2 * edge + (end ? 1 : 0);
        try
        {
            for (var i = 0; i < closed.Count; i++)
                for (var j = 0; j < i; j++)
                    if (Touch(closed[i], closed[j], token))
                        nodes.Union(i, j);
            for (var a = 0; a < edges.Count; a++)
            {
                var edge = edges[a];
                if (edge.Start.DistanceTo(edge.End) <= PostVerificationGeometry.Epsilon)
                    return false;
                for (var g = 0; g < closed.Count; g++)
                {
                    foreach (var other in closed[g].Curves.Concat(closed[g].Leads))
                    {
                        token.ThrowIfCancellationRequested();
                        if (edge.Extent.IsClearOf(other.Extent))
                            continue;
                        foreach (var point in Contacts(edge, other))
                        {
                            if (EndAt(edge, point, other) is not { } end)
                                return false;
                            nodes.Union(Node(a, end), g);
                        }
                    }
                }
                for (var b = a + 1; b < edges.Count; b++)
                {
                    token.ThrowIfCancellationRequested();
                    var other = edges[b];
                    if (edge.Extent.IsClearOf(other.Extent))
                        continue;
                    foreach (var point in Contacts(edge, other))
                    {
                        if (EndAt(edge, point, other) is not { } end || EndAt(other, point, edge) is not { } otherEnd)
                            return false;
                        nodes.Union(Node(a, end), Node(b, otherEnd));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or ArithmeticException)
        {
            return false;
        }
        var forest = new DisjointSet(closed.Count + 2 * edges.Count);
        for (var a = 0; a < edges.Count; a++)
        {
            var start = nodes.Find(Node(a, false));
            var end = nodes.Find(Node(a, true));
            if (forest.Find(start) == forest.Find(end))
                return false;
            forest.Union(start, end);
        }
        return true;

        static IReadOnlyList<Vector> Contacts(PostVerificationGeometry.Curve a, PostVerificationGeometry.Curve b)
        {
            var points = a.Contacts(b, out var overlap);
            if (overlap)
                throw new ArgumentException("Cuts share a span.");
            return points;
        }

        // Native contacts at a near-tangent joint (a lead or line meeting an arc) can land
        // slightly off the vertex the two cuts share; that is still their shared end.
        static bool? EndAt(PostVerificationGeometry.Curve curve, Vector point, PostVerificationGeometry.Curve other)
        {
            foreach (var end in new[] { false, true })
            {
                var vertex = end ? curve.End : curve.Start;
                if (point.DistanceTo(vertex) <= PostVerificationGeometry.Epsilon
                    || point.DistanceTo(vertex) <= JointTolerance
                    && (vertex.DistanceTo(other.Start) <= PostVerificationGeometry.Epsilon
                        || vertex.DistanceTo(other.End) <= PostVerificationGeometry.Epsilon))
                    return end;
            }
            return null;
        }

        static bool Touch(CutGroup a, CutGroup b, CancellationToken token)
        {
            foreach (var curve in a.Curves.Concat(a.Leads))
                foreach (var other in b.Curves.Concat(b.Leads))
                {
                    token.ThrowIfCancellationRequested();
                    if (!curve.Extent.IsClearOf(other.Extent) && Contacts(curve, other).Count > 0)
                        return true;
                }
            return false;
        }
    }

    /// <summary>One contour's cut motions with the leads entering and leaving it.</summary>
    private sealed class CutGroup
    {
        internal List<PostVerificationGeometry.Curve> Curves { get; } = new();
        internal List<PostVerificationGeometry.Curve> Leads { get; } = new();
        internal bool Fixed { get; set; }
    }

    private sealed class DisjointSet(int count)
    {
        private readonly int[] parents = Enumerable.Range(0, count).ToArray();

        internal int Find(int node)
        {
            while (parents[node] != node)
                node = parents[node] = parents[parents[node]];
            return node;
        }

        internal void Union(int a, int b) => parents[Find(a)] = Find(b);
    }

    private sealed record Obstacle(int Part, int Contour, IReadOnlyList<PostVerificationGeometry.Curve> Curves)
    {
        internal PostVerificationGeometry.Extent Extent { get; } =
            Curves.Aggregate(PostVerificationGeometry.Extent.None, (extent, curve) => extent.Union(curve.Extent));
    }
}

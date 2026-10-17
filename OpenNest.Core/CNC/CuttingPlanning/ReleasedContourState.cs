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
        Func<int, bool> isFixed = null, List<PostVerificationFinding> fixedFindings = null)
    {
        var contour = new List<PostVerificationGeometry.Curve>();
        var contourFixed = false;
        var unfinished = new List<(PostVerificationGeometry.Curve[] Curves, bool Fixed)>();
        var hasLead = false;
        var contourNumber = 0;
        var inBreakCut = false;
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
                            "A break cut has no nonzero placed lead-in motion.")
                        { Location = move.Curve.Start }, owned);
                    inBreakCut = true;
                    hasLead = false;
                }
                continue;
            }
            inBreakCut = false;
            if (move.Rapid)
            {
                Finish();
                hasLead = false;
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
                hasLead |= move.Curve.Length > PostVerificationGeometry.Epsilon;
            }
            else if (move.Layer is LayerType.Leadout or LayerType.Scribe)
            {
                if (move.Layer == LayerType.Leadout && contour.Count > 0
                    && !PostVerificationGeometry.Closed(contour)
                    && move.Curve.Length > PostVerificationGeometry.Epsilon)
                    Add(new(PostVerificationKind.Incomplete, plate, part, null,
                        "A lead-out follows an open cutting contour and may cut through its retention gap. " +
                        "Rapid safety for that contour requires manual review."), owned);
                Finish();
                hasLead = false;
            }
            else if (move.Curve.Length > PostVerificationGeometry.Epsilon)
            {
                if (contour.Count == 0)
                {
                    contourNumber++;
                    if (reportMissingLeadIns && !cutoff && !hasLead)
                        Add(new(PostVerificationKind.MissingLeadIn, plate, part, null,
                            $"Cutting contour {contourNumber} has no nonzero placed lead-in motion.")
                        { Location = move.Curve.Start }, owned);
                    hasLead = false;
                    // A rapid can pause/reposition without leaving any material gap.
                    // Retain already-cut fragments, but do not turn them into obstacles
                    // until an actually continuous chain closes.
                    var previous = unfinished.FindIndex(chain =>
                        chain.Curves[^1].End.DistanceTo(move.Curve.Start) <= PostVerificationGeometry.Epsilon);
                    if (previous >= 0)
                    {
                        contour.AddRange(unfinished[previous].Curves);
                        contourFixed |= unfinished[previous].Fixed;
                        unfinished.RemoveAt(previous);
                    }
                }
                contour.Add(move.Curve);
                contourFixed |= owned;
                // A completed contour becomes an obstacle immediately, not at part end.
                if (PostVerificationGeometry.Closed(contour))
                    Finish();
            }
        }
        Finish();
        // Only one open fragment per part can be the planner's own certified tab, so a
        // further fragment that a fixed emission cut makes this uncertainty that emission's.
        if (unfinished.Count > 1)
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

        void Finish()
        {
            if (contour.Count == 0)
                return;
            // A real uncut gap leaves the contour attached. CuttingParameters can be stale;
            // no flag or tab configuration is used as evidence of retention.
            if (!cutoff && PostVerificationGeometry.Closed(contour))
                obstacles.Add(new(part, contourNumber, contour.ToArray()));
            else if (!cutoff)
                unfinished.Add((contour.ToArray(), contourFixed));
            contour.Clear();
            contourFixed = false;
        }
    }

    private sealed record Obstacle(int Part, int Contour, IReadOnlyList<PostVerificationGeometry.Curve> Curves)
    {
        internal PostVerificationGeometry.Extent Extent { get; } =
            Curves.Aggregate(PostVerificationGeometry.Extent.None, (extent, curve) => extent.Union(curve.Extent));
    }
}

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

    public ReleasedContourState Copy()
    {
        var copy = new ReleasedContourState();
        copy.obstacles.AddRange(obstacles);
        return copy;
    }

    /// <summary>Consumes one whole owned program; part numbers are caller identity keys.</summary>
    public IReadOnlyList<PostVerificationFinding> Check(OwnedExecution execution, Vector? arrival,
        int partNumber, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (arrival is { } point)
            PostVerificationGeometry.Validate(point);
        var moves = execution.Motions.ToArray();
        moves[0] = moves[0].WithStart(arrival);
        var findings = new List<PostVerificationFinding>();
        AnalyzeMoves(moves, false, findings, 1, partNumber, token);
        return findings.AsReadOnly();
    }

    internal void AnalyzeMoves(IReadOnlyList<ExecutionMotion> moves, bool cutoff,
        List<PostVerificationFinding> findings, int plate, int part, CancellationToken token)
    {
        var contour = new List<PostVerificationGeometry.Curve>();
        var unfinished = new List<PostVerificationGeometry.Curve[]>();
        var hasLead = false;
        var contourNumber = 0;
        foreach (var move in moves)
        {
            token.ThrowIfCancellationRequested();
            if (move.Rapid)
            {
                Finish();
                hasLead = false;
                if (move.Start is not { } start || start.DistanceTo(move.End) <= PostVerificationGeometry.Epsilon)
                    continue;
                foreach (var obstacle in obstacles)
                {
                    token.ThrowIfCancellationRequested();
                    if (PostVerificationGeometry.Crosses(start, move.End, obstacle.Curves, token))
                        findings.Add(new(PostVerificationKind.RapidCrossing, plate, part, obstacle.Part,
                            $"Direct XY rapid crosses or touches completed untabbed contour {obstacle.Contour} " +
                            $"of part {obstacle.Part}."));
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
                    findings.Add(new(PostVerificationKind.Incomplete, plate, part, null,
                        "A lead-out follows an open cutting contour and may cut through its retention gap. " +
                        "Rapid safety for that contour requires manual review."));
                Finish();
                hasLead = false;
            }
            else if (move.Curve.Length > PostVerificationGeometry.Epsilon)
            {
                if (contour.Count == 0)
                {
                    contourNumber++;
                    if (!cutoff && !hasLead)
                        findings.Add(new(PostVerificationKind.MissingLeadIn, plate, part, null,
                            $"Cutting contour {contourNumber} has no nonzero placed lead-in motion."));
                    hasLead = false;
                    // A rapid can pause/reposition without leaving any material gap.
                    // Retain already-cut fragments, but do not turn them into obstacles
                    // until an actually continuous chain closes.
                    var previous = unfinished.FindIndex(chain =>
                        chain[^1].End.DistanceTo(move.Curve.Start) <= PostVerificationGeometry.Epsilon);
                    if (previous >= 0)
                    {
                        contour.AddRange(unfinished[previous]);
                        unfinished.RemoveAt(previous);
                    }
                }
                contour.Add(move.Curve);
                // A completed contour becomes an obstacle immediately, not at part end.
                if (PostVerificationGeometry.Closed(contour))
                    Finish();
            }
        }
        Finish();
        if (unfinished.Count > 1)
            findings.Add(new(PostVerificationKind.Incomplete, plate, part, null,
                "Multiple interrupted/open cutting fragments remain. Their combined cuts may release material; " +
                "they cannot be assumed to be retained by tabs. Review rapid travel manually."));

        void Finish()
        {
            if (contour.Count == 0)
                return;
            // A real uncut gap leaves the contour attached. CuttingParameters can be stale;
            // no flag or tab configuration is used as evidence of retention.
            if (!cutoff && PostVerificationGeometry.Closed(contour))
                obstacles.Add(new(part, contourNumber, contour.ToArray()));
            else if (!cutoff)
                unfinished.Add(contour.ToArray());
            contour.Clear();
        }
    }

    private sealed record Obstacle(int Part, int Contour, IReadOnlyList<PostVerificationGeometry.Curve> Curves);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// A deliberately uncertified fallback, not a relaxed safety checker. Reuses closed native
/// contour preparation and emission, retaining source part order except for proven
/// prerequisites; each part's holes follow a proposed tour, its perimeter last.
/// Unknown program semantics, invalid motions and unrepresentable contours still refuse.
/// </summary>
internal static class BestEffortCuttingPlan
{
    internal static CuttingPlanResult Plan(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.Failure is { } failure)
            return new(failure, findings: snapshot.Findings);
        if (!snapshot.Regeneration || snapshot.OwnedParameters == null)
            return new(CuttingPlanStatus.InvalidInput);
        var findings = snapshot.Findings.ToList();
        findings.Add(new(null, null, null, null, PostVerificationKind.Incomplete,
            "Best-effort plan: review lead clearance, rapid travel, material containment and source "
            + "contours as part of a complete review before cutting."));
        var order = new List<FixedProgramPlacement>();
        var done = new HashSet<int>();
        var position = snapshot.StartPoint;
        var distance = 0.0;
        var checker = new ReleasedContourState(reportMissingLeadIns: false);
        try
        {
            while (order.Count < snapshot.Placements.Count)
            {
                token.ThrowIfCancellationRequested();
                var source = snapshot.PreservePartOrder ? snapshot.Placements[order.Count]
                    : snapshot.Placements.FirstOrDefault(p => !done.Contains(p.SourceOrdinal)
                        && snapshot.Dependencies.IsReady(p.SourceOrdinal, done));
                if (source == null || !snapshot.Dependencies.IsReady(source.SourceOrdinal, done))
                    return new(CuttingPlanStatus.ConstraintConflict, findings: findings);
                var proposal = source;
                if (source.Prepared is { } prepared)
                {
                    var choices = new List<ContourChoice>();
                    var approach = position - source.Location;
                    // Source order is not a route: tour the holes from the arrival (the same
                    // nearest-neighbor + 2-opt proposal the search uses) so the rapids between
                    // them do not double back across each other. The perimeter is still last.
                    var holes = Enumerable.Range(0, prepared.PerimeterOrdinal).ToArray();
                    var route = CuttingHoleOrder.Plan(holes, CuttingHoleOrder.Centers(prepared, approach, token),
                        approach, null, token);
                    foreach (var contour in prepared.HasCutPerimeter ? route.Append(prepared.PerimeterOrdinal) : route)
                    {
                        token.ThrowIfCancellationRequested();
                        choices.Add(prepared.ClosestEntry(contour, approach));
                        // Prefixes are standalone programs, not relative to the prior prefix.
                        var prefix = ExecutionMotionReader.ReadSupported(prepared.EmitPrefix(choices),
                            source.Location, position, token);
                        approach = prefix.DeparturePoint - source.Location;
                    }
                    var program = prepared.Emit(choices);
                    var emitted = ExecutionMotionReader.ReadSupported(program, source.Location, position, token);
                    proposal = source.Propose(program, emitted, choices, token);
                }
                // Read the owned payload again; malformed/nonfinite output never reaches Apply.
                var execution = ExecutionMotionReader.ReadSupported(proposal.CopyProgram(), source.Location, position, token);
                if (!execution.HasCuttingContour)
                    throw new ArgumentException("Best-effort output has no cutting contour.");
                findings.AddRange(checker.Check(execution, position, source.SourceOrdinal + 1, source.IsCutOff, token)
                    .Select(f => new CuttingPlanFinding(f.PartNumber is int part ? part - 1 : null,
                        f.PartNumber is int ordinal ? snapshot.Placements[ordinal - 1].SourcePart : null,
                        f.OtherPartNumber is int other ? other - 1 : null,
                        f.OtherPartNumber is int otherOrdinal ? snapshot.Placements[otherOrdinal - 1].SourcePart : null,
                        f.Kind, f.Message)
                    { Witness = f }));
                if (SupportTabCapture.Review(source, proposal.ContourChoices) is { } unsteady)
                    findings.Add(unsteady);
                findings.AddRange(SupportTabCapture.Notes(proposal));
                distance += execution.RapidDistanceFrom(position);
                if (!double.IsFinite(distance))
                    throw new ArgumentException("Best-effort travel is not finite.");
                position = execution.DeparturePoint;
                done.Add(source.SourceOrdinal);
                order.Add(proposal);
            }
            token.ThrowIfCancellationRequested();
            findings.AddRange(SupportTabCapture.Skipped(snapshot));
            return new(CuttingPlanStatus.BestEffort, order, findings, rapidDistance: distance)
            { Snapshot = snapshot };
        }
        catch (OperationCanceledException)
        {
            return new(CuttingPlanStatus.Canceled);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or InvalidOperationException or ArithmeticException)
        {
            findings.Add(new(null, null, null, null, PostVerificationKind.Incomplete, exception.Message));
            return new(CuttingPlanStatus.UnsupportedGeometry, findings: findings);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;

namespace OpenNest.Diagnostics;

/// <summary>
/// Pre-post review of a plate's common-line groups (<see cref="Plate.CommonLines"/>): every member
/// still shares a span one kerf from another member, the group's kerf is still the plate's, and no
/// member's lead runs into a partner one kerf away (a lead on a shared edge would cut the partner).
/// Reports only; nothing is repaired, and an unreadable lead check is incomplete, never clear.
/// </summary>
internal static class CommonLineReview
{
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken token)
    {
        if (plate.CommonLines is not { Count: > 0 } groups)
            return;
        var plateKerf = plate.CuttingParameters?.Kerf is double k && double.IsFinite(k) && k > 0 ? k : (double?)null;
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var first = Number(plate, group.Members[0]);
            if (plateKerf is { } kerf && System.Math.Abs(kerf - group.Kerf) > CommonLineGeometry.GapTolerance)
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, first, null,
                    $"Common line was placed for a {group.Kerf:0.####} kerf, but the plate now cuts with a "
                    + $"{kerf:0.####} kerf. Rejoin the parts at the current kerf before posting."));
            var check = CommonLineGeometry.Check(group);
            foreach (var problem in check.Problems)
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, Number(plate, problem.Part), null,
                    $"'{problem.Part.BaseDrawing.Name}' {problem.Message}")
                { Location = problem.Part.BoundingBox?.Center });
            Leads(plate, plateNumber, group, findings, token);
        }
    }

    private static void Leads(Plate plate, int plateNumber, CommonLineGroup group,
        List<PostVerificationFinding> findings, CancellationToken token)
    {
        var members = group.Members;
        var materials = new LeadMaterialSnapshot[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                materials[i] = LeadMaterialSnapshot.Capture(FixedLeadReview.Clean(members[i]), members[i].Location, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                materials[i] = null;
            }
            if (materials[i] is not { IsComplete: true })
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, Number(plate, members[i]), null,
                    "Common-line lead check: " + (materials[i]?.Reason ?? "the clean material could not be captured.")));
        }
        for (var i = 0; i < members.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (materials[i] is not { IsComplete: true } own)
                continue;
            var partners = Enumerable.Range(0, members.Count)
                .Where(j => j != i && materials[j] is { IsComplete: true }).ToArray();
            OwnedExecution execution;
            try
            {
                execution = ExecutionMotionReader.Read(members[i].Program, members[i].Location, null, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                continue; // The rapid check reports the unreadable program.
            }
            var failures = new List<FixedLeadFailure>();
            var result = LeadPathValidator.Check(execution, own, partners.Select(j => materials[j]).ToArray(),
                _ => true, failures, token);
            if (!result.IsComplete)
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, Number(plate, members[i]), null,
                    "Common-line lead check: " + result.Reason));
            // Only partners matter here: a lead into the part's own material is the lead checks' to report.
            foreach (var failure in failures.Where(f => f.Obstacle is int))
            {
                var partner = members[partners[failure.Obstacle!.Value]];
                var move = execution.Motions[failure.Motion];
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, Number(plate, members[i]),
                    Number(plate, partner),
                    $"A lead of '{members[i].BaseDrawing.Name}' runs into '{partner.BaseDrawing.Name}', its "
                    + "common-line partner one kerf away. Move the lead off the shared edge (Plan Cutting does).")
                { Location = move.Curve?.Midpoint ?? move.End });
            }
        }
    }

    private static int? Number(Plate plate, Part part)
    {
        var index = plate.Parts.IndexOf(part);
        return index < 0 ? null : index + 1;
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}

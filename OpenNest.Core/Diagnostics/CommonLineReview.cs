using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// Pre-post review of a plate's common-line groups (<see cref="Plate.CommonLines"/>): every member
/// still shares a span one kerf from another member, the group's kerf is still the plate's, and no
/// member's lead or shared cut runs into a partner one kerf away (a lead on a shared edge would cut
/// the partner). A plate that cuts its common lines once must cut each shared span exactly once,
/// on its centerline, by the member cut first; one set to cut them twice must have no shared cuts.
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
            if (check.IsIntact)
                SharedCuts(plate, plateNumber, group, check, findings, token);
        }
    }

    // The shared cuts the members' programs make, against the spans the group derives now.
    private static void SharedCuts(Plate plate, int plateNumber, CommonLineGroup group, CommonLineCheck check,
        List<PostVerificationFinding> findings, CancellationToken token)
    {
        var cuts = new List<(Part Part, ExecutionMotion Motion)>();
        foreach (var member in group.Members)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                cuts.AddRange(ExecutionMotionReader.Read(member.Program, member.Location, null, token).Motions
                    .Where(motion => !motion.Rapid && motion.Layer == LayerType.CommonLine && motion.Curve != null)
                    .Select(motion => (member, motion)));
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                // The rapid check reports the unreadable program.
            }
        }
        if (plate.CutCommonLinesTwice)
        {
            if (cuts.Count > 0)
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, Number(plate, cuts[0].Part), null,
                    "The plate is set to cut its common lines twice, but this part's program cuts a shared line "
                    + "once. Plan cutting again before posting.")
                { Location = cuts[0].Motion.Curve.Midpoint });
            return;
        }
        foreach (var span in check.Spans)
        {
            token.ThrowIfCancellationRequested();
            var covering = cuts.Where(cut => Covers(cut.Motion, span)).ToList();
            var first = Number(plate, span.First);
            var second = Number(plate, span.Second);
            var middle = (span.Start + span.End) * 0.5;
            if (covering.Count == 0)
            {
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, first, second,
                    "This common line is not cut once on its centerline. Plan cutting cuts each shared line once; "
                    + "or set the plate to cut its common lines twice.")
                { Location = middle });
                continue;
            }
            if (covering.Count > 1)
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, first, second,
                    "This common line is cut more than once. Plan cutting again before posting.")
                { Location = middle });
            // The partner's remainder ends in this kerf, so the part that cuts it must come first.
            var owner = covering[0].Part;
            var partner = ReferenceEquals(owner, span.First) ? span.Second : span.First;
            if (Number(plate, owner) is int o && Number(plate, partner) is int p && o > p)
                findings.Add(new(PostVerificationKind.CommonLine, plateNumber, o, p,
                    "The shared line is cut after the partner whose remainder ends in its kerf. Plan cutting "
                    + "again, or move the part that cuts it ahead of its partner.")
                { Location = middle });
        }
    }

    // A shared cut on the span's centerline that covers the whole span.
    private static bool Covers(ExecutionMotion motion, CommonLineSpan span)
    {
        var curve = motion.Curve;
        if (curve.IsFiniteArc)
            return false;
        var length = span.Length;
        var along = (span.End - span.Start) * (1 / length);
        double Offset(Vector point) => System.Math.Abs((point.X - span.Start.X) * along.Y - (point.Y - span.Start.Y) * along.X);
        double At(Vector point) => (point.X - span.Start.X) * along.X + (point.Y - span.Start.Y) * along.Y;
        var tolerance = CommonLineGeometry.GapTolerance;
        if (Offset(curve.Start) > tolerance || Offset(curve.End) > tolerance)
            return false;
        var from = System.Math.Min(At(curve.Start), At(curve.End));
        var to = System.Math.Max(At(curve.Start), At(curve.End));
        return from <= tolerance && to >= length - tolerance;
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
                    Number(plate, partner), move.Layer == LayerType.CommonLine
                        ? $"A shared cut of '{members[i].BaseDrawing.Name}' runs into '{partner.BaseDrawing.Name}', its "
                            + "common-line partner. Rejoin the parts one kerf apart and plan cutting again."
                        : $"A lead of '{members[i].BaseDrawing.Name}' runs into '{partner.BaseDrawing.Name}', its "
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

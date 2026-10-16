using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Pre-post review of fixed emissions: Plan Cutting accepts a locked program's leads as given
/// and warns (D1a), so posting checks them again against every part's clean material on the
/// plate. Reports only; nothing is repaired and an incomplete check is never a clear result.
/// </summary>
internal static class FixedLeadReview
{
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken token)
    {
        var parts = plate.Parts;
        var reviewed = Enumerable.Range(0, parts.Count)
            .Where(i => parts[i] is { LeadInsLocked: true, BaseDrawing.IsCutOff: false }).ToArray();
        if (reviewed.Length == 0)
            return;
        var materials = new LeadMaterialSnapshot[parts.Count];
        var uncaptured = new List<int>();
        for (var index = 0; index < parts.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var part = parts[index];
            if (part?.BaseDrawing?.Program == null || part.BaseDrawing.IsCutOff)
                continue;
            try
            {
                // Scribe-only drawings are marks, not material.
                if (!ExecutionMotionReader.Read(part.BaseDrawing.Program, Geometry.Vector.Zero, null, token).Motions
                        .Any(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display))
                    continue;
                // The same clean, rotation-baked, seam-trimmed material that planning captures.
                var clean = (Program)part.BaseDrawing.Program.Clone();
                clean.Rotate(part.Rotation - part.BaseDrawing.Program.Rotation);
                SeamOvercut.Trim(clean);
                materials[index] = LeadMaterialSnapshot.Capture(clean, part.Location, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                // The overlap and rapid checks already report unreadable programs.
            }
            if (materials[index] is not { IsComplete: true })
                uncaptured.Add(index);
        }
        var others = materials.Select((material, index) => (Material: material, Index: index))
            .Where(m => m.Material is { IsComplete: true }).ToArray();
        // Leads near material that could not be captured are not checked: say so once per plate.
        foreach (var index in uncaptured)
            findings.Add(Incomplete(index, (materials[index]?.Reason ?? "the clean material could not be captured.")
                + " Locked leads near this part are not checked."));
        foreach (var index in reviewed)
        {
            token.ThrowIfCancellationRequested();
            var part = parts[index];
            if (materials[index] is not { IsComplete: true } own)
                continue;
            OwnedExecution execution;
            try
            {
                execution = ExecutionMotionReader.Read(part.Program, part.Location, null, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                continue; // The rapid check reports the unreadable program.
            }
            var failures = new List<FixedLeadFailure>();
            var result = LeadPathValidator.Check(execution, own, others.Select(o => o.Material).ToArray(),
                _ => true, failures, token);
            if (!result.IsComplete)
                findings.Add(Incomplete(index, result.Reason));
            foreach (var failure in failures)
            {
                var obstacle = failure.Obstacle is int other ? others[other].Index + 1 : (int?)null;
                var what = !failure.Result.IsComplete ? $"could not be checked ({failure.Result.Reason})"
                    : obstacle != null ? $"contacts or enters the material of part {obstacle}"
                    : "contacts or enters its own part's material";
                var motion = execution.Motions[failure.Motion];
                findings.Add(new(PostVerificationKind.FixedLead, plateNumber, index + 1, obstacle,
                    $"Locked lead on cutting contour {ContourAt(execution, failure.Motion)} {what}. "
                    + "Planning kept it as given; review it before posting.")
                { Location = motion.Curve?.Midpoint ?? motion.End });
            }
        }

        PostVerificationFinding Incomplete(int index, string reason) =>
            new(PostVerificationKind.Incomplete, plateNumber, index + 1, null, "Fixed lead check: " + reason);
    }

    /// <summary>
    /// The one-based cutting contour a lead motion belongs to: a lead-in leads into the next cut
    /// run, a lead-out leaves the last one. Scribes are not cutting contours.
    /// </summary>
    internal static int ContourAt(OwnedExecution execution, int motion)
    {
        var count = 0;
        var cutting = false;
        for (var index = 0; index < motion; index++)
        {
            var move = execution.Motions[index];
            var cut = !move.Rapid && move.Layer is LayerType.Cut or LayerType.Display
                && move.Length > PostVerificationGeometry.Epsilon;
            if (cut && !cutting)
                count++;
            cutting = cut;
        }
        return execution.Motions[motion].Layer == LayerType.Leadout ? System.Math.Max(count, 1) : count + 1;
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Pre-post review of fixed emissions. Plan Cutting accepts a locked program's leads and a
/// drawing-pinned contour's leads as given and warns (D1a), so posting checks them again against
/// every part's clean material on the plate and the active clamps. It also reports a part with
/// active drawing pins whose program does not cut its pinned contours as pinned. Reports only;
/// nothing is repaired and an incomplete check is never a clear result.
/// </summary>
internal static class FixedLeadReview
{
    /// <param name="fixedMotions">Receives, per reviewed part index, which of its placed program's
    /// motions this review owns, so other lead checks do not report them twice.</param>
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        IReadOnlyList<ClampZone> clamps, CancellationToken token,
        IDictionary<int, Func<int, bool>> fixedMotions = null)
    {
        var parts = plate.Parts;
        var reviewed = Enumerable.Range(0, parts.Count)
            .Where(i => parts[i] is { BaseDrawing.IsCutOff: false } part
                && (part.LeadInsLocked || part.HasActiveDrawingPins)).ToArray();
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
                if (!ExecutionMotionReader.Read(part.BaseDrawing.Program, Vector.Zero, null, token).Motions
                        .Any(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display))
                    continue;
                materials[index] = LeadMaterialSnapshot.Capture(Clean(part), part.Location, token);
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
                + " Fixed leads near this part are not checked."));
        foreach (var index in reviewed)
        {
            token.ThrowIfCancellationRequested();
            var part = parts[index];
            OwnedExecution execution;
            try
            {
                execution = ExecutionMotionReader.Read(part.Program, part.Location, null, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                continue; // The rapid check reports the unreadable program.
            }
            // Which motions are fixed: a locked program's every motion; otherwise the motions
            // that cut each active pin's contour exactly as pinned. Anything else was planned.
            int?[] pins = null;
            if (part.LeadInsLocked)
            {
                if (part.HasActiveDrawingPins)
                    NotApplied(index, "the part's locked lead-ins override them. Clear Lead-ins and plan cutting "
                        + "to apply the pins.");
            }
            else
            {
                pins = PinnedMotions(plate, part, execution, reason => NotApplied(index, reason), token);
                if (pins == null || pins.All(p => p == null))
                    continue;
            }
            bool IsFixed(int motion) => pins == null || pins[motion] != null;
            if (fixedMotions != null)
                fixedMotions[index] = IsFixed;
            if (materials[index] is { IsComplete: true } own)
            {
                var failures = new List<FixedLeadFailure>();
                var result = LeadPathValidator.Check(execution, own, others.Select(o => o.Material).ToArray(),
                    IsFixed, failures, token);
                if (!result.IsComplete)
                    findings.Add(Incomplete(index, result.Reason));
                foreach (var failure in failures)
                {
                    var obstacle = failure.Obstacle is int other ? others[other].Index + 1 : (int?)null;
                    var what = !failure.Result.IsComplete ? $"could not be checked ({failure.Result.Reason})"
                        : obstacle != null ? $"contacts or enters the material of part {obstacle}"
                        : "contacts or enters its own part's material";
                    Lead(index, execution, failure.Motion, pins?[failure.Motion], what, obstacle, null, null);
                }
            }
            for (var motion = 0; motion < execution.Motions.Count && clamps is { Count: > 0 }; motion++)
            {
                var move = execution.Motions[motion];
                if (!IsFixed(motion) || move.Rapid || move.Start is not { } from
                    || move.Layer is not (LayerType.Leadin or LayerType.Leadout))
                    continue;
                foreach (var zone in clamps)
                    if (PostVerificationAnalyzer.CutTouches(move, from, zone.Bounds, out var contact))
                        Lead(index, execution, motion, pins?[motion], $"touches active clamp '{zone.Id}'", null,
                            zone.Id, contact);
            }
        }

        void Lead(int index, OwnedExecution execution, int motion, int? pin, string what, int? obstacle,
            string clampId, Vector? contact)
        {
            var lead = pin is int number ? $"Drawing pin {number} lead" : "Locked lead";
            var move = execution.Motions[motion];
            findings.Add(new(PostVerificationKind.FixedLead, plateNumber, index + 1, obstacle,
                $"{lead} on cutting contour {ContourAt(execution, motion)} {what}. "
                + "Planning kept it as given; review it before posting.")
            { Location = contact ?? move.Curve?.Midpoint ?? move.End, ClampId = clampId });
        }

        void NotApplied(int index, string reason) => findings.Add(new(PostVerificationKind.FixedLead,
            plateNumber, index + 1, null, "Drawing pins not applied: " + reason));

        PostVerificationFinding Incomplete(int index, string reason) =>
            new(PostVerificationKind.Incomplete, plateNumber, index + 1, null, "Fixed lead check: " + reason);
    }

    /// <summary>
    /// For each motion of an unlocked part's placed program, the one-based pin whose contour it
    /// cuts exactly as pinned, found by re-emitting each pinned contour with the part's settings
    /// and matching that emission motion for motion; null where none. Each pin that cannot be
    /// found, or does not resolve, is reported through <paramref name="notApplied"/>.
    /// </summary>
    private static int?[] PinnedMotions(Plate plate, Part part, OwnedExecution execution,
        Action<string> notApplied, CancellationToken token)
    {
        var parameters = part.CuttingParameters ?? plate.CuttingParameters ?? new CuttingParameters();
        PreparedContours pinned;
        IReadOnlyList<CutPinResolution> resolutions;
        try
        {
            var clean = Clean(part);
            var plain = PreparedContours.Capture(clean, parameters, token);
            resolutions = CutPinResolver.Resolve(part.BaseDrawing, part.Rotation, plain, token);
            pinned = PreparedContours.Capture(clean, parameters, resolutions, token);
        }
        catch (Exception exception) when (IsInvalid(exception))
        {
            notApplied($"the drawing pins could not be checked ({exception.Message}).");
            return null;
        }
        foreach (var resolution in resolutions.Where(r => !r.IsResolved))
            notApplied($"drawing pin {resolution.PinIndex + 1} no longer matches the geometry.");
        foreach (var rejected in pinned.RejectedPins)
            notApplied($"drawing pin {rejected.PinIndex + 1} puts a tab on an internal contour.");
        var owned = new int?[execution.Motions.Count];
        for (var contour = 0; contour < pinned.Count; contour++)
        {
            token.ThrowIfCancellationRequested();
            if (pinned.PinnedEntry(contour) is not { } entry)
                continue;
            var number = pinned.PinNumberOf(contour);
            var expected = ExecutionMotionReader.Read(pinned.EmitCandidateForValidation(entry), part.Location, null, token)
                .Motions.Where(m => !m.Rapid && m.Layer != LayerType.Scribe).ToArray();
            var at = Find(execution.Motions, expected);
            if (at < 0)
            {
                notApplied($"drawing pin {number} is not cut as pinned; plan cutting to apply it.");
                continue;
            }
            for (var offset = 0; offset < expected.Length; offset++)
                owned[at + offset] = number;
        }
        return owned;
    }

    // The first place the expected emission occurs motion for motion in the placed program.
    private static int Find(IReadOnlyList<ExecutionMotion> actual, ExecutionMotion[] expected)
    {
        const double epsilon = PostVerificationGeometry.Epsilon;
        for (var start = 0; expected.Length != 0 && start + expected.Length <= actual.Count; start++)
        {
            var same = true;
            for (var offset = 0; offset < expected.Length && same; offset++)
            {
                var a = actual[start + offset];
                var e = expected[offset];
                same = !a.Rapid && Kind(a.Layer) == Kind(e.Layer) && a.End.DistanceTo(e.End) <= epsilon
                    && System.Math.Abs(a.Length - e.Length) <= epsilon
                    && (a.Start is not { } from || e.Start is not { } to || from.DistanceTo(to) <= epsilon);
            }
            if (same)
                return start;
        }
        return -1;

        static LayerType Kind(LayerType layer) => layer == LayerType.Display ? LayerType.Cut : layer;
    }

    // The same clean, rotation-baked, seam-trimmed program that planning captures.
    internal static Program Clean(Part part)
    {
        var clean = (Program)part.BaseDrawing.Program.Clone();
        clean.Rotate(part.Rotation - part.BaseDrawing.Program.Rotation);
        SeamOvercut.Trim(clean);
        return clean;
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

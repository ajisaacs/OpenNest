using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Pre-post review of pierce zones. When a part's cutting parameters set a
/// <see cref="CuttingParameters.PierceDiameter"/>, every lead-in pierce of its placed program must
/// keep half of it from every part's clean material on the plate, however the leads were made
/// (planned, assigned, placed by hand or locked). A contour pierced directly, without a lead-in,
/// is not checked. Reports only; an incomplete check is never a clear result.
/// </summary>
internal static class PierceZoneReview
{
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken token)
    {
        var parts = plate.Parts;
        var radii = parts.Select(part => part is { BaseDrawing.IsCutOff: false }
            ? (part.CuttingParameters ?? plate.CuttingParameters)?.GetPierceZoneRadius() ?? 0 : 0).ToArray();
        if (radii.All(radius => radius <= 0))
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
                materials[index] = LeadMaterialSnapshot.Capture(FixedLeadReview.Clean(part), part.Location, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                // The overlap and rapid checks already report unreadable programs.
            }
            if (materials[index] is not { IsComplete: true })
                uncaptured.Add(index);
        }
        // Pierces near material that could not be captured are not checked: say so once per part.
        foreach (var index in uncaptured)
            findings.Add(new(PostVerificationKind.Incomplete, plateNumber, index + 1, null,
                "Pierce zone check: " + (materials[index]?.Reason ?? "the clean material could not be captured.")
                + " Pierces near this part are not checked."));
        for (var index = 0; index < parts.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (radii[index] <= 0)
                continue;
            OwnedExecution execution;
            try
            {
                execution = ExecutionMotionReader.Read(parts[index].Program, parts[index].Location, null, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                continue; // The rapid check reports the unreadable program.
            }
            try
            {
                Review(index, execution, radii[index]);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, index + 1, null,
                    "Pierce zone check: " + exception.Message));
            }
        }

        void Review(int index, OwnedExecution execution, double radius)
        {
            var budget = 1000000;
            var motions = execution.Motions;
            for (var motion = 0; motion < motions.Count; motion++)
            {
                token.ThrowIfCancellationRequested();
                var move = motions[motion];
                // A lead-in reached by a rapid (or starting the program) starts at its pierce.
                if (move.Rapid || move.Layer != LayerType.Leadin || move.Start is not { } pierce
                    || motion > 0 && !motions[motion - 1].Rapid)
                    continue;
                var end = motion;
                while (end + 1 < motions.Count && !motions[end + 1].Rapid && motions[end + 1].Layer == LayerType.Leadin)
                    end++;
                // A break cut's pierce sits in its breakoff scrap, inside its own part's material.
                var breakLead = end + 1 < motions.Count && motions[end + 1].Layer == LayerType.Break;
                for (var other = 0; other < parts.Count; other++)
                {
                    if (materials[other] is not { IsComplete: true } material || other == index && breakLead)
                        continue;
                    var distance = LeadPathValidator.MaterialDistance(pierce, material, ref budget, token);
                    if (!LeadPathValidator.IsInsideZone(distance, radius))
                        continue;
                    var what = other == index ? "its own part's material" : $"the material of part {other + 1}";
                    findings.Add(new(PostVerificationKind.PierceZone, plateNumber, index + 1,
                        other == index ? null : other + 1,
                        $"Lead-in on cutting contour {FixedLeadReview.ContourAt(execution, motion)}: "
                        + LeadPathValidator.PierceZoneMessage(pierce, distance, what, radius)
                        + " Replan cutting or edit the lead before posting.")
                    { Location = pierce });
                }
            }
        }
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}

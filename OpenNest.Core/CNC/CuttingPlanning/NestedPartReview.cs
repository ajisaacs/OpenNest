using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Pre-post review of parts nested in other parts' cutouts. Cutting a host's enclosing hole
/// releases the slug the nested part is cut from, so the nested part must be cut first; and the
/// host's hole leads must not cross the nested part, nor the nested part's leads enter the host.
/// Containment is proven exactly as the cutting planner proves it; an ambiguous pair is incomplete,
/// never clear. Motions a fixed-lead review already owns are not reported twice. Reports only.
/// </summary>
internal static class NestedPartReview
{
    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        IReadOnlyDictionary<int, Func<int, bool>> fixedMotions, CancellationToken token)
    {
        var parts = plate.Parts;
        var clean = new Program[parts.Count];
        var cleanBounds = new Box[parts.Count];
        var hostBounds = new Box[parts.Count];
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
                        .Any(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null))
                    continue;
                var program = FixedLeadReview.Clean(part);
                cleanBounds[index] = NestedPartContainment.CleanBounds(program, part.Location, token);
                hostBounds[index] = NestedPartContainment.Union(part.BoundingBox, cleanBounds[index]);
                clean[index] = program;
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                // The overlap and rapid checks already report unreadable programs.
            }
        }
        var materials = new LeadMaterialSnapshot[parts.Count];
        var executions = new OwnedExecution[parts.Count];
        for (var inner = 0; inner < parts.Count; inner++)
            for (var host = 0; host < parts.Count; host++)
            {
                token.ThrowIfCancellationRequested();
                if (inner == host || clean[inner] == null || clean[host] == null
                    || !NestedPartContainment.Within(cleanBounds[inner], hostBounds[host]))
                    continue;
                var inside = NestedPartContainment.InsideCutout(Material(inner), Material(host), token,
                    out var ambiguity);
                if (inside == null)
                {
                    findings.Add(new(PostVerificationKind.Incomplete, plateNumber, inner + 1, host + 1,
                        $"Containment of part {inner + 1} relative to part {host + 1} cannot be established: "
                        + $"{ambiguity}. Their cut order and leads are not checked."));
                    continue;
                }
                if (inside == false)
                    continue;
                if (inner > host)
                {
                    var bounds = cleanBounds[inner];
                    findings.Add(new(PostVerificationKind.NestedCutOrder, plateNumber, inner + 1, host + 1,
                        $"Part {inner + 1} lies inside a cutout of part {host + 1} but is cut after it, so cutting "
                        + "the cutout releases its slug first. Plan Cutting cuts nested parts first; replan or "
                        + "reorder before posting.")
                    { Location = new Vector((bounds.Left + bounds.Right) / 2, (bounds.Bottom + bounds.Top) / 2) });
                }
                Leads(host, inner, $"part {inner + 1}, which is nested in this part's cutout");
                Leads(inner, host, $"part {host + 1}, whose cutout this part is nested in");
            }

        LeadMaterialSnapshot Material(int index) =>
            materials[index] ??= LeadMaterialSnapshot.Capture(clean[index], parts[index].Location, token);

        void Leads(int from, int against, string what)
        {
            OwnedExecution execution;
            try
            {
                execution = executions[from] ??= ExecutionMotionReader.Read(parts[from].Program,
                    parts[from].Location, null, token);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                return; // The rapid check reports the unreadable program.
            }
            var owned = fixedMotions.TryGetValue(from, out var isFixed) ? isFixed : null;
            var budget = 1000000;
            for (var motion = 0; motion < execution.Motions.Count; motion++)
            {
                token.ThrowIfCancellationRequested();
                var move = execution.Motions[motion];
                if (move.Rapid || move.Layer is not (LayerType.Leadin or LayerType.Leadout)
                    || owned?.Invoke(motion) == true
                    || move.Curve == null || !(move.Length > PostVerificationGeometry.Epsilon))
                    continue;
                string contact;
                try
                {
                    contact = LeadPathValidator.MaterialContact(move.Curve, materials[against], null, ref budget, token);
                }
                catch (Exception exception) when (IsInvalid(exception))
                {
                    findings.Add(new(PostVerificationKind.Incomplete, plateNumber, from + 1, against + 1,
                        "Nested part lead check: " + exception.Message));
                    return;
                }
                if (contact != null)
                    findings.Add(new(PostVerificationKind.NestedLead, plateNumber, from + 1, against + 1,
                        $"Lead on cutting contour {FixedLeadReview.ContourAt(execution, motion)} contacts or enters "
                        + $"the material of {what}. Replan cutting or edit the lead before posting.")
                    { Location = move.Curve.Midpoint });
            }
        }
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}

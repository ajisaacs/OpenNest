using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>A complete unsafe result is distinct from an incomplete/unsupported check.</summary>
public sealed record LeadPathValidationResult(bool IsComplete, bool IsClear, string Reason);

/// <summary>Certifies actual emitted native lead paths against owned nominal material.</summary>
public static class LeadPathValidator
{
    public static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (execution == null || target == null || otherMaterials == null)
            return new(false, false, "Missing execution or material snapshots.");
        if (!target.IsComplete || otherMaterials.Any(m => m == null || !m.IsComplete))
            return new(false, false, "Material snapshot is incomplete.");
        try
        {
            var budget = 1000000;
            for (var i = 0; i < execution.Motions.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var move = execution.Motions[i];
                if (move.Layer is not (LayerType.Leadin or LayerType.Leadout))
                    continue;
                if (move.Rapid || move.Start is not { } start || move.Curve == null
                    || !double.IsFinite(move.Length) || move.Length <= PostVerificationGeometry.Epsilon
                    || start.DistanceTo(move.Curve.Start) > PostVerificationGeometry.Epsilon
                    || move.End.DistanceTo(move.Curve.End) > PostVerificationGeometry.Epsilon)
                    return new(false, false, "Lead motion is missing, degenerate or inconsistent.");
                Vector? allowed = null;
                var groupEdge = i;
                var step = move.Layer == LayerType.Leadin ? 1 : -1;
                while (groupEdge + step >= 0 && groupEdge + step < execution.Motions.Count
                    && execution.Motions[groupEdge + step].Layer == move.Layer)
                {
                    token.ThrowIfCancellationRequested();
                    if (--budget < 0)
                        throw new NotSupportedException("Lead validation exceeds the native query limit.");
                    groupEdge += step;
                }
                var adjacentIndex = groupEdge + step;
                var genuineJoint = false;
                if (adjacentIndex >= 0 && adjacentIndex < execution.Motions.Count)
                {
                    var adjacent = execution.Motions[adjacentIndex];
                    if (!adjacent.Rapid && adjacent.Layer is LayerType.Cut or LayerType.Display
                        && adjacent.Curve != null && adjacent.Length > PostVerificationGeometry.Epsilon)
                    {
                        var edge = execution.Motions[groupEdge];
                        var joint = move.Layer == LayerType.Leadin ? edge.End : edge.Curve.Start;
                        var contourJoint = move.Layer == LayerType.Leadin ? adjacent.Curve.Start : adjacent.End;
                        foreach (var boundary in target.Rings.SelectMany(r => r))
                        {
                            token.ThrowIfCancellationRequested();
                            if (--budget < 0)
                                throw new NotSupportedException("Lead validation exceeds the native query limit.");
                            if (joint.DistanceTo(contourJoint) <= PostVerificationGeometry.Epsilon
                                && boundary.SameSupport(adjacent.Curve)
                                && boundary.Contains(adjacent.Curve.Start) && boundary.Contains(adjacent.End)
                                && boundary.Contains(adjacent.Curve.Midpoint)
                                && adjacent.Length <= boundary.Length + PostVerificationGeometry.Epsilon)
                            {
                                genuineJoint = true;
                                if (groupEdge == i)
                                    allowed = joint;
                                break;
                            }
                        }
                    }
                }
                if (!genuineJoint)
                    return new(true, false, "Lead chain has no genuine adjacent target contour entry or exit.");
                if (allowed is { } jointPoint
                    && (move.Layer == LayerType.Leadin ? start : move.End).DistanceTo(jointPoint) <= PostVerificationGeometry.Epsilon)
                    return new(true, false, "A positive-length lead returns to its contour joint; contact is not endpoint-only.");
                var failure = CheckMaterial(target, allowed);
                if (failure != null)
                    return new(true, false, $"Lead motion {i} contacts or enters target material outside its adjacent contour joint ({failure}).");
                foreach (var material in otherMaterials)
                {
                    token.ThrowIfCancellationRequested();
                    if (ReferenceEquals(material, target))
                        continue;
                    if (CheckMaterial(material, null) != null)
                        return new(true, false, "Lead contacts or enters another placed material. "
                            + "Try spacing the parts farther apart or reducing the lead-in/lead-out length, then replan. "
                            + "For locked parts, edit the leads or unlock the part before replanning.");
                }

                string CheckMaterial(LeadMaterialSnapshot material, Vector? permittedJoint)
                {
                    foreach (var boundary in material.Rings.SelectMany(r => r))
                    {
                        token.ThrowIfCancellationRequested();
                        if (--budget < 0)
                            throw new NotSupportedException("Lead validation exceeds the native query limit.");
                        var contacts = move.Curve.Contacts(boundary, out var overlap);
                        if (overlap || contacts.Any(p => permittedJoint is not { } joint
                            || p.DistanceTo(joint) > PostVerificationGeometry.Epsilon))
                            return $"boundary; allowed={permittedJoint}; contacts={string.Join(";", contacts)}; overlap={overlap}";
                    }
                    // With all other boundary contacts excluded, the connected open path
                    // has constant material membership. Use the native arc midpoint, not
                    // the chord midpoint or endpoints (which can both lie in scrap).
                    return material.ContainsMaterial(move.Curve.Midpoint, token) ? "interior" : null;
                }
            }
            // Missing leads are the ReleasedContourState check's responsibility.
            return new(true, true, null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new(false, false, ex.Message);
        }
    }
}

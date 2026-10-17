using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>A complete unsafe result is distinct from an incomplete/unsupported check.</summary>
public sealed record LeadPathValidationResult(bool IsComplete, bool IsClear, string Reason);

/// <summary>
/// One lead motion of a fixed emission that failed validation. <see cref="Obstacle"/> indexes the
/// other materials passed to the check, or is null when the lead meets its own part's material
/// or could not be checked.
/// </summary>
internal sealed record FixedLeadFailure(int Motion, LeadPathValidationResult Result, int? Obstacle);

/// <summary>Certifies actual emitted native lead paths against owned nominal material.</summary>
public static class LeadPathValidator
{
    public static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, CancellationToken token = default) =>
        Check(execution, target, otherMaterials, null, null, token);

    /// <summary>
    /// As the public check, but a lead motion that <paramref name="isFixed"/> owns (by motion
    /// index) is part of a fixed emission: its failure is added to <paramref name="fixedFailures"/>
    /// and checking continues. Any other failure is returned exactly as the public check returns it.
    /// </summary>
    internal static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, Func<int, bool> isFixed,
        List<FixedLeadFailure> fixedFailures, CancellationToken token)
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
                var (failure, obstacle) = CheckMotion(i);
                if (failure == null)
                    continue;
                if (isFixed?.Invoke(i) != true || fixedFailures == null)
                    return failure;
                fixedFailures.Add(new(i, failure, obstacle));
            }
            // No lead motion is valid for direct contour piercing. The caller still
            // checks rapid travel and contour accounting for the complete program.
            return new(true, true, null);

            (LeadPathValidationResult Failure, int? Obstacle) CheckMotion(int i)
            {
                var move = execution.Motions[i];
                if (move.Rapid || move.Start is not { } start || move.Curve == null
                    || !double.IsFinite(move.Length) || move.Length <= PostVerificationGeometry.Epsilon
                    || start.DistanceTo(move.Curve.Start) > PostVerificationGeometry.Epsilon
                    || move.End.DistanceTo(move.Curve.End) > PostVerificationGeometry.Epsilon)
                    return (new(false, false, "Lead motion is missing, degenerate or inconsistent."), null);
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
                    return (new(true, false, "Lead chain has no genuine adjacent target contour entry or exit."), null);
                if (allowed is { } jointPoint
                    && (move.Layer == LayerType.Leadin ? start : move.End).DistanceTo(jointPoint) <= PostVerificationGeometry.Epsilon)
                    return (new(true, false, "A positive-length lead returns to its contour joint; contact is not endpoint-only."), null);
                var failure = CheckMaterial(target, allowed);
                if (failure != null)
                    return (new(true, false, $"Lead motion {i} contacts or enters target material outside its adjacent contour joint ({failure})."), null);
                for (var other = 0; other < otherMaterials.Count; other++)
                {
                    token.ThrowIfCancellationRequested();
                    var material = otherMaterials[other];
                    if (ReferenceEquals(material, target))
                        continue;
                    if (CheckMaterial(material, null) != null)
                        return (new(true, false, "Lead contacts or enters another placed material. "
                            + "Try spacing the parts farther apart or reducing the lead-in/lead-out length, then replan. "
                            + "For locked parts, edit the leads or unlock the part before replanning."), other);
                }
                return (null, null);

                string CheckMaterial(LeadMaterialSnapshot material, Vector? permittedJoint) =>
                    MaterialContact(move.Curve, material, permittedJoint, ref budget, token);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new(false, false, ex.Message);
        }
    }

    /// <summary>
    /// How a native lead curve contacts or enters complete material, or null when it stays clear.
    /// A contact at <paramref name="permittedJoint"/> is allowed. Exceeding the budget throws.
    /// </summary>
    internal static string MaterialContact(PostVerificationGeometry.Curve curve, LeadMaterialSnapshot material,
        Vector? permittedJoint, ref int budget, CancellationToken token)
    {
        foreach (var boundary in material.Rings.SelectMany(r => r))
        {
            token.ThrowIfCancellationRequested();
            if (--budget < 0)
                throw new NotSupportedException("Lead validation exceeds the native query limit.");
            var contacts = curve.Contacts(boundary, out var overlap);
            if (overlap || contacts.Any(p => permittedJoint is not { } joint
                || p.DistanceTo(joint) > PostVerificationGeometry.Epsilon))
                return $"boundary; allowed={permittedJoint}; contacts={string.Join(";", contacts)}; overlap={overlap}";
        }
        // With all other boundary contacts excluded, the connected open path
        // has constant material membership. Use the native arc midpoint, not
        // the chord midpoint or endpoints (which can both lie in scrap).
        return material.ContainsMaterial(curve.Midpoint, token) ? "interior" : null;
    }
}

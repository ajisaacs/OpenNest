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
    /// As the public check, and every lead-in pierce must also keep <paramref name="pierceRadius"/>
    /// from all of the materials (see <see cref="CuttingStrategy.CuttingParameters.PierceDiameter"/>).
    /// Zero checks paths only.
    /// </summary>
    public static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, double pierceRadius, CancellationToken token = default) =>
        Check(execution, target, otherMaterials, null, null, token, pierceRadius: pierceRadius);

    /// <summary>As the public check, remembering other-material results in <paramref name="memo"/>.</summary>
    internal static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, LeadContactMemo memo, CancellationToken token,
        double pierceRadius = 0) =>
        Check(execution, target, otherMaterials, null, null, token, memo, pierceRadius);

    /// <summary>
    /// As the public check, but a lead motion that <paramref name="isFixed"/> owns (by motion
    /// index) is part of a fixed emission: its failure is added to <paramref name="fixedFailures"/>
    /// and checking continues. Any other failure is returned exactly as the public check returns it.
    /// A positive <paramref name="pierceRadius"/> also requires each lead-in pierce to keep that
    /// distance from every material; a contour pierced directly, without a lead-in, is not checked.
    /// </summary>
    internal static LeadPathValidationResult Check(OwnedExecution execution, LeadMaterialSnapshot target,
        IReadOnlyList<LeadMaterialSnapshot> otherMaterials, Func<int, bool> isFixed,
        List<FixedLeadFailure> fixedFailures, CancellationToken token, LeadContactMemo memo = null,
        double pierceRadius = 0)
    {
        token.ThrowIfCancellationRequested();
        if (execution == null || target == null || otherMaterials == null)
            return new(false, false, "Missing execution or material snapshots.");
        if (!double.IsFinite(pierceRadius) || pierceRadius < 0)
            return new(false, false, "Pierce zone radius must be finite and nonnegative.");
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
                var breakLead = false;
                if (adjacentIndex >= 0 && adjacentIndex < execution.Motions.Count)
                {
                    var adjacent = execution.Motions[adjacentIndex];
                    // A break cut lies inside its part's material, so its lead joins no boundary:
                    // the joint is the cut's own start (or end for a lead-out).
                    if (!adjacent.Rapid && adjacent.Layer == LayerType.Break
                        && adjacent.Curve != null && adjacent.Length > PostVerificationGeometry.Epsilon)
                    {
                        var edge = execution.Motions[groupEdge];
                        var joint = move.Layer == LayerType.Leadin ? edge.End : edge.Curve.Start;
                        var cutJoint = move.Layer == LayerType.Leadin ? adjacent.Curve.Start : adjacent.End;
                        if (joint.DistanceTo(cutJoint) <= PostVerificationGeometry.Epsilon)
                        {
                            genuineJoint = true;
                            breakLead = true;
                            if (groupEdge == i)
                                allowed = joint;
                        }
                    }
                    else if (!adjacent.Rapid && adjacent.Layer is LayerType.Cut or LayerType.Display
                        && adjacent.Curve != null && adjacent.Length > PostVerificationGeometry.Epsilon)
                    {
                        var edge = execution.Motions[groupEdge];
                        var joint = move.Layer == LayerType.Leadin ? edge.End : edge.Curve.Start;
                        var contourJoint = move.Layer == LayerType.Leadin ? adjacent.Curve.Start : adjacent.End;
                        // A boundary clear of the contour's first motion contains none of its points.
                        foreach (var boundary in target.BoundariesNear(adjacent.Curve.Extent))
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
                // A break cut's lead runs through its own breakoff scrap: entering the target's
                // material is expected there, but touching any of its boundaries is not.
                var failure = breakLead
                    ? MaterialContact(move.Curve, target, null, ref budget, token, allowInterior: true)
                    : CheckMaterial(target, allowed);
                if (failure != null)
                    return (new(true, false, breakLead
                        ? $"Break cut lead motion {i} contacts its part's boundary ({failure}). Try a shorter lead-in or move the tabs."
                        : $"Lead motion {i} contacts or enters target material outside its adjacent contour joint ({failure})."), null);
                var reach = move.Curve.Extent;
                for (var other = 0; other < otherMaterials.Count; other++)
                {
                    token.ThrowIfCancellationRequested();
                    var material = otherMaterials[other];
                    if (ReferenceEquals(material, target))
                        continue;
                    if (reach.IsClearOfQueriesFrom(material.Extent) ? FarInside(material) : OtherContact(material))
                        return (new(true, false, "Lead contacts or enters another placed material. "
                            + "Try spacing the parts farther apart or reducing the lead-in/lead-out length, then replan. "
                            + "For locked parts, edit the leads or unlock the part before replanning."), other);
                }
                return PierceZone();

                // A lead-in reached by a rapid (or starting the program) starts at its pierce. A break
                // cut's pierce lies in its own breakoff scrap, inside the part's material, so only
                // others are checked.
                (LeadPathValidationResult, int?) PierceZone()
                {
                    if (pierceRadius <= 0 || move.Layer != LayerType.Leadin || i > 0 && !execution.Motions[i - 1].Rapid)
                        return (null, null);
                    if (!breakLead && Clearance(target) is { } own)
                        return (Short(own, "its own part's material"), null);
                    for (var other = 0; other < otherMaterials.Count; other++)
                    {
                        token.ThrowIfCancellationRequested();
                        var material = otherMaterials[other];
                        if (ReferenceEquals(material, target))
                            continue;
                        // Every boundary of material this far away is farther than the zone.
                        if (new PostVerificationGeometry.Extent(start.X, start.Y, start.X, start.Y)
                            .IsClearOfQueriesFrom(material.Extent, pierceRadius))
                            continue;
                        if (Clearance(material) is { } distance)
                            return (Short(distance, "another placed part"), other);
                    }
                    return (null, null);
                }

                // The pierce's distance to the material when it is inside the zone, else null.
                double? Clearance(LeadMaterialSnapshot material)
                {
                    var distance = MaterialDistance(start, material, ref budget, token);
                    return IsInsideZone(distance, pierceRadius) ? distance : null;
                }

                LeadPathValidationResult Short(double distance, string what) =>
                    new(true, false, PierceZoneMessage(start, distance, what, pierceRadius)
                        + " Try a longer lead-in, more part spacing or a smaller pierce diameter, then replan.");

                string CheckMaterial(LeadMaterialSnapshot material, Vector? permittedJoint) =>
                    MaterialContact(move.Curve, material, permittedJoint, ref budget, token);

                // Exactly the unmemoized check: a remembered scan charges the queries it ran
                // and throws where running it again would exhaust the budget.
                bool OtherContact(LeadMaterialSnapshot material)
                {
                    if (memo == null)
                        return CheckMaterial(material, null) != null;
                    if (memo.TryGet(move.Curve, material, out var scan))
                    {
                        Charge(scan.Queries);
                        return scan.Contact;
                    }
                    var before = budget;
                    var contact = CheckMaterial(material, null) != null;
                    memo.Add(move.Curve, material, new(contact, before - budget));
                    return contact;
                }

                // Material too far from the lead for any native boundary query to report a contact
                // (see IsClearOfQueriesFrom): the result the full scan gives. Only queries that run
                // are charged, so many far parts or holes do not exhaust the budget. Ray parity is
                // still asked natively unless the lead lies beside the material's rows.
                bool FarInside(LeadMaterialSnapshot material)
                {
                    var midpoint = move.Curve.Midpoint;
                    return !material.Extent.IsClearOfRow(midpoint.Y) && material.ContainsMaterial(midpoint, token);
                }

                void Charge(int queries)
                {
                    if (budget - queries < 0)
                        throw new NotSupportedException("Lead validation exceeds the native query limit.");
                    budget -= queries;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new(false, false, ex.Message);
        }
    }

    /// <summary>
    /// The exact native distance from <paramref name="point"/> to the nearest boundary of complete
    /// material. Exceeding the budget throws.
    /// </summary>
    internal static double MaterialDistance(Vector point, LeadMaterialSnapshot material, ref int budget,
        CancellationToken token)
    {
        var nearest = double.PositiveInfinity;
        foreach (var boundary in material.Rings.SelectMany(r => r))
        {
            token.ThrowIfCancellationRequested();
            if (--budget < 0)
                throw new NotSupportedException("Lead validation exceeds the native query limit.");
            nearest = System.Math.Min(nearest, boundary.DistanceTo(point));
        }
        return nearest;
    }

    /// <summary>True when material <paramref name="distance"/> away lies inside a pierce zone.</summary>
    internal static bool IsInsideZone(double distance, double pierceRadius) =>
        pierceRadius > 0 && distance < pierceRadius - PostVerificationGeometry.Epsilon;

    internal static string PierceZoneMessage(Vector pierce, double distance, string what, double pierceRadius) =>
        FormattableString.Invariant(
            $"The pierce at ({pierce.X:0.####}, {pierce.Y:0.####}) is {distance:0.####} from {what}; ")
        + FormattableString.Invariant($"the {pierceRadius * 2:0.####} pierce diameter needs {pierceRadius:0.####}.");

    /// <summary>
    /// How a native lead curve contacts or enters complete material, or null when it stays clear.
    /// A contact at <paramref name="permittedJoint"/> is allowed. Exceeding the budget throws.
    /// </summary>
    internal static string MaterialContact(PostVerificationGeometry.Curve curve, LeadMaterialSnapshot material,
        Vector? permittedJoint, ref int budget, CancellationToken token, bool allowInterior = false)
    {
        // Boundaries no native query from the curve can reach report no contact; only the others run.
        foreach (var boundary in material.BoundariesNear(curve.Extent))
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
        return !allowInterior && material.ContainsMaterial(curve.Midpoint, token) ? "interior" : null;
    }
}

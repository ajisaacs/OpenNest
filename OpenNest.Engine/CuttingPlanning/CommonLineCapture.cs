using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Capture of a plate's common-line groups cut once (docs/common-line-cutting.md). Each group is
/// cut member by member in <see cref="CommonLineCutting.Order"/>'s order: a member's holes, the
/// shared cuts it owns (open centerline cuts), then its remainder pieces, which end in kerf those
/// shared cuts, or an earlier member's, opened. The owner of a shared cut therefore precedes its
/// partner. Shared spans are derived from the placed geometry at capture; a broken group refuses.
/// </summary>
internal sealed class CommonLineCapture
{
    private readonly Dictionary<Part, Member> members = new(ReferenceEqualityComparer.Instance);

    private CommonLineCapture()
    {
    }

    /// <summary>Null when the request's plate cuts no common line once.</summary>
    /// <exception cref="NotSupportedException">A group is broken.</exception>
    internal static CommonLineCapture Of(CuttingPlanRequest request)
    {
        var plate = request?.Plate;
        if (plate == null || !plate.CutsCommonLinesOnce)
            return null;
        var capture = new CommonLineCapture();
        foreach (var group in plate.CommonLines)
        {
            var check = CommonLineGeometry.Check(group);
            if (!check.IsIntact)
            {
                var problem = check.Problems[0];
                throw new NotSupportedException(
                    $"Part {IndexOf(request.Parts, problem.Part) + 1} {problem.Message} Rejoin its common line, "
                    + "remove it, or cut the plate's common lines twice, then replan.");
            }
            var order = CommonLineCutting.Order(group, check);
            for (var i = 0; i < order.Members.Count; i++)
            {
                var part = order.Members[i];
                if (IndexOf(request.Parts, part) < 0)
                    throw new ArgumentException("A common-line member is not among the planned parts.");
                capture.members[part] = new Member(order, part, order.PathsOf(part).ToArray(),
                    order.CutsOwnedBy(part).ToArray());
            }
        }
        return capture;
    }

    /// <summary>Source ordinals of every member, for the rapid checker.</summary>
    internal IReadOnlySet<int> MemberNumbers(IReadOnlyList<Part> parts) =>
        members.Keys.Select(part => IndexOf(parts, part) + 1).ToHashSet();

    /// <summary>(part, prerequisite) source ordinals: each shared cut's owner precedes its partner.</summary>
    internal IReadOnlyList<(int Part, int Prerequisite)> Order(IReadOnlyList<Part> parts) =>
        members.Values.SelectMany(member => member.Owned)
            .Select(cut => (IndexOf(parts, cut.Partner), IndexOf(parts, cut.Owner)))
            .Distinct()
            .ToArray();

    internal bool IsMember(Part part) => members.ContainsKey(part);

    /// <summary>The member's owned clean program with its shared spans on the Tab layer.</summary>
    /// <exception cref="NotSupportedException">A span does not lie along the program's edges.</exception>
    internal Program Mark(Part part, Program ownedClean, Vector location, int ordinal)
    {
        var member = members[part];
        try
        {
            return CommonLineCutting.MarkSharedSpans(ownedClean, location, member.Paths, member.Order.Group.Kerf);
        }
        catch (ArgumentException exception)
        {
            throw new NotSupportedException($"Part {ordinal + 1} cannot be cut on its common line: {exception.Message}");
        }
    }

    /// <summary>
    /// The member's shared cuts in part coordinates, each cut from whichever end its straight
    /// leads fit: the cut, its lead-in and its lead-out may touch no material, and the pierce keeps
    /// the pierce zone. Leads are never shortened: a cut whose leads fit at neither end refuses,
    /// naming the span.
    /// </summary>
    /// <exception cref="NotSupportedException">No lead fits a shared cut.</exception>
    internal CommonLinePerimeter Perimeter(Part part, int ordinal, IReadOnlyList<Part> parts,
        LeadMaterialSnapshot own, IReadOnlyList<LeadMaterialSnapshot> materials, CuttingParameters parameters,
        double pierceRadius, CancellationToken token)
    {
        var member = members[part];
        var location = part.Location;
        var leadIn = parameters.ExternalLeadIn ?? new NoLeadIn();
        var leadOut = parameters.ExternalLeadOut ?? new NoLeadOut();
        var cuts = new List<CommonLineCutPath>();
        foreach (var cut in member.Owned)
        {
            token.ThrowIfCancellationRequested();
            var forward = new CommonLineCutPath(cut.Path.Points.Select(point => point - location).ToArray());
            var reverse = forward.Reversed();
            string reason = null;
            CommonLineCutPath chosen = null;
            foreach (var path in new[] { forward, reverse })
            {
                var program = new Program(Mode.Absolute);
                program.Codes.AddRange(ContourCuttingStrategy.CommonLineSharedCutCodes(path, leadIn, leadOut));
                var execution = ExecutionMotionReader.ReadSupported(program, location, null, token);
                var result = LeadPathValidator.Check(execution, own, materials, pierceRadius, token);
                if (result.IsComplete && result.IsClear)
                {
                    chosen = path;
                    break;
                }
                reason ??= result.Reason;
            }
            if (chosen == null)
                throw new NotSupportedException(FormattableString.Invariant(
                    $"No lead fits the common line between parts {ordinal + 1} and {IndexOf(parts, cut.Partner) + 1} from ({cut.Path.Start.X:0.####}, {cut.Path.Start.Y:0.####}) to ({cut.Path.End.X:0.####}, {cut.Path.End.Y:0.####}) at either end: ")
                    + reason + " Shorten the external lead-in or lead-out, or cut the plate's common lines twice.");
            cuts.Add(chosen);
        }
        return new CommonLinePerimeter(member.Order.Group.Kerf, cuts);
    }

    private static int IndexOf(IReadOnlyList<Part> parts, Part part)
    {
        for (var i = 0; i < parts.Count; i++)
            if (ReferenceEquals(parts[i], part))
                return i;
        return -1;
    }

    private sealed record Member(CommonLineCutOrder Order, Part Part, IReadOnlyList<CommonLinePath> Paths,
        IReadOnlyList<CommonLineSharedCut> Owned);
}

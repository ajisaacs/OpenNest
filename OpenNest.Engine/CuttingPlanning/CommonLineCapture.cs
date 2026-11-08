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
/// cut member by member in <see cref="CommonLineCutting.Order"/>'s order: a member's holes, then
/// its outside. A member no earlier member shares with cuts its whole perimeter from one of its
/// common lines; any other cuts its remainder pieces, which end in kerf an earlier member opened.
/// The owner of a shared cut therefore precedes its partner. Shared spans are derived from the placed geometry at capture; a broken group refuses.
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
                capture.members[part] = new Member(order, part, order.PathsCutBefore(part).ToArray(),
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

    /// <summary>The member's owned clean program with the spans earlier members cut on the Tab layer.</summary>
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
    /// How the member's outside is cut once. A member that cuts a common line itself starts its
    /// outside where its travel enters one, with a straight lead-in along that edge from the scrap:
    /// the first whose lead-in touches no material and keeps the pierce zone. Leads are never
    /// shortened: when none fits, planning refuses, naming the common line. A member that cuts none
    /// cuts its remainder from the kerf earlier members opened.
    /// </summary>
    /// <exception cref="NotSupportedException">No lead fits the start of any of its common lines.</exception>
    internal CommonLinePerimeter Perimeter(Part part, int ordinal, IReadOnlyList<Part> parts, Program clean,
        LeadMaterialSnapshot own, IReadOnlyList<LeadMaterialSnapshot> materials, CuttingParameters parameters,
        double pierceRadius, CancellationToken token)
    {
        var member = members[part];
        var kerf = member.Order.Group.Kerf;
        if (member.Owned.Count == 0)
            return new CommonLinePerimeter(kerf);
        var reach = ContourCuttingStrategy.CommonLineLeadInReach(parameters.ExternalLeadIn ?? new NoLeadIn());
        if (reach <= Math.Tolerance.Epsilon)
            throw new NotSupportedException($"Part {ordinal + 1} starts its perimeter on its common line, which "
                + "needs an external lead-in with length to reach it from the scrap. Choose an external lead-in "
                + "with length, or cut the plate's common lines twice.");
        var location = part.Location;
        var owned = member.Owned.Select(cut => cut.Path).ToArray();
        string reason = null;
        foreach (var (entry, pierce, next) in CommonLineCutting.PerimeterEntries(clean, location, owned, kerf, reach))
        {
            token.ThrowIfCancellationRequested();
            var program = new Program(Mode.Absolute);
            program.Codes.Add(new RapidMove(pierce));
            program.Codes.Add(new LinearMove(entry) { Layer = LayerType.Leadin });
            program.Codes.Add(new LinearMove(next) { Layer = LayerType.Display });
            var execution = ExecutionMotionReader.ReadSupported(program, location, null, token);
            var result = LeadPathValidator.Check(execution, own, materials, pierceRadius, token);
            if (result.IsComplete && result.IsClear)
                return new CommonLinePerimeter(kerf, entry, pierce);
            reason ??= result.Reason;
        }
        var first = member.Owned[0];
        throw new NotSupportedException(FormattableString.Invariant(
            $"No lead fits the start of the common line between parts {ordinal + 1} and {IndexOf(parts, first.Partner) + 1} from ({first.Path.Start.X:0.####}, {first.Path.Start.Y:0.####}) to ({first.Path.End.X:0.####}, {first.Path.End.Y:0.####}): ")
            + (reason ?? "it does not run along a straight edge of the part's program.")
            + " Shorten the external lead-in, or cut the plate's common lines twice.");
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

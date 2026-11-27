using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// The last resort for a route no search could keep head-down: each planner-chosen rapid that
/// crosses parts already cut is marked to raise the head (<see cref="HeadRaise"/>). Only the
/// safest-route fallback uses it, after the strict search found no route avoiding every crossing,
/// so a plate that can be cut head-down never gets a raise. Rapids into or inside a fixed emission
/// are never marked, and neither is a sub-program, which may be shared: the rapid arriving at a hole
/// sub-program is raised by marking its call, and other rapids inside sub-programs keep their
/// crossings as findings.
/// </summary>
internal static class RaisedRapids
{
    /// <summary>
    /// <paramref name="order"/> with the crossing rapids of regenerated parts marked, walked as
    /// replay walks it. Parts with nothing to mark keep their proposal.
    /// </summary>
    internal static IReadOnlyList<FixedProgramPlacement> Mark(CuttingPlanSnapshot snapshot,
        IReadOnlyList<FixedProgramPlacement> order, CancellationToken token)
    {
        var checker = snapshot.NewReleasedState();
        var position = snapshot.StartPoint;
        var marked = new List<FixedProgramPlacement>(order.Count);
        foreach (var proposal in order)
        {
            token.ThrowIfCancellationRequested();
            var program = proposal.CopyProgram();
            var execution = ExecutionMotionReader.Read(program, proposal.Location, position, token);
            var placement = proposal;
            var rapids = proposal.Prepared == null || proposal.IsCutOff ? []
                : Crossing(checker.Copy(), proposal, execution, position, snapshot.WarnsTabLeadOuts(proposal), token).Codes;
            if (rapids.Count != 0)
            {
                var raised = HeadRaise.Mark(program, rapids);
                execution = ExecutionMotionReader.Read(raised, proposal.Location, position, token);
                placement = proposal.Propose(raised, execution, proposal.ContourChoices, token);
            }
            checker.Check(execution, position, proposal.SourceOrdinal + 1, proposal.IsCutOff,
                proposal.FixedMotions(execution), token, snapshot.WarnsTabLeadOuts(proposal));
            marked.Add(placement);
            position = execution.DeparturePoint;
        }
        return marked;
    }

    /// <summary>
    /// The places along an unmarked <paramref name="order"/> whose planner-chosen rapids cross parts
    /// already cut, each with the source ordinals of the parts crossed, walked as replay walks it.
    /// </summary>
    internal static IReadOnlyList<(int Index, int[] Crossed)> Crossed(CuttingPlanSnapshot snapshot,
        IReadOnlyList<FixedProgramPlacement> order, CancellationToken token)
    {
        var checker = snapshot.NewReleasedState();
        var position = snapshot.StartPoint;
        var crossed = new List<(int, int[])>();
        for (var index = 0; index < order.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var proposal = order[index];
            var execution = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, position, token);
            if (proposal.Prepared != null && !proposal.IsCutOff
                && Crossing(checker.Copy(), proposal, execution, position, snapshot.WarnsTabLeadOuts(proposal), token)
                    .Parts is { Count: > 0 } parts)
                crossed.Add((index, [.. parts.Order()]));
            checker.Check(execution, position, proposal.SourceOrdinal + 1, proposal.IsCutOff,
                proposal.FixedMotions(execution), token, snapshot.WarnsTabLeadOuts(proposal));
            position = execution.DeparturePoint;
        }
        return crossed;
    }

    // The own-code indices of the planner's rapids that cross parts already cut (the rapid itself,
    // or the call of the sub-program the rapid arrives at) and the source ordinals of those parts.
    private static (HashSet<int> Codes, HashSet<int> Parts) Crossing(ReleasedContourState checker,
        FixedProgramPlacement proposal, OwnedExecution execution, Vector arrival, bool tabLeadOuts,
        CancellationToken token)
    {
        var isFixed = proposal.FixedMotions(execution);
        var strict = checker.Check(execution, arrival, proposal.SourceOrdinal + 1, false, isFixed, token,
            tabLeadOuts).Strict;
        var codes = new HashSet<int>();
        var parts = new HashSet<int>();
        foreach (var finding in strict)
        {
            if (finding.Kind != PostVerificationKind.RapidCrossing || finding.RapidEnd is not { } end)
                continue;
            for (var index = 0; index < execution.Motions.Count; index++)
            {
                var motion = execution.Motions[index];
                if (!motion.Rapid || isFixed?.Invoke(index) == true || motion.End != end
                    || (index == 0 ? arrival : motion.Start) != finding.RapidStart)
                    continue;
                if (motion.Code is int code)
                    codes.Add(code);
                else if (motion.CallCode is int call
                    && (index == 0 || execution.Motions[index - 1].CallCode != call))
                    codes.Add(call);
                else
                    continue;
                if (finding.OtherPartNumber is int other && other - 1 != proposal.SourceOrdinal)
                    parts.Add(other - 1);
            }
        }
        return (codes, parts);
    }

    /// <summary>
    /// One advisory note per raised rapid of <paramref name="source"/>, naming the cut parts it
    /// passes over, from the replay's raised crossings (check positions are source ordinals + 1).
    /// </summary>
    internal static IEnumerable<CuttingPlanFinding> Notes(CuttingPlanSnapshot snapshot,
        FixedProgramPlacement source, IReadOnlyList<PostVerificationFinding> raised) =>
        raised.GroupBy(f => (f.RapidStart, f.RapidEnd)).Select(rapid =>
        {
            var parts = rapid.Select(f => f.OtherPartNumber).OfType<int>().Distinct().Order().ToArray();
            var other = parts.Length == 1 ? snapshot.Placements[parts[0] - 1] : null;
            var over = other != null ? "a part already cut" : $"parts {string.Join(", ", parts)}, already cut,";
            return new CuttingPlanFinding(source.SourceOrdinal, source.SourcePart, other?.SourceOrdinal,
                other?.SourcePart, null, $"A rapid raises the head over {over} because no route found keeps the head down.")
            { Witness = rapid.First() };
        });

    /// <summary>The raised rapids in <paramref name="raised"/>: crossings of one rapid count once.</summary>
    internal static int Count(IEnumerable<PostVerificationFinding> raised) =>
        raised.Select(f => (f.RapidStart, f.RapidEnd)).Distinct().Count();
}

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
/// so a plate that can be cut head-down never gets a raise. Rapids into or inside a fixed emission,
/// and rapids inside sub-programs, are never marked: their crossings stay findings.
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
        var checker = new ReleasedContourState(reportMissingLeadIns: false);
        var position = snapshot.StartPoint;
        var marked = new List<FixedProgramPlacement>(order.Count);
        foreach (var proposal in order)
        {
            token.ThrowIfCancellationRequested();
            var program = proposal.CopyProgram();
            var execution = ExecutionMotionReader.Read(program, proposal.Location, position, token);
            var placement = proposal;
            var rapids = proposal.Prepared == null || proposal.IsCutOff ? []
                : Crossing(checker.Copy(), proposal, execution, position, token);
            if (rapids.Count != 0)
            {
                var raised = HeadRaise.Mark(program, rapids);
                execution = ExecutionMotionReader.Read(raised, proposal.Location, position, token);
                placement = proposal.Propose(raised, execution, proposal.ContourChoices, token);
            }
            checker.Check(execution, position, proposal.SourceOrdinal + 1, proposal.IsCutOff,
                proposal.FixedMotions(execution), token);
            marked.Add(placement);
            position = execution.DeparturePoint;
        }
        return marked;
    }

    // The own-code indices of the planner's rapids that cross parts already cut.
    private static HashSet<int> Crossing(ReleasedContourState checker, FixedProgramPlacement proposal,
        OwnedExecution execution, Vector arrival, CancellationToken token)
    {
        var isFixed = proposal.FixedMotions(execution);
        var strict = checker.Check(execution, arrival, proposal.SourceOrdinal + 1, false, isFixed, token).Strict;
        var codes = new HashSet<int>();
        foreach (var finding in strict)
        {
            if (finding.Kind != PostVerificationKind.RapidCrossing || finding.RapidEnd is not { } end)
                continue;
            for (var index = 0; index < execution.Motions.Count; index++)
            {
                var motion = execution.Motions[index];
                if (motion.Rapid && motion.Code is int code && isFixed?.Invoke(index) != true
                    && motion.End == end && (index == 0 ? arrival : motion.Start) == finding.RapidStart)
                    codes.Add(code);
            }
        }
        return codes;
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

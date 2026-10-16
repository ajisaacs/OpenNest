using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Turns violations inside a fixed emission into named <see cref="PostVerificationKind.FixedLead"/>
/// warnings (D1a). The emission is kept exactly as given; only the operator's review can accept it.
/// </summary>
internal static class FixedLeadWarnings
{
    private const string LockedAdvice = "The locked program is kept as given. Increase spacing, or edit or "
        + "unlock this part's lead-ins and replan.";

    /// <summary>A replay-checker finding (rapid crossing or retention) inside the fixed emission.</summary>
    internal static CuttingPlanFinding FromChecker(CuttingPlanSnapshot snapshot, FixedProgramPlacement source,
        PostVerificationFinding finding)
    {
        var other = finding.OtherPartNumber is int part ? snapshot.Placements[part - 1] : null;
        return new(source.SourceOrdinal, source.SourcePart, other?.SourceOrdinal, other?.SourcePart,
            PostVerificationKind.FixedLead, $"Locked lead-ins: {finding.Message} {LockedAdvice}")
        {
            Witness = finding,
            FixedLead = new(null, null, other?.SourceOrdinal)
        };
    }

    /// <summary>A lead motion of the fixed emission that meets material or could not be certified.</summary>
    internal static CuttingPlanFinding FromLead(FixedProgramPlacement source, OwnedExecution execution,
        FixedLeadFailure failure, IReadOnlyList<FixedProgramPlacement> materialOwners)
    {
        var contour = FixedLeadReview.ContourAt(execution, failure.Motion);
        var obstacle = failure.Obstacle is int index ? materialOwners[index] : null;
        var what = !failure.Result.IsComplete
            ? $"could not be checked ({failure.Result.Reason})"
            : obstacle != null ? "contacts or enters another part's material"
            : $"contacts or enters its own part's material ({failure.Result.Reason})";
        var motion = execution.Motions[failure.Motion];
        return new(source.SourceOrdinal, source.SourcePart, obstacle?.SourceOrdinal, obstacle?.SourcePart,
            PostVerificationKind.FixedLead, $"Locked lead on cutting contour {contour} {what}. {LockedAdvice}")
        {
            Witness = new(PostVerificationKind.FixedLead, 1, source.SourceOrdinal + 1,
                obstacle == null ? null : obstacle.SourceOrdinal + 1, failure.Result.Reason ?? what)
            { Location = motion.Curve?.Midpoint ?? motion.End },
            FixedLead = new(contour, null, obstacle?.SourceOrdinal)
        };
    }

    /// <summary>A fixed program whose contour coverage (for example a tab gap) cannot be certified.</summary>
    internal static CuttingPlanFinding Uncertified(FixedProgramPlacement source) =>
        new(source.SourceOrdinal, source.SourcePart, null, null, PostVerificationKind.FixedLead,
            "Locked lead-ins: the program does not cut every closed contour of its drawing exactly once "
            + "(for example, it leaves a tab gap), so its retention cannot be certified. " + LockedAdvice)
        { FixedLead = new(null, null, null) };
}

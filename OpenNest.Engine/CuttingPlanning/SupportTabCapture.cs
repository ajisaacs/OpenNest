using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Capture-time support tabs for one regenerated part (docs/support-tabs.md): with a support
/// bed and <see cref="CNC.CuttingStrategy.CuttingParameters.SupportTabs"/> on, a part the bed
/// cannot hold level has its perimeter tabbed where the tab steadies it. A supported part, a
/// pinned perimeter (the drawing wins), a missing tab configuration or material the analyzer
/// cannot read leave the preparation as it was.
/// </summary>
internal static class SupportTabCapture
{
    internal static PreparedContours Prepare(PreparedContours prepared, Program ownedClean, Vector location,
        CuttingPlanRequest request, CancellationToken token)
    {
        var parameters = request.ConfirmedParameters;
        if (prepared == null || request.SupportBed == null || parameters?.SupportTabs != true
            || parameters.TabConfig == null || prepared.IsPinned(prepared.PerimeterOrdinal))
            return prepared;
        token.ThrowIfCancellationRequested();
        // The clean program is the drawing's own, rotated as placed: its material at the
        // part's location is the material that drops onto the bed. The tab keeps clear of
        // inside corners by twice its size.
        var support = SupportAnalyzer.Analyze(request.SupportBed, request.SupportQuadrant,
            SupportMaterial.FromProgram(ownedClean, location),
            SupportAnalyzer.InsideCornerClearance(parameters.TabConfig.Size));
        if (!support.IsComplete || support.IsSupported || support.BestTabPoint == null)
            return prepared;
        return prepared.WithSupportTab(support, location, parameters.TabConfig);
    }

    /// <summary>
    /// Notes for a planned support-tabbed part whose tab, at its perimeter entry, cannot hold it
    /// level or sits in an inside corner. Advisory: they never refuse or downgrade a plan.
    /// </summary>
    internal static IEnumerable<CuttingPlanFinding> Notes(FixedProgramPlacement planned)
    {
        var prepared = planned.Prepared;
        if (prepared?.Support is not { } support
            || planned.ContourChoices.FirstOrDefault(c => c.ContourOrdinal == prepared.PerimeterOrdinal) is not { } entry)
            yield break;
        var tab = entry.Point + planned.Location;
        var at = FormattableString.Invariant($"({tab.X:0.####}, {tab.Y:0.####})");
        if (!support.TabCanSupport)
            yield return JointCuttingPlanSearch.Finding(planned, null,
                $"The support grid cannot hold it level, and no single tab can: its tab at {at} steadies it "
                + "as far as one tab can, but it may still tip. Add tabs by hand or move the part, then replan.");
        else if (!prepared.HoldsLevelAt(entry))
            yield return JointCuttingPlanSearch.Finding(planned, null,
                $"The support grid cannot hold it level, and no lead-in fits where a tab would: its tab at {at} "
                + "is the steadiest that fits, but it may still tip. Shorten the lead-in in Cutting Settings or "
                + "space the parts farther apart, then replan.");
        if (!prepared.IsClearOfInsideCorners(entry))
            yield return JointCuttingPlanSearch.Finding(planned, null,
                $"Its support tab at {at} is in an inside corner, because no lead-in fits clear of one; "
                + "its nub will be hard to grind flush.");
    }
}

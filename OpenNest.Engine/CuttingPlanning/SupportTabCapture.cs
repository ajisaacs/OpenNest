using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Capture-time support tabs for one regenerated part (docs/support-tabs.md): with a support
/// bed and <see cref="CNC.CuttingStrategy.CuttingParameters.SupportTabs"/> on, a part the bed
/// cannot hold level has its perimeter tabbed where the tab steadies it; a pinned perimeter
/// keeps its pin (the drawing wins) and only carries the analysis for replay to review. A
/// supported part, a missing tab configuration or material the analyzer cannot read leave the
/// preparation as it was.
/// </summary>
internal static class SupportTabCapture
{
    internal static PreparedContours Prepare(PreparedContours prepared, Program ownedClean, Vector location,
        CuttingPlanRequest request, CancellationToken token)
    {
        var parameters = request.ConfirmedParameters;
        if (prepared == null || request.SupportBed == null || parameters?.SupportTabs != true
            || parameters.TabConfig == null)
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
    /// A note for a planned support-tabbed part whose tab, at its perimeter entry, sits in an
    /// inside corner because no lead-in fits clear of one. Advisory: it never refuses or
    /// downgrades a plan. A tab that cannot steady its part is <see cref="Review"/>'s warning.
    /// </summary>
    internal static IEnumerable<CuttingPlanFinding> Notes(FixedProgramPlacement planned)
    {
        var prepared = planned.Prepared;
        if (prepared?.Support == null || prepared.IsPinned(prepared.PerimeterOrdinal)
            || planned.ContourChoices.FirstOrDefault(c => c.ContourOrdinal == prepared.PerimeterOrdinal) is not { } entry
            || prepared.IsClearOfInsideCorners(entry))
            yield break;
        var tab = entry.Point + planned.Location;
        var at = FormattableString.Invariant($"({tab.X:0.####}, {tab.Y:0.####})");
        yield return JointCuttingPlanSearch.Finding(planned, null,
            $"Its support tab at {at} is in an inside corner, because no lead-in fits clear of one; "
            + "its nub will be hard to grind flush.");
    }

    /// <summary>
    /// A <see cref="PostVerificationKind.PartSupport"/> warning when a regenerated part its bed
    /// cannot hold level is cut without a tab that keeps it level: its pin has no tab or a tab
    /// elsewhere, no steadying entry had a lead-in that fits, or no single tab can steady it.
    /// Null otherwise. Like other warnings it makes the proposal UNVERIFIED.
    /// </summary>
    internal static CuttingPlanFinding Review(FixedProgramPlacement source, IReadOnlyList<ContourChoice> choices)
    {
        var prepared = source.Prepared;
        if (prepared?.Support is not { } support || choices == null)
            return null;
        var perimeter = choices.FirstOrDefault(c => c.ContourOrdinal == prepared.PerimeterOrdinal);
        if (perimeter == null)
            return null;
        var tab = perimeter.Point + prepared.SupportOrigin;
        if (prepared.PerimeterTabbed && support.IsSteadiedByTabAt(tab))
            return null;
        var part = source.SourceOrdinal + 1;
        var held = support.Margin == double.NegativeInfinity
            ? "rests on no support contact"
            : support.Margin < 0
                ? FormattableString.Invariant($"has its center of gravity {-support.Margin:0.####} outside its support contacts")
                : FormattableString.Invariant($"has its center of gravity only {support.Margin:0.####} inside its support contacts");
        var why = !prepared.PerimeterTabbed
            ? $"drawing pin {prepared.PinNumberOf(prepared.PerimeterOrdinal)} cuts its outside without a tab"
            : prepared.IsPinned(prepared.PerimeterOrdinal)
                ? $"drawing pin {prepared.PinNumberOf(prepared.PerimeterOrdinal)} puts its tab where it does not steady the part"
                : support.TabCanSupport
                    ? "no lead-in fits at a point where its tab would steady the part"
                    : "no single tab can steady it";
        return new CuttingPlanFinding(source.SourceOrdinal, source.SourcePart, null, null,
            PostVerificationKind.PartSupport, FormattableString.Invariant(
                $"Part {part} {held} (needs {support.RequiredMargin:0.####}) and {why}; it may tip once cut free."));
    }
}

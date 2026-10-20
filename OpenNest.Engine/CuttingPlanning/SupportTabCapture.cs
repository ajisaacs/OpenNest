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
        // part's location is the material that drops onto the bed.
        var support = SupportAnalyzer.Analyze(request.SupportBed, request.SupportQuadrant,
            SupportMaterial.FromProgram(ownedClean, location));
        if (!support.IsComplete || support.IsSupported || support.BestTabPoint == null)
            return prepared;
        return prepared.WithSupportTab(support, location, parameters.TabConfig);
    }
}

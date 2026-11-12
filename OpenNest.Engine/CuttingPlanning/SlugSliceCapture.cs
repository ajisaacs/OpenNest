using System.Collections.Generic;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Capture-time slug slices for one regenerated part (docs/support-tabs.md#slug-slices): with
/// <see cref="CNC.CuttingStrategy.CuttingParameters.SliceTippingSlugs"/> on and a support bed,
/// the slug of each cutout the bed cannot hold level is sliced along the bed's contact lines
/// before the cutout is cut (<see cref="PreparedContours.WithSlugSlices"/>).
/// </summary>
internal static class SlugSliceCapture
{
    internal static PreparedContours Prepare(PreparedContours prepared, Vector location, CuttingPlanRequest request,
        IReadOnlyList<Box> partBoxes, CancellationToken token)
    {
        if (prepared == null || request.ConfirmedParameters?.SliceTippingSlugs != true || request.SupportBed == null)
            return prepared;
        token.ThrowIfCancellationRequested();
        // A cutout's box can never hold its own part's box, so every part's box may be passed.
        return prepared.WithSlugSlices(request.SupportBed, request.SupportQuadrant, location, partBoxes, token);
    }
}

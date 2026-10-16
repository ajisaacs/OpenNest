using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Capture-time preparation of a regenerated part's contours with its drawing's pins (D1: the
/// drawing wins). A pin that does not resolve exactly is never guessed: its contour is planned
/// from the plate settings and the plate gets a warning, so its proposal is UNVERIFIED.
/// </summary>
internal static class DrawingPinCapture
{
    internal static PreparedContours Prepare(Part source, int ordinal, Program ownedClean,
        CuttingParameters parameters, List<CuttingPlanFinding> warnings, CancellationToken token)
    {
        var prepared = PreparedContours.Capture(ownedClean, parameters, token);
        if (source.BaseDrawing.CutPins is not { Count: > 0 })
            return prepared;
        // The clean program is the drawing's own program rotated by the part's rotation, so the
        // resolver maps the source-frame pins onto exactly these contours.
        var resolutions = CutPinResolver.Resolve(source.BaseDrawing, source.Rotation, prepared, token);
        var pinned = PreparedContours.Capture(ownedClean, parameters, resolutions, token);
        foreach (var resolution in resolutions.Where(r => !r.IsResolved))
            warnings.Add(Warning(source, ordinal, resolution, Unresolved(resolution)));
        foreach (var rejected in pinned.RejectedPins)
            warnings.Add(Warning(source, ordinal, rejected, $"Drawing pin {rejected.PinIndex + 1} puts a tab on an "
                + "internal contour, but only the outside can be tabbed; planned with nest settings."));
        return pinned;
    }

    private static string Unresolved(CutPinResolution resolution)
    {
        var pin = resolution.PinIndex + 1;
        return resolution.Status switch
        {
            CutPinStatus.NotOnContour => $"Drawing pin {pin} no longer matches the geometry; planned with nest settings.",
            CutPinStatus.OnSeveralContours => $"Drawing pin {pin} touches more than one contour; planned with nest settings.",
            CutPinStatus.ContourPinnedTwice => $"Drawing pin {pin} shares its contour with another pin, so neither is used; "
                + "planned with nest settings.",
            _ => $"Drawing pin {pin} cannot be placed on this drawing's program; planned with nest settings.",
        };
    }

    private static CuttingPlanFinding Warning(Part source, int ordinal, CutPinResolution resolution, string message) =>
        new(ordinal, source, null, null, PostVerificationKind.FixedLead, message)
        {
            FixedLead = new(null, resolution.PinIndex + 1, null)
        };
}

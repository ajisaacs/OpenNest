using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

public enum CutPinStatus
{
    /// <summary>The pin lies on exactly one contour, which no other pin claims.</summary>
    Resolved,

    /// <summary>The pin no longer lies on any contour of the drawing.</summary>
    NotOnContour,

    /// <summary>The pin lies on more than one contour, so which one it pins is unknown.</summary>
    OnSeveralContours,

    /// <summary>Another pin lies on the same contour; neither is used.</summary>
    ContourPinnedTwice,

    /// <summary>The drawing's program cannot be prepared, or is not in the frame its source offset describes.</summary>
    UnsupportedGeometry,
}

/// <summary>
/// Where one drawing pin lands on a placed contour. <see cref="Point"/> is in the placed
/// part's local frame (add the part's location for plate coordinates): the exact native point
/// when resolved, otherwise where the pin maps to (<see cref="Vector.Invalid"/> when the drawing
/// frame is unknown). Ordinals match <see cref="PreparedContours"/> captured from the same
/// rotated clean program.
/// </summary>
public sealed record CutPinResolution(int PinIndex, ContourCutPin Pin, CutPinStatus Status, int ContourOrdinal,
    int EntityOrdinal, Vector Point)
{
    public bool IsResolved => Status == CutPinStatus.Resolved;
}

/// <summary>
/// Maps a drawing's source-frame pins onto its program contours at a placement rotation. A
/// pin never resolves by guessing: it must lie within the contour closure tolerance of exactly
/// one contour that no other pin claims, or it is reported unresolved with the reason.
/// </summary>
public static class CutPinResolver
{
    /// <summary>
    /// Resolves the drawing's pins for a part rotated by <paramref name="rotation"/> radians
    /// about the drawing program's origin, as <see cref="Part.Rotate(double)"/> does.
    /// </summary>
    public static IReadOnlyList<CutPinResolution> Resolve(Drawing drawing, double rotation, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (!double.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "Rotation must be finite.");
        var pins = drawing.CutPins ?? new List<ContourCutPin>();
        if (pins.Count == 0)
            return Array.Empty<CutPinResolution>();
        if (drawing.Program == null || !IsSourceFrame(drawing))
            return Unsupported(pins);

        var program = (Program)drawing.Program.Clone();
        if (!Math.Tolerance.IsEqualTo(rotation, 0))
            program.Rotate(rotation);

        PreparedContours contours;
        try
        {
            contours = PreparedContours.Capture(program, new CuttingParameters(), token);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
        {
            return Unsupported(pins);
        }

        return Resolve(drawing, rotation, contours, token);
    }

    /// <summary>Resolves the pins of a placed part at its current rotation.</summary>
    public static IReadOnlyList<CutPinResolution> Resolve(Part part, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Resolve(part.BaseDrawing, part.Rotation, token);
    }

    /// <summary>
    /// Resolves against contours the caller already captured. <paramref name="contours"/> must
    /// come from the drawing's clean program rotated by <paramref name="rotation"/>.
    /// </summary>
    public static IReadOnlyList<CutPinResolution> Resolve(Drawing drawing, double rotation, PreparedContours contours,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(contours);
        if (!double.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "Rotation must be finite.");
        var pins = drawing.CutPins ?? new List<ContourCutPin>();
        if (pins.Count == 0)
            return Array.Empty<CutPinResolution>();
        if (!IsSourceFrame(drawing))
            return Unsupported(pins);

        var offset = drawing.Source?.Offset ?? Vector.Zero;
        var results = new CutPinResolution[pins.Count];
        for (var i = 0; i < pins.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var pin = pins[i] ?? throw new ArgumentException($"Drawing pin {i + 1} is missing.", nameof(drawing));
            var local = (pin.Point - offset).Rotate(rotation);
            var near = contours.ContoursNear(local, NominalContourClosure.Tolerance);
            results[i] = near.Count switch
            {
                0 => new(i, pin, CutPinStatus.NotOnContour, -1, -1, local),
                1 => new(i, pin, CutPinStatus.Resolved, near[0].ContourOrdinal, near[0].EntityOrdinal, near[0].Point),
                _ => new(i, pin, CutPinStatus.OnSeveralContours, -1, -1, local),
            };
        }

        // At most one pin per contour. Choosing between two would be a guess, so neither is used.
        foreach (var claimed in results.Where(r => r.IsResolved).GroupBy(r => r.ContourOrdinal).Where(g => g.Count() > 1))
            foreach (var result in claimed)
                results[result.PinIndex] = result with { Status = CutPinStatus.ContourPinnedTwice };

        return results;
    }

    // Pins are stored relative to the source entities; the program is that frame moved by
    // -Source.Offset and never rotated in place. A rotated program (a canonical copy) is not.
    private static bool IsSourceFrame(Drawing drawing) =>
        drawing.Program != null && Math.Tolerance.IsEqualTo(drawing.Program.Rotation, 0);

    private static CutPinResolution[] Unsupported(List<ContourCutPin> pins) =>
        pins.Select((pin, i) => new CutPinResolution(i, pin, CutPinStatus.UnsupportedGeometry, -1, -1, Vector.Invalid))
            .ToArray();
}

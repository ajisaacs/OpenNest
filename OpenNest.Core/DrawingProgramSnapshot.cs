using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>
/// Captures drawing program text before an edit and rebuilds only the parts whose
/// drawing program changed. Drawing keys use reference identity because names are editable.
/// </summary>
public sealed class DrawingProgramSnapshot
{
    private readonly Dictionary<Drawing, Captured> programs;
    private readonly Func<Program, string> fingerprint;

    private DrawingProgramSnapshot(
        Dictionary<Drawing, Captured> programs,
        Func<Program, string> fingerprint)
    {
        this.programs = programs;
        this.fingerprint = fingerprint;
    }

    /// <summary>
    /// Capture before handing drawings to an editor, including its load operation:
    /// programs can be edited in place. The callback must include both the main
    /// program text and its hole sub-programs, and must not mutate the program.
    /// </summary>
    public static DrawingProgramSnapshot Capture(
        IEnumerable<Drawing> drawings,
        Func<Program, string> fingerprint)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        ArgumentNullException.ThrowIfNull(fingerprint);
        var programs = new Dictionary<Drawing, Captured>(ReferenceEqualityComparer.Instance);

        foreach (var drawing in drawings)
        {
            if (!drawing.IsCutOff && !programs.ContainsKey(drawing))
                programs.Add(drawing, new Captured(fingerprint(drawing.Program), SourceOffset(drawing)));
        }

        return new DrawingProgramSnapshot(programs, fingerprint);
    }

    /// <summary>
    /// Complete the captured edit by rebuilding changed drawings' parts across all plates.
    /// Part.Update clears obsolete lead-ins, tabs and locks; the placement is anchored to the
    /// source frame, so a rebuild that re-origins the program (Source.Offset moved, e.g. a new
    /// first pierce) leaves the geometry where it was on the plate.
    /// Unchanged and uncaptured drawings' parts retain their program instances and state.
    /// Returns the rebuilt parts so a UI can invalidate just their graphics.
    /// </summary>
    public IReadOnlyList<Part> UpdateChangedParts(IEnumerable<Plate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var originShifts = new Dictionary<Drawing, Vector>(ReferenceEqualityComparer.Instance);

        foreach (var entry in programs)
        {
            if (!entry.Key.IsCutOff
                && !string.Equals(entry.Value.Fingerprint, fingerprint(entry.Key.Program), StringComparison.Ordinal))
                originShifts.Add(entry.Key, SourceOffset(entry.Key) - entry.Value.SourceOffset);
        }

        var updated = new List<Part>();
        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (!originShifts.TryGetValue(part.BaseDrawing, out var shift))
                    continue;

                // A source point P sat at Location + R(P - old); keep it there with the new origin.
                var rotation = part.Rotation;
                part.Update();
                if (shift != Vector.Zero)
                    part.Offset(shift.Rotate(rotation));
                updated.Add(part);
            }
        }

        return updated;
    }

    private static Vector SourceOffset(Drawing drawing) => drawing.Source?.Offset ?? Vector.Zero;

    private readonly record struct Captured(string Fingerprint, Vector SourceOffset);
}

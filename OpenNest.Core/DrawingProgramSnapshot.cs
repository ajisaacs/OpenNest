using System;
using System.Collections.Generic;
using OpenNest.CNC;

namespace OpenNest;

/// <summary>
/// Captures drawing program text before an edit and rebuilds only the parts whose
/// drawing program changed. Drawing keys use reference identity because names are editable.
/// </summary>
public sealed class DrawingProgramSnapshot
{
    private readonly Dictionary<Drawing, string> programs;
    private readonly Func<Program, string> fingerprint;

    private DrawingProgramSnapshot(
        Dictionary<Drawing, string> programs,
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
        var programs = new Dictionary<Drawing, string>(ReferenceEqualityComparer.Instance);

        foreach (var drawing in drawings)
        {
            if (!drawing.IsCutOff && !programs.ContainsKey(drawing))
                programs.Add(drawing, fingerprint(drawing.Program));
        }

        return new DrawingProgramSnapshot(programs, fingerprint);
    }

    /// <summary>
    /// Complete the captured edit by rebuilding changed drawings' parts across all plates.
    /// Part.Update preserves the placement and clears obsolete lead-ins, tabs and locks.
    /// Unchanged and uncaptured drawings' parts retain their program instances and state.
    /// Returns the rebuilt parts so a UI can invalidate just their graphics.
    /// </summary>
    public IReadOnlyList<Part> UpdateChangedParts(IEnumerable<Plate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var changed = new HashSet<Drawing>(ReferenceEqualityComparer.Instance);

        foreach (var entry in programs)
        {
            if (!entry.Key.IsCutOff
                && !string.Equals(entry.Value, fingerprint(entry.Key.Program), StringComparison.Ordinal))
                changed.Add(entry.Key);
        }

        var updated = new List<Part>();
        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (!changed.Contains(part.BaseDrawing))
                    continue;

                part.Update();
                updated.Add(part);
            }
        }

        return updated;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Clamps;
using OpenNest.CNC;

namespace OpenNest.Diagnostics;

/// <summary>
/// Cheap ordered identity/pose check, not a geometry hash. In-place geometry editors must
/// explicitly invalidate before mutating. Capture and match only on the model's UI thread.
/// </summary>
public sealed class OverlapGeometryStamp
{
    private readonly Plate plate;
    private readonly Entry[] entries;
    private readonly (string Id, long X, long Y, long Length, long Width)[] zones;
    // Gaps are replaced, never edited in place, so their reference stands for their content.
    private readonly (CutOff Definition, CutOffAxis Axis, long X, long Y, long? Start, long? End,
        IReadOnlyList<CutOffGap> Gaps, long Overcut)[] cutoffs;
    private readonly (long Width, long Length, int Quadrant) sheet;
    private readonly long partSpacing;
    private readonly string commonLines;
    // The label also checks the sheet edge, so its spacing and outline make a result stale too.
    private readonly (long Left, long Bottom, long Right, long Top) edgeSpacing;
    private readonly SheetOutline outline;

    private OverlapGeometryStamp(Plate plate)
    {
        this.plate = plate;
        sheet = SheetState(plate);
        partSpacing = Bits(plate.PartSpacing);
        commonLines = CommonLineState(plate);
        edgeSpacing = EdgeState(plate);
        outline = plate.Outline;
        entries = new Entry[plate.Parts.Count];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new Entry(plate.Parts[i]);
        zones = new (string, long, long, long, long)[plate.ClampZones.Count];
        for (var i = 0; i < zones.Length; i++)
            zones[i] = ZoneState(plate.ClampZones[i]);
        cutoffs = new (CutOff, CutOffAxis, long, long, long?, long?, IReadOnlyList<CutOffGap>, long)[plate.CutOffs.Count];
        for (var i = 0; i < cutoffs.Length; i++)
            cutoffs[i] = CutOffState(plate.CutOffs[i]);
    }

    // Missing entries are malformed input the analysis itself reports; freshness must not
    // throw first (the desktop captures it before its error boundary).
    private static (string, long, long, long, long) ZoneState(ClampZone zone) =>
        zone?.Bounds is not { } box ? default
            : (zone.Id, Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width));

    private static (CutOff, CutOffAxis, long, long, long?, long?, IReadOnlyList<CutOffGap>, long) CutOffState(CutOff cut) =>
        cut == null ? default
            : (cut, cut.Axis, Bits(cut.Position.X), Bits(cut.Position.Y),
                cut.StartLimit.HasValue ? Bits(cut.StartLimit.Value) : null,
                cut.EndLimit.HasValue ? Bits(cut.EndLimit.Value) : null,
                cut.Gaps, Bits(cut.HeadLockOvercut));

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static (long, long, int) SheetState(Plate plate) =>
        (Bits(plate.Size.Width), Bits(plate.Size.Length), plate.Quadrant);

    private static (long, long, long, long) EdgeState(Plate plate) =>
        (Bits(plate.EdgeSpacing.Left), Bits(plate.EdgeSpacing.Bottom), Bits(plate.EdgeSpacing.Right),
            Bits(plate.EdgeSpacing.Top));

    public static OverlapGeometryStamp Capture(Plate plate) => new(plate);

    internal bool SameZones(Plate current)
    {
        // Stored zones are sheet-relative: a size or quadrant change moves them in the
        // parts' frame without changing their bits.
        if (!ReferenceEquals(plate, current) || current.ClampZones.Count != zones.Length
            || sheet != SheetState(current))
            return false;
        for (var i = 0; i < zones.Length; i++)
            if (zones[i] != ZoneState(current.ClampZones[i]))
                return false;
        return true;
    }

    internal bool SameCutOffs(Plate current)
    {
        if (!ReferenceEquals(plate, current) || current.CutOffs.Count != cutoffs.Length
            || sheet != SheetState(current))
            return false;
        for (var i = 0; i < cutoffs.Length; i++)
            if (cutoffs[i] != CutOffState(current.CutOffs[i]))
                return false;
        return true;
    }

    internal bool SameSpacing(Plate current) =>
        ReferenceEquals(plate, current) && partSpacing == Bits(current.PartSpacing)
        && commonLines == CommonLineState(current);

    /// <summary>The same sheet, edge spacing and outline: an unchanged part keeps its edge finding.</summary>
    internal bool SameSheet(Plate current) =>
        ReferenceEquals(plate, current) && sheet == SheetState(current) && edgeSpacing == EdgeState(current)
        && Equals(outline, current.Outline);

    // Which plate slots share a common-line group, and its kerf: the spacing check reads both.
    private static string CommonLineState(Plate plate) =>
        string.Join(";", plate.CommonLines.Select(group => Bits(group.Kerf) + ":"
            + string.Join(",", group.Members.Select(member => plate.Parts.IndexOf(member)))));

    public bool Matches(Plate current)
    {
        if (current.Parts.Count != entries.Length || partSpacing != Bits(current.PartSpacing)
            || commonLines != CommonLineState(current) || edgeSpacing != EdgeState(current)
            || !Equals(outline, current.Outline) || !SameZones(current) || !SameCutOffs(current))
            return false;
        for (var i = 0; i < entries.Length; i++)
            if (!entries[i].Matches(current.Parts[i]))
                return false;
        return true;
    }

    /// <summary>
    /// Conservative display reuse: only unchanged ordered slots on the same plate survive.
    /// Index-changing edits may hide extra pairs, but cannot attach old geometry to new parts.
    /// </summary>
    internal bool[] UnchangedSlots(Plate current)
    {
        var matches = new bool[entries.Length];
        if (!ReferenceEquals(plate, current))
            return matches;
        for (var i = 0; i < entries.Length && i < current.Parts.Count; i++)
            matches[i] = entries[i].Matches(current.Parts[i]);
        return matches;
    }

    private readonly struct Entry
    {
        private readonly Part part;
        private readonly Drawing drawing;
        private readonly Program placedProgram;
        private readonly Program cleanProgram;
        private readonly long x, y, rotation;
        private readonly bool isCutOff;

        public Entry(Part part)
        {
            this.part = part;
            drawing = part.BaseDrawing;
            placedProgram = part.Program;
            cleanProgram = drawing.Program;
            x = BitConverter.DoubleToInt64Bits(part.Location.X);
            y = BitConverter.DoubleToInt64Bits(part.Location.Y);
            rotation = BitConverter.DoubleToInt64Bits(part.Rotation);
            isCutOff = drawing.IsCutOff;
        }

        public bool Matches(Part current) =>
            ReferenceEquals(part, current)
            && ReferenceEquals(drawing, current.BaseDrawing)
            && ReferenceEquals(placedProgram, current.Program)
            && ReferenceEquals(cleanProgram, current.BaseDrawing.Program)
            && x == BitConverter.DoubleToInt64Bits(current.Location.X)
            && y == BitConverter.DoubleToInt64Bits(current.Location.Y)
            && rotation == BitConverter.DoubleToInt64Bits(current.Rotation)
            && isCutOff == current.BaseDrawing.IsCutOff;
    }
}

using System;
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
    private readonly (CutOff Definition, CutOffAxis Axis, long X, long Y, long? Start, long? End)[] cutoffs;
    private readonly (long Width, long Length, int Quadrant) sheet;

    private OverlapGeometryStamp(Plate plate)
    {
        this.plate = plate;
        sheet = (Bits(plate.Size.Width), Bits(plate.Size.Length), plate.Quadrant);
        entries = new Entry[plate.Parts.Count];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new Entry(plate.Parts[i]);
        zones = new (string, long, long, long, long)[plate.ClampZones.Count];
        for (var i = 0; i < zones.Length; i++)
        {
            var zone = plate.ClampZones[i];
            var box = zone.Bounds;
            zones[i] = (zone.Id, Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width));
        }
        cutoffs = new (CutOff, CutOffAxis, long, long, long?, long?)[plate.CutOffs.Count];
        for (var i = 0; i < cutoffs.Length; i++)
            cutoffs[i] = CutOffState(plate.CutOffs[i]);
    }

    private static (CutOff, CutOffAxis, long, long, long?, long?) CutOffState(CutOff cut) =>
        (cut, cut.Axis, Bits(cut.Position.X), Bits(cut.Position.Y),
            cut.StartLimit.HasValue ? Bits(cut.StartLimit.Value) : null,
            cut.EndLimit.HasValue ? Bits(cut.EndLimit.Value) : null);

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    public static OverlapGeometryStamp Capture(Plate plate) => new(plate);

    internal bool SameZones(Plate current)
    {
        if (!ReferenceEquals(plate, current) || current.ClampZones.Count != zones.Length)
            return false;
        for (var i = 0; i < zones.Length; i++)
        {
            var zone = current.ClampZones[i];
            var box = zone.Bounds;
            if (zones[i] != (zone.Id, Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width)))
                return false;
        }
        return true;
    }

    internal bool SameCutOffs(Plate current)
    {
        if (!ReferenceEquals(plate, current) || current.CutOffs.Count != cutoffs.Length
            || sheet != (Bits(current.Size.Width), Bits(current.Size.Length), current.Quadrant))
            return false;
        for (var i = 0; i < cutoffs.Length; i++)
            if (cutoffs[i] != CutOffState(current.CutOffs[i]))
                return false;
        return true;
    }

    public bool Matches(Plate current)
    {
        if (current.Parts.Count != entries.Length || !SameZones(current) || !SameCutOffs(current))
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

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

    private OverlapGeometryStamp(Plate plate)
    {
        this.plate = plate;
        entries = new Entry[plate.Parts.Count];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new Entry(plate.Parts[i]);
    }

    public static OverlapGeometryStamp Capture(Plate plate) => new(plate);

    public bool Matches(Plate current)
    {
        if (!ReferenceEquals(plate, current) || current.Parts.Count != entries.Length)
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

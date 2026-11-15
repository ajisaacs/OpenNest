using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The WinForms plate view's edits of the selection, toolkit-neutral: delete, nudge, step, push
/// and rotate. Each edit is one undo step named as in WinForms; repeated nudges and steps of the
/// same parts, and wheel rotations, undo as one. Every edit does nothing without a plate or
/// selected parts (Delete also acts on selected cut-offs alone) and redraws the view, and nothing
/// at all while the host's edits are locked (<see cref="IPlateToolHost.EditsLocked"/>).
/// </summary>
public sealed class PlateEdits
{
    public PlateEdits(IPlateToolHost host)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public IPlateToolHost Host { get; }

    /// <summary>How far an arrow-key nudge moves the selection, in plate units.</summary>
    public double OffsetIncrement { get; set; } = 10;

    /// <summary>
    /// Whether an arrow key steps the selection by its own size instead (<see cref="Step"/>).
    /// </summary>
    public bool CalculatedOffset { get; set; }

    /// <summary>How far one wheel notch rotates the selection, in degrees.</summary>
    public double RotateIncrementDegrees { get; set; } = 10;

    /// <summary>
    /// Removes the selected parts and cut-offs and clears the selection. When the plate had
    /// cut-offs, the remaining ones are rebuilt once both kinds are gone, so they close the gaps
    /// left around deleted parts.
    /// </summary>
    public void Delete()
    {
        var plate = Host.Plate;
        if (plate == null || Host.EditsLocked)
            return;
        var selection = Host.Selection;
        selection.Prune();
        var parts = selection.Parts.ToList();
        var cutOffs = selection.CutOffs.ToList();
        if (parts.Count == 0 && cutOffs.Count == 0)
            return;

        using var change = Host.BeginChange("Delete");
        var regenerateCutOffs = plate.CutOffs.Count > 0;
        change.MarkChanged();
        foreach (var cutOff in cutOffs)
            plate.CutOffs.Remove(cutOff);
        foreach (var part in parts)
            plate.Parts.Remove(part);
        selection.Clear();
        if (regenerateCutOffs)
            plate.RegenerateCutOffs(Host.CutOffSettings);
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>
    /// An arrow key: moves by <see cref="OffsetIncrement"/>, or with
    /// <see cref="CalculatedOffset"/> steps.
    /// </summary>
    public void Nudge(PushDirection direction)
    {
        if (CalculatedOffset)
        {
            Step(direction);
            return;
        }

        var offset = SpatialQuery.DirectionToOffset(direction, OffsetIncrement);
        NudgeBy(offset.X, offset.Y);
    }

    /// <summary>Moves the selection by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public void NudgeBy(double dx, double dy)
    {
        if (!TryGetSelectedParts(out var plate, out var parts))
            return;

        using var change = Host.BeginChange("Nudge", new SelectionKey("Nudge", plate, parts));
        change.MarkChanged();
        foreach (var part in parts)
            part.Offset(dx, dy);
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>
    /// Moves the selection one repeat step in <paramref name="direction"/>: its size along that
    /// axis plus the part spacing, measured on its geometry (<see cref="Compactor.Step"/>).
    /// </summary>
    public void Step(PushDirection direction)
    {
        if (!TryGetSelectedParts(out var plate, out var parts))
            return;

        using var change = Host.BeginChange("Nudge", new SelectionKey("Nudge", plate, parts));
        if (Compactor.Step(parts, plate, direction) > 0)
            change.MarkChanged();
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>
    /// Pushes the selection in <paramref name="direction"/> until it meets a part or the edge.
    /// </summary>
    public void Push(PushDirection direction)
    {
        if (!TryGetSelectedParts(out var plate, out var parts))
            return;

        using var change = Host.BeginChange("Push");
        change.MarkChanged();
        Compactor.Push(parts, plate, direction);
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>
    /// Rotates the selection by <paramref name="radians"/> about its center, keeping its lower-left
    /// corner; with <paramref name="coalesce"/>, quick repeats undo as one.
    /// </summary>
    public void Rotate(double radians, bool coalesce = false)
    {
        if (!TryGetSelectedParts(out var plate, out var parts))
            return;

        using var change = Host.BeginChange("Rotate",
            coalesce ? new SelectionKey("Rotate", plate, parts) : null);
        change.MarkChanged();
        var bounds = parts.GetBoundingBox();
        var center = bounds.Center;
        var anchor = bounds.Location;
        foreach (var part in parts)
            part.Rotate(radians, center);

        var diff = anchor - parts.GetBoundingBox().Location;
        foreach (var part in parts)
            part.Offset(diff);

        if (plate.CutOffs.Count > 0)
            plate.RegenerateCutOffs(Host.CutOffSettings);
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>A middle click: a quarter turn counterclockwise, its own undo step.</summary>
    public void RotateQuarterTurn() => Rotate(Angle.ToRadians(90));

    /// <summary>
    /// Shift+wheel: <paramref name="notches"/> wheel notches, positive turned away from the user,
    /// rotate clockwise by <see cref="RotateIncrementDegrees"/> each (a tenth of it when
    /// <paramref name="fine"/>, Ctrl held); a turn of the wheel undoes as one rotation.
    /// </summary>
    public void RotateByWheel(int notches, bool fine)
    {
        if (notches == 0)
            return;
        var increment = fine ? RotateIncrementDegrees * 0.1 : RotateIncrementDegrees;
        var degrees = (notches > 0 ? -increment : increment) * System.Math.Abs(notches);
        var angle = Angle.ToRadians(degrees);
        Rotate(angle, coalesce: true);
    }

    private bool TryGetSelectedParts(out Plate plate, out List<Part> parts)
    {
        plate = Host.Plate;
        parts = null;
        if (plate == null || Host.EditsLocked)
            return false;
        Host.Selection.Prune();
        parts = Host.Selection.Parts.ToList();
        return parts.Count > 0;
    }

    /// <summary>
    /// Equal for the same edit of the same parts, in the same order, on the same plate, so
    /// repeated nudges or wheel turns coalesce into one undo entry.
    /// </summary>
    private sealed class SelectionKey : IEquatable<SelectionKey>
    {
        private readonly string operation;
        private readonly Plate plate;
        private readonly Part[] parts;

        public SelectionKey(string operation, Plate plate, IEnumerable<Part> selection)
        {
            this.operation = operation;
            this.plate = plate;
            parts = selection.ToArray();
        }

        public bool Equals(SelectionKey other) =>
            other != null
            && operation == other.operation
            && ReferenceEquals(plate, other.plate)
            && parts.Length == other.parts.Length
            && parts.Zip(other.parts).All(pair => ReferenceEquals(pair.First, pair.Second));

        public override bool Equals(object obj) => Equals(obj as SelectionKey);

        public override int GetHashCode() =>
            HashCode.Combine(operation, RuntimeHelpers.GetHashCode(plate), parts.Length);
    }
}

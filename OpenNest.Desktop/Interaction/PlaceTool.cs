using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CommonLines;
using OpenNest.Desktop.Rendering;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The WinForms Clone Parts action, toolkit-neutral: copies of the source parts follow the
/// pointer (their combined lower-left corner at it) and each left click places another copy on
/// the plate, as one "Add Part"/"Add Parts" undo step. Shift settles the copy toward the plate's
/// quadrant corner on the parts' geometry; Ctrl alone settles it on bounding boxes only. Copied
/// common-line groups are joined again among the placed copies, and cut-offs are rebuilt. The
/// tool stays active until Escape and survives a plate change; <see cref="PlateEdits"/> nudges,
/// pushes and rotates its <see cref="Previews"/> while it is active, and its Delete removes the
/// sources (<see cref="DeleteSources"/>).
/// </summary>
public sealed class PlaceTool : PlateTool
{
    private readonly List<Part> previews;
    private readonly List<Part> sources;
    private readonly List<(int[] Members, double Kerf)> commonLines;
    private readonly Func<SettleOrder> placementOrder;
    private ScreenPoint? restingPoint;

    /// <param name="sources">The parts copied; kept so Delete can remove them.</param>
    /// <param name="placementOrder">
    /// Which way Shift+click and Ctrl+click settle the copy, read at each click so a changed
    /// setting applies without restarting the tool.
    /// </param>
    public PlaceTool(
        IPlateToolHost host, IReadOnlyList<Part> sources, Func<SettleOrder> placementOrder)
        : base(host)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(placementOrder);
        this.placementOrder = placementOrder;
        this.sources = sources.ToList();
        previews = this.sources.Select(source => source.Clone() as Part).ToList();
        commonLines = CopiedCommonLines(host.Plate, this.sources);
    }

    public override string Name => "Clone Parts";

    public override bool IsBusy => false;

    public override bool SurvivesPlateChange => true;

    /// <summary>The copies following the pointer; they are not on the plate.</summary>
    public IReadOnlyList<Part> Previews => previews;

    public override void Cancel() { }

    public override void OnPlateChanged() => Host.Invalidate();

    // The common-line groups among the copied parts, by index into the copy, so each placement
    // joins its copies the same way. Read now: Cut removes the sources, and their groups, next.
    private static List<(int[] Members, double Kerf)> CopiedCommonLines(
        Plate plate, List<Part> copied) =>
        CommonLineGroup.AmongParts(plate, copied);

    /// <summary>
    /// Moves the previews' lower-left corner to the pointer. A move to the screen point last seen
    /// is ignored, as WinForms ignores the mouse moves posted when a dialog opens or closes, so
    /// an arrow-key nudge of the previews is not thrown away.
    /// </summary>
    public override bool PointerMoved(ToolPointer pointer)
    {
        // The WinForms view raises no mouse move while its edits are locked.
        if (Host.EditsLocked)
            return false;
        if (restingPoint == pointer.Screen)
            return true;

        restingPoint = pointer.Screen;
        if (previews.Count > 0)
        {
            var offset = pointer.World - previews.GetBoundingBox().Location;
            foreach (var preview in previews)
                preview.Offset(offset);
        }
        Host.Invalidate();
        return true;
    }

    public override bool PointerPressed(ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left || Host.Plate == null || Host.EditsLocked)
            return false;
        Apply(pointer.Modifiers);
        return true;
    }

    /// <summary>
    /// Places a copy of the previews on the plate (see the class summary); the previews stay
    /// where they settled, so the next click places another copy beside this one.
    /// </summary>
    public void Apply(ToolModifiers modifiers)
    {
        var plate = Host.Plate;
        if (plate == null || Host.EditsLocked || previews.Count == 0)
            return;

        // Settling moves only the previews, which are not on the nest, so it runs before the
        // step opens: if reading the order or settling throws, there is nothing to roll back.
        var shift = (modifiers & ToolModifiers.Shift) == ToolModifiers.Shift;
        var boxesOnly = !shift && (modifiers & ToolModifiers.Control) == ToolModifiers.Control;
        if (shift || boxesOnly)
        {
            var (horizontal, vertical) = SettleDirections(plate.Quadrant);
            Compactor.SettlePlacement(previews, plate, horizontal, vertical,
                order: placementOrder(), boundingBoxesOnly: boxesOnly);
            Host.Invalidate();
        }

        using var change = Host.BeginChange(previews.Count == 1 ? "Add Part" : "Add Parts");
        change.MarkChanged();
        var placed = previews.Select(preview => preview.Clone() as Part).ToList();
        foreach (var part in placed)
            plate.Parts.Add(part);
        foreach (var (members, kerf) in commonLines)
            plate.JoinCommonLine(members.Select(i => placed[i]), kerf);

        if (plate.CutOffs.Count > 0)
            plate.RegenerateCutOffs(Host.CutOffSettings);
        Host.Invalidate();
        change.Complete();
    }

    /// <summary>
    /// Removes the source parts, and any parts selected on the plate (Select All while placing),
    /// as one "Delete" undo step; the previews stay. Recorded only when any of them was still on
    /// the plate.
    /// </summary>
    public void DeleteSources()
    {
        var plate = Host.Plate;
        if (plate == null || Host.EditsLocked)
            return;

        var doomed = sources.Concat(Host.Selection.Parts).ToList();
        using var change = Host.BeginChange("Delete");
        var removing = doomed.Any(plate.Parts.Contains);
        if (removing)
            change.MarkChanged();
        foreach (var part in doomed)
            plate.Parts.Remove(part);

        // Only after a removal: a rebuild gives the cut-offs new programs, which would count as
        // an unrecorded change (WinForms rebuilds either way).
        if (removing && plate.CutOffs.Count > 0)
            plate.RegenerateCutOffs(Host.CutOffSettings);
        Host.Selection.Clear();
        Host.Invalidate();
        change.Complete();
    }

    // Toward the quadrant's origin corner, as WinForms: 1 lower-left, 2 lower-right,
    // 3 upper-right, 4 upper-left.
    private static (PushDirection Horizontal, PushDirection Vertical) SettleDirections(
        int quadrant) =>
        quadrant switch
        {
            2 => (PushDirection.Right, PushDirection.Down),
            3 => (PushDirection.Right, PushDirection.Up),
            4 => (PushDirection.Left, PushDirection.Up),
            _ => (PushDirection.Left, PushDirection.Down),
        };
}

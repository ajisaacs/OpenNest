using OpenNest.Geometry;
using OpenNest.Undo;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The default plate tool, with the WinForms Select action's selection rules:
/// <list type="bullet">
/// <item>A left click within <see cref="CutOffPickPixels"/> of a cut-off's stroke selects that cut-off alone.</item>
/// <item>A left click on a part selects it (with its common-line group) in place of the selection;
/// with Ctrl alone held, or on a part already selected, it adds to the selection instead.</item>
/// <item>Dragging from a part moves every selected part with the pointer, as one "Move" undo
/// step; on release the plate's cut-offs are rebuilt around the parts' new places.</item>
/// <item>A left press on an empty area clears the selection and starts a selection window; on
/// release the window adds the parts and cut-offs it selects (<see cref="SelectionWindow"/>).</item>
/// <item>A right click on an unselected part selects it alone, for its menu; on an empty area it
/// clears the selection.</item>
/// </list>
/// Cancelling ends a move in progress where it is (still one undo step) and clears the selection.
/// Losing the pointer mid-drag ends a move as a release where it is and keeps the selection; a
/// selection window is dropped without selecting.
/// <para>
/// While the host's edits are locked (<see cref="IPlateToolHost.EditsLocked"/>) a press does
/// nothing at all, as in WinForms, whose <c>PlateView.OnMouseDown</c> returns before the Select
/// action sees the press: nothing is selected or deselected and no move starts. A move already
/// open when the lock appears is left to finish at its release: a lock is started by a command,
/// not mid-drag, and ending the move then would leave WinForms' quirk of a drag scope held open
/// until some later release.
/// </para>
/// </summary>
public sealed class SelectTool : PlateTool
{
    /// <summary>How near, in pixels, a click must be to a cut-off's stroke to pick it.</summary>
    public const double CutOffPickPixels = 5;

    public SelectTool(IPlateToolHost host)
        : base(host)
    {
    }

    public override string Name => "Select";

    /// <summary>The window being dragged, or null.</summary>
    public SelectionWindow? Window { get; private set; }

    /// <summary>Whether a drag is moving the selected parts.</summary>
    public bool IsMoving => move != null;

    private ChangeScope move;
    private Vector lastMoveWorld;

    /// <summary>Busy while dragging a window or while anything is selected, so Escape first deselects.</summary>
    public override bool IsBusy => Window != null || !Host.Selection.IsEmpty;

    public override void Cancel()
    {
        // Parts already dragged stay where they are, so the move is recorded.
        EndMove();
        Window = null;
        Host.Selection.Clear();
        Host.Invalidate();
    }

    public override bool PointerPressed(ToolPointer pointer)
    {
        var plate = Host.Plate;
        if (plate == null || Host.EditsLocked)
            return false;
        var selection = Host.Selection;
        selection.Prune();

        if (pointer.Button == ToolButton.Left)
        {
            EndMove();
            Window = null;
            var tolerance = Host.View.LengthToWorld(CutOffPickPixels);
            var cutOff = Host.HitTester.CutOffAt(plate, pointer.World, tolerance);
            if (cutOff != null)
            {
                selection.SelectOnly(cutOff);
                Host.Invalidate();
                return true;
            }
            selection.ClearCutOffs();

            var part = Host.HitTester.PartAt(plate, pointer.World);
            if (part == null)
            {
                selection.Clear();
                Window = new SelectionWindow(pointer.World, pointer.World);
            }
            else
            {
                if (pointer.Modifiers != ToolModifiers.Control && !selection.Contains(part))
                    selection.Clear();
                selection.Select(part);
                move = Host.BeginChange("Move");
                lastMoveWorld = pointer.World;
            }
            Host.Invalidate();
            return true;
        }

        if (pointer.Button == ToolButton.Right)
        {
            var part = Host.HitTester.PartAt(plate, pointer.World);
            if (part == null)
                Cancel();
            else if (!selection.Contains(part))
            {
                selection.SelectOnly(part);
                Host.Invalidate();
            }
            return true;
        }

        return false;
    }

    public override bool PointerMoved(ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return false;
        if (move != null)
        {
            var offset = pointer.World - lastMoveWorld;
            lastMoveWorld = pointer.World;
            move.MarkChanged();
            foreach (var part in Host.Selection.Parts)
                part.Offset(offset.X, offset.Y);
            Host.Invalidate();
            return true;
        }
        if (Window is not { } window)
            return false;
        Window = window with { Second = pointer.World };
        Host.Invalidate();
        return true;
    }

    public override bool PointerReleased(ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return false;
        if (move != null)
        {
            FinishMove();
            return true;
        }
        if (Window is not { } window)
            return false;
        window = window with { Second = pointer.World };
        Window = null;
        var plate = Host.Plate;
        if (plate != null)
        {
            Host.Selection.Select(Host.HitTester.PartsIn(plate, window));
            Host.Selection.Select(Host.HitTester.CutOffsIn(plate, window));
        }
        Host.Invalidate();
        return true;
    }

    public override void PointerCaptureLost()
    {
        if (move != null)
            FinishMove();
        Window = null;
        Host.Invalidate();
    }

    /// <summary>
    /// Ends the open move as a release does: the cut-offs are rebuilt around moved parts, then
    /// the step is recorded. If rebuilding throws, the scope is disposed incomplete, so the move
    /// rolls back, and the exception propagates.
    /// </summary>
    private void FinishMove()
    {
        var scope = move;
        move = null;
        try
        {
            // Only after a real move: rebuilding gives the cut-offs new programs, which would
            // otherwise count as an unrecorded change after a plain click.
            var movedOn = Host.Plate;
            if (scope.IsChanged && movedOn != null && Host.Selection.Parts.Count > 0
                && movedOn.CutOffs.Count > 0)
                movedOn.RegenerateCutOffs(Host.CutOffSettings);
            scope.Complete();
        }
        finally
        {
            scope.Dispose();
            Host.Invalidate();
        }
    }

    private void EndMove()
    {
        var scope = move;
        move = null;
        if (scope == null)
            return;
        scope.Complete();
        scope.Dispose();
    }
}

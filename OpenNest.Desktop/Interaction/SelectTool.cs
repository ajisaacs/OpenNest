namespace OpenNest.Desktop.Interaction;

/// <summary>
/// The default plate tool, with the WinForms Select action's selection rules:
/// <list type="bullet">
/// <item>A left click within <see cref="CutOffPickPixels"/> of a cut-off's stroke selects that cut-off alone.</item>
/// <item>A left click on a part selects it (with its common-line group) in place of the selection;
/// with Ctrl alone held, or on a part already selected, it adds to the selection instead.</item>
/// <item>A left press on an empty area clears the selection and starts a selection window; on
/// release the window adds the parts and cut-offs it selects (<see cref="SelectionWindow"/>).</item>
/// <item>A right click on an unselected part selects it alone, for its menu; on an empty area it
/// clears the selection.</item>
/// </list>
/// Moving the selection by dragging belongs to the move tools built on this one.
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

    /// <summary>Busy while dragging a window or while anything is selected, so Escape first deselects.</summary>
    public override bool IsBusy => Window != null || !Host.Selection.IsEmpty;

    public override void Cancel()
    {
        Window = null;
        Host.Selection.Clear();
        Host.Invalidate();
    }

    public override bool PointerPressed(ToolPointer pointer)
    {
        var plate = Host.Plate;
        if (plate == null)
            return false;
        var selection = Host.Selection;
        selection.Prune();

        if (pointer.Button == ToolButton.Left)
        {
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
        if (Window is not { } window || pointer.Button != ToolButton.Left)
            return false;
        Window = window with { Second = pointer.World };
        Host.Invalidate();
        return true;
    }

    public override bool PointerReleased(ToolPointer pointer)
    {
        if (Window is not { } window || pointer.Button != ToolButton.Left)
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
}

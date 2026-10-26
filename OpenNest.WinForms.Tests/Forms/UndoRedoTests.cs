using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using Size = OpenNest.Geometry.Size;

namespace OpenNest.WinForms.Tests.Forms;

// Undo swaps whole nests into the window. Reads Application.OpenForms through the busy
// guards, so it must not overlap fill-lifetime tests.
[Collection("Fill operation lifetime")]
public class UndoRedoTests
{
    [Fact]
    public void UndoKeepsThePlateTheViewportAndTheSelection() => Run(env =>
    {
        var editor = env.Editor;
        var view = editor.PlateView;
        editor.PlateManager.LoadAt(1);
        view.SelectAll();
        var before = view.Plate.Parts.Select(p => p.Location).ToArray();
        view.Selection.NudgeSelected(5, 0);
        view.RestoreViewport((2.5f, new PointF(13, 17)));

        Assert.True(env.Host.TryUndo(editor));

        Assert.Equal(1, editor.PlateManager.CurrentIndex);
        Assert.Same(editor.Nest.Plates[1], view.Plate);
        Assert.Equal((2.5f, new PointF(13, 17)), view.CaptureViewport());
        Assert.Equal(before, view.Plate.Parts.Select(p => p.Location).ToArray());
        Assert.Equal(view.Plate.Parts, view.SelectedParts.Select(p => p.BasePart));
        Assert.All(view.SelectedParts, layout => Assert.True(layout.IsSelected));
    });

    [Fact]
    public void UndoKeepsTheNestIdentityAndTheDocument() => Run(env =>
    {
        var editor = env.Editor;
        var document = editor.Document;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.Selection.DeleteSelected();
        editor.Nest.Name = "renamed";
        editor.Nest.Status = NestStatus.HasBeenCut;

        Assert.True(env.Host.TryUndo(editor));

        Assert.Same(document, editor.Document);
        Assert.Equal("renamed", editor.Nest.Name);
        Assert.Equal(NestStatus.HasBeenCut, editor.Nest.Status);
        Assert.Equal(2, editor.Nest.Plates[0].Parts.Count);
    });

    [Fact]
    public void EachCommandRecordsOneEntryAndRedoRepeatsIt() => Run(env =>
    {
        var editor = env.Editor;
        var view = editor.PlateView;
        editor.PlateManager.LoadAt(0);
        view.SelectAll();

        view.RotateSelectedParts(System.Math.PI / 2);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.Equal("Rotate", editor.History.UndoLabel);

        view.PushSelected(PushDirection.Left);
        view.AlignSelected(AlignType.Bottom);
        view.Selection.DeleteSelected();
        Assert.Equal(4, editor.History.UndoCount);
        Assert.Equal("Delete", editor.History.UndoLabel);
        Assert.Empty(editor.Nest.Plates[0].Parts);

        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(2, editor.Nest.Plates[0].Parts.Count);
        Assert.Equal("Delete", editor.History.RedoLabel);
        Assert.True(env.Host.TryRedo(editor));
        Assert.Empty(editor.Nest.Plates[0].Parts);
    });

    [Fact]
    public void ACommandThatChangesNothingRecordsNothing() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.DeselectAll();

        editor.PlateView.Selection.DeleteSelected();
        editor.PlateView.RotateSelectedParts(1);

        Assert.Equal(0, editor.History.UndoCount);
    });

    [Fact]
    public void NudgesOfOneSelectionUndoAsOne() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        var start = editor.Nest.Plates[0].Parts[0].Location;

        for (var i = 0; i < 3; i++)
            editor.PlateView.Selection.NudgeSelected(1, 0);

        Assert.Equal(1, editor.History.UndoCount);
        Assert.Equal("Nudge", editor.History.UndoLabel);
        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(start, editor.Nest.Plates[0].Parts[0].Location);
    });

    [Fact]
    public void ADragRecordsOneEntry() => Run(env =>
    {
        var editor = env.Editor;
        var view = editor.PlateView;
        editor.PlateManager.LoadAt(0);
        view.ZoomToFit();
        var part = view.Plate.Parts[0];
        var start = part.Location;
        var grab = view.PointWorldToControl(part.BoundingBox.Center);

        Mouse(view, "OnMouseMove", MouseButtons.None, grab);
        Mouse(view, "OnMouseDown", MouseButtons.Left, grab);
        for (var step = 1; step <= 5; step++)
            Mouse(view, "OnMouseMove", MouseButtons.Left, new Point(grab.X + step * 4, grab.Y));
        Mouse(view, "OnMouseUp", MouseButtons.Left, new Point(grab.X + 20, grab.Y));

        Assert.NotEqual(start, part.Location);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.Equal("Move", editor.History.UndoLabel);
        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(start, editor.Nest.Plates[0].Parts[0].Location);
    });

    [Fact]
    public void AClickWithoutMovingRecordsNothing() => Run(env =>
    {
        var editor = env.Editor;
        var view = editor.PlateView;
        editor.PlateManager.LoadAt(0);
        view.ZoomToFit();
        var grab = view.PointWorldToControl(view.Plate.Parts[0].BoundingBox.Center);

        Mouse(view, "OnMouseMove", MouseButtons.None, grab);
        Mouse(view, "OnMouseDown", MouseButtons.Left, grab);
        Mouse(view, "OnMouseUp", MouseButtons.Left, grab);

        Assert.Single(view.SelectedParts);
        Assert.Equal(0, editor.History.UndoCount);
    });

    [Fact]
    public void AScopeLeftIncompleteRollsBack() => Run(env =>
    {
        var editor = env.Editor;
        var start = editor.Nest.Plates[0].Parts[0].Location;

        using (var change = editor.BeginChange("Broken"))
        {
            using (var inner = editor.BeginChange("Inner"))
            {
                inner.MarkChanged();
                editor.Nest.Plates[0].Parts[0].Offset(7, 0);
                inner.Complete();
            }
            // Not completed: an exception or a cancellation.
        }

        Assert.Equal(start, editor.Nest.Plates[0].Parts[0].Location);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.False(editor.IsChangeInProgress);
    });

    [Fact]
    public void AnIncompleteInnerScopeRollsBackTheWholeEdit() => Run(env =>
    {
        var editor = env.Editor;
        var start = editor.Nest.Plates[0].Parts[0].Location;

        using (var change = editor.BeginChange("Outer"))
        {
            change.MarkChanged();
            editor.Nest.Plates[0].Parts[0].Offset(7, 0);
            using (editor.BeginChange("Inner")) { }
            change.Complete();
        }

        Assert.Equal(start, editor.Nest.Plates[0].Parts[0].Location);
        Assert.Equal(0, editor.History.UndoCount);
    });

    [Fact]
    public void AnUnrecordedEditBecomesItsOwnEntryBeforeUndo() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.RotateSelectedParts(System.Math.PI);
        var rotated = editor.Nest.Plates[0].Parts[0].Location;
        editor.Nest.Plates[0].Parts[0].Offset(3, 0); // An edit no scope recorded.

        Assert.True(env.Host.TryUndo(editor));

        Assert.Equal(rotated, editor.Nest.Plates[0].Parts[0].Location);
        Assert.Equal("Rotate", editor.History.UndoLabel);
        Assert.Equal("Change", editor.History.RedoLabel);
    });

    [Fact]
    public void UndoWaitsForAFillAndABackgroundSave() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.Selection.DeleteSelected();

        SetBackingField(editor.PlateView, "IsFillInProgress", true);
        try
        {
            Assert.True(env.Host.IsUndoBusy(editor));
            Assert.False(env.Host.TryUndo(editor));
        }
        finally
        {
            SetBackingField(editor.PlateView, "IsFillInProgress", false);
        }

        var storage = typeof(MainForm).GetField("storage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(env.Host)!;
        SetBackingField(storage, "IsSaving", true);
        try
        {
            Assert.False(env.Host.TryUndo(editor));
            Assert.Empty(editor.Nest.Plates[0].Parts);
        }
        finally
        {
            SetBackingField(storage, "IsSaving", false);
        }

        // A selection alone is not busy work for undo.
        editor.PlateView.SelectAll();
        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(2, editor.Nest.Plates[0].Parts.Count);
    });

    [Fact]
    public void TextEditorsKeepTheirOwnUndo()
    {
        using var panel = new Panel();
        var text = new TextBox();
        panel.Controls.Add(text);
        using var numeric = new System.Windows.Forms.NumericUpDown();
        var edit = numeric.Controls.OfType<TextBoxBase>().Single();
        using var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        using var view = new PlateView();

        Assert.True(MainForm.KeepsOwnUndo(text));
        Assert.True(MainForm.KeepsOwnUndo(edit));
        Assert.True(MainForm.KeepsOwnUndo(numeric));
        Assert.False(MainForm.KeepsOwnUndo(list));
        Assert.False(MainForm.KeepsOwnUndo(view));
        Assert.False(MainForm.KeepsOwnUndo(null));
    }

    [Fact]
    public void CtrlZInAFocusedTextBoxLeavesTheNestAlone() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.Selection.DeleteSelected();
        var box = new TextBox();
        editor.Controls.Add(box);
        box.Focus();
        Application.DoEvents();
        if (!box.Focused)
            return; // No interactive focus in this session; KeepsOwnUndo covers the rule.

        var handled = (bool)typeof(MainForm).GetMethod("ProcessUndoKeys", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(env.Host, new object[] { Keys.Control | Keys.Z })!;

        Assert.False(handled);
        Assert.Empty(editor.Nest.Plates[0].Parts);
        Assert.Equal(1, editor.History.UndoCount);
    });

    [Fact]
    public void UndoWithAnEmptyPlateInTheMiddleLandsOnTheSamePlate() => Run(env =>
    {
        var editor = env.Editor;
        var nest = editor.Nest;
        // Plates: 0 parts, 1 parts, 2 empty, 3 parts, then the empty sentinel.
        Assert.Empty(nest.Plates[2].Parts);
        editor.PlateManager.LoadAt(3);
        editor.PlateView.SelectAll();
        var start = nest.Plates[3].Parts[0].Location;
        editor.PlateView.Selection.NudgeSelected(0, 4);

        Assert.True(env.Host.TryUndo(editor));

        // A copy that dropped the empty plate would shift this one to index 2.
        Assert.Equal(5, editor.Nest.Plates.Count);
        Assert.Equal(3, editor.PlateManager.CurrentIndex);
        Assert.Same(editor.Nest.Plates[3], editor.PlateView.Plate);
        Assert.Equal(start, Assert.Single(editor.PlateView.Plate.Parts).Location);
        Assert.Empty(editor.Nest.Plates[2].Parts);
    });

    [Fact]
    public void AddingAndRemovingPlatesUndo() => Run(env =>
    {
        var editor = env.Editor;
        var count = editor.Nest.Plates.Count;
        editor.AddPlate();
        Assert.Equal("Add Plate", editor.History.UndoLabel);
        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(count, editor.Nest.Plates.Count);

        editor.PlateManager.LoadAt(1);
        editor.RemoveCurrentPlate();
        Assert.Equal(count - 1, editor.Nest.Plates.Count);
        Assert.True(env.Host.TryUndo(editor));
        Assert.Equal(count, editor.Nest.Plates.Count);
        Assert.Equal(1, editor.PlateManager.CurrentIndex);
    });

    [Fact]
    public void TheEditMenuNamesTheNextUndoAndRedo() => Run(env =>
    {
        var editor = env.Editor;
        editor.PlateManager.LoadAt(0);
        editor.PlateView.SelectAll();
        editor.PlateView.Selection.DeleteSelected();

        var undo = MenuItem(env.Host, "mnuEditUndo");
        var redo = MenuItem(env.Host, "mnuEditRedo");
        Assert.Equal("Undo Delete", undo.Text);
        Assert.True(undo.Enabled);
        Assert.False(redo.Enabled);

        env.Host.TryUndo(editor);
        Assert.Equal("Undo", undo.Text);
        Assert.False(undo.Enabled);
        Assert.Equal("Redo Delete", redo.Text);
        Assert.True(redo.Enabled);
    });

    private sealed record Env(MainForm Host, EditNestForm Editor);

    private static void Run(Action<Env> body) => StaTestThread.Run(() =>
    {
        using var host = new Host();
        host.Show();
        using var editor = new EditNestForm(BuildNest()) { MdiParent = host };
        editor.PlateView.SetOverlapAutoCheck(null);
        editor.Show();
        editor.Activate();
        body(new Env(host, editor));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static Nest BuildNest()
    {
        var nest = new Nest("undo");
        var square = new Drawing("square", Square());
        nest.Drawings.Add(square);
        int[] partsPerPlate = { 2, 2, 0, 1 };
        foreach (var count in partsPerPlate)
        {
            var plate = nest.CreatePlate();
            plate.Size = new Size(100, 100);
            for (var i = 0; i < count; i++)
                plate.Parts.Add(new Part(square, new Vector(5 + i * 20, 5)));
        }
        return nest;
    }

    private static ToolStripMenuItem MenuItem(MainForm host, string name) =>
        (ToolStripMenuItem)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static void Mouse(PlateView view, string method, MouseButtons button, Point location) =>
        typeof(PlateView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new MouseEventArgs(button, 1, location.X, location.Y, 0) });

    private static void SetBackingField(object target, string property, object value) =>
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static CNC.Program Square()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    private sealed class Host : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Controls;

[Collection("Fill operation lifetime")]
public class HideNestedDrawingsNoteTests
{
    [Fact]
    public void HidingEveryDrawingShowsANoteThatTurnsTheFilterOff() => RunSta(() =>
    {
        var nest = new Nest("hide");
        var drawing = new Drawing("a", Square());
        drawing.Quantity.Required = 1;
        nest.Drawings.Add(drawing);
        var plate = new Plate(100, 100) { Quantity = 1 };
        nest.Plates.Add(plate);
        plate.Parts.Add(new Part(drawing));
        Assert.Equal(0, drawing.Quantity.Remaining);

        using var form = new EditNestForm(nest) { Width = 1100, Height = 700 };
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();
        Application.DoEvents();

        var list = Field<DrawingListBox>(form, "drawingListBox1");
        var hide = Field<ToolStripButton>(form, "hideNestedButton");
        Assert.Null(list.EmptyText);

        hide.Checked = true;

        Assert.Empty(list.Items);
        Assert.NotNull(list.EmptyText);

        typeof(DrawingListBox)
            .GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list, [new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0)]);

        Assert.False(hide.Checked);
        Assert.Single(list.Items);
        Assert.Null(list.EmptyText);
    });

    [Fact]
    public void ListWithRowsNeverRaisesTheNoteClick() => RunSta(() =>
    {
        using var list = new ClickableDrawingListBox { Size = new Size(260, 400), EmptyText = "note" };
        list.Items.Add(new Drawing("a", Square()));
        var clicks = 0;
        list.EmptyTextClicked += (_, _) => clicks++;

        list.Release(new MouseEventArgs(MouseButtons.Left, 1, 20, 300, 0));
        Assert.Equal(0, clicks);

        list.Items.Clear();
        list.Release(new MouseEventArgs(MouseButtons.Right, 1, 20, 20, 0));
        Assert.Equal(0, clicks);

        list.Release(new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
        Assert.Equal(1, clicks);
    });

    private static T Field<T>(EditNestForm form, string name) =>
        (T)typeof(EditNestForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    private static Program Square()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");

    private sealed class ClickableDrawingListBox : DrawingListBox
    {
        public void Release(MouseEventArgs e) => OnMouseUp(e);
    }
}

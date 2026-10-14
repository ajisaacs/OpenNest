using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Controls;

[Collection("Fill operation lifetime")]
public class DrawingListPickupTests
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    [Fact]
    public void PressingADrawingRowPicksItUpWithoutWaitingForClick() => RunSta(() =>
    {
        var (form, list, second) = CreateEditor();
        using (form)
        {
            var center = RowCenter(list, list.Items.IndexOf(second));

            // Only the press: a mouse that moves before release never produces Click.
            SendMessage(list.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(center));
            try
            {
                EnterPlateView(form);

                var clone = Assert.IsType<ActionClone>(form.PlateView.Actions.CurrentAction);
                Assert.Same(second, ClonedDrawing(clone));
            }
            finally
            {
                SendMessage(list.Handle, WM_LBUTTONUP, 0, MakeLParam(center));
            }
        }
    });

    [Fact]
    public void CompletedDragDropDoesNotAttachASecondCopy() => RunSta(() =>
    {
        var (form, list, second) = CreateEditor();
        using (form)
        {
            var center = RowCenter(list, list.Items.IndexOf(second));
            SendMessage(list.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(center));
            SendMessage(list.Handle, WM_LBUTTONUP, 0, MakeLParam(center));

            typeof(EditNestForm)
                .GetMethod("drawingListBox1_DrawingDropped", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [list, second]);
            EnterPlateView(form);

            Assert.IsType<ActionSelect>(form.PlateView.Actions.CurrentAction);
        }
    });

    [Theory]
    [InlineData(MouseButtons.Right, 1, false)]
    [InlineData(MouseButtons.Left, 2, false)]
    [InlineData(MouseButtons.Left, 1, true)]
    public void OnlyASingleLeftPressOnARowPicksUp(MouseButtons button, int clicks, bool belowRows) =>
        RunSta(() =>
        {
            using var list = new PressableDrawingListBox { Size = new Size(260, 400) };
            var drawing = new Drawing("only", Square());
            list.Items.Add(drawing);
            _ = list.Handle;
            var picked = new List<Drawing>();
            list.DrawingPickedUp += (_, d) => picked.Add(d);

            var point = belowRows
                ? new Point(20, list.GetItemRectangle(0).Bottom + 20)
                : RowCenter(list, 0);
            list.Press(new MouseEventArgs(button, clicks, point.X, point.Y, 0));

            Assert.Empty(picked);

            list.Press(new MouseEventArgs(MouseButtons.Left, 1, RowCenter(list, 0).X, RowCenter(list, 0).Y, 0));
            Assert.Same(drawing, Assert.Single(picked));
        });

    private static (EditNestForm Form, DrawingListBox List, Drawing Second) CreateEditor()
    {
        var nest = new Nest("pickup");
        nest.Drawings.Add(new Drawing("a", Square()));
        var second = new Drawing("b", Square());
        nest.Drawings.Add(second);
        var form = new EditNestForm(nest) { Width = 1100, Height = 700 };
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();
        Application.DoEvents();

        var list = (DrawingListBox)typeof(EditNestForm)
            .GetField("drawingListBox1", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        Assert.Equal(2, list.Items.Count);
        Assert.IsType<ActionSelect>(form.PlateView.Actions.CurrentAction);
        return (form, list, second);
    }

    private static void EnterPlateView(EditNestForm form) =>
        typeof(EditNestForm)
            .GetMethod("PlateView_Enter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [form.PlateView, EventArgs.Empty]);

    private static Drawing ClonedDrawing(ActionClone clone)
    {
        var parts = (List<Part>)typeof(ActionClone)
            .GetField("sourceParts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(clone)!;
        return Assert.Single(parts).BaseDrawing;
    }

    private static Point RowCenter(ListBox list, int index)
    {
        var row = list.GetItemRectangle(index);
        return new Point(row.Left + row.Width / 2, row.Top + row.Height / 2);
    }

    private static IntPtr MakeLParam(Point point) => (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));

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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    private sealed class PressableDrawingListBox : DrawingListBox
    {
        public void Press(MouseEventArgs e) => OnMouseDown(e);
    }
}

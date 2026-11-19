using System.Reflection;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Properties;

namespace OpenNest.WinForms.Tests.Controls;

[Collection("Fill operation lifetime")]
public class DrawingSortButtonTests
{
    [Fact]
    public void ChoosingASortReordersTheListAndIsSavedForTheNextWindow() => RunSta(() =>
    {
        var saved = Settings.Default.DrawingSortOrder;
        try
        {
            Settings.Default.DrawingSortOrder = "Name";
            var nest = NestOf(("a-small", 5, 0), ("b-large", 20, 0), ("c-medium", 10, 0));

            using var form = Open(nest);
            var list = Field<DrawingListBox>(form, "drawingListBox1");
            var sort = Field<ToolStripDropDownButton>(form, "sortDrawingsButton");
            Assert.Equal(new[] { "a-small", "b-large", "c-medium" }, Names(list));

            Choose(sort, "Area (largest first)");

            Assert.Equal(new[] { "b-large", "c-medium", "a-small" }, Names(list));
            Assert.Equal("Area", Settings.Default.DrawingSortOrder);
            Assert.True(Item(sort, "Area (largest first)").Checked);
            Assert.False(Item(sort, "Name").Checked);
            Assert.Contains("Area", sort.ToolTipText);

            using var next = Open(NestOf(("x", 5, 0), ("y", 20, 0)));
            Assert.Equal(new[] { "y", "x" }, Names(Field<DrawingListBox>(next, "drawingListBox1")));
        }
        finally
        {
            Settings.Default.DrawingSortOrder = saved;
        }
    });

    [Fact]
    public void PlacingPartsResortsAListSortedByRemainingQuantity() => RunSta(() =>
    {
        var saved = Settings.Default.DrawingSortOrder;
        try
        {
            Settings.Default.DrawingSortOrder = "Remaining";
            var nest = NestOf(("a", 5, 3), ("b", 5, 2));

            using var form = Open(nest);
            var list = Field<DrawingListBox>(form, "drawingListBox1");
            Assert.Equal(new[] { "a", "b" }, Names(list));

            var a = nest.Drawings.First(d => d.Name == "a");
            nest.Plates[0].Parts.Add(new Part(a));
            nest.Plates[0].Parts.Add(new Part(a));
            typeof(EditNestForm)
                .GetMethod("RefreshDrawingListOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);

            Assert.Equal(new[] { "b", "a" }, Names(list));
        }
        finally
        {
            Settings.Default.DrawingSortOrder = saved;
        }
    });

    private static EditNestForm Open(Nest nest)
    {
        var form = new EditNestForm(nest) { Width = 1100, Height = 700 };
        form.PlateView.SetOverlapAutoCheck(null);
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static Nest NestOf(params (string Name, double Side, int Required)[] drawings)
    {
        var nest = new Nest("sort");
        nest.Plates.Add(new Plate(100, 100) { Quantity = 1 });
        foreach (var (name, side, required) in drawings)
        {
            var drawing = new Drawing(name, Square(side));
            drawing.Quantity.Required = required;
            nest.Drawings.Add(drawing);
        }
        return nest;
    }

    private static void Choose(ToolStripDropDownButton sort, string text) => Item(sort, text).PerformClick();

    private static ToolStripMenuItem Item(ToolStripDropDownButton sort, string text) =>
        sort.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == text);

    private static string[] Names(DrawingListBox list) => list.Items.Cast<Drawing>().Select(d => d.Name).ToArray();

    private static T Field<T>(EditNestForm form, string name) =>
        (T)typeof(EditNestForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    private static Program Square(double side)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, side);
        program.LineTo(side, side);
        program.LineTo(side, 0);
        program.LineTo(0, 0);
        return program;
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromMinutes(3), "The STA test did not complete.");
}

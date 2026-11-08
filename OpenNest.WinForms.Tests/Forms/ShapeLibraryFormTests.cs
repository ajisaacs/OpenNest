using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

public class ShapeLibraryFormTests
{
    [Fact]
    public void FriendlyNameRegexHasExplicitTimeout()
    {
        var regex = Assert.IsType<Regex>(typeof(ShapeLibraryForm)
            .GetField("FriendlyNamePattern", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        Assert.Equal(TimeSpan.FromMilliseconds(100), regex.MatchTimeout);
        Assert.Equal(RegexOptions.None, regex.Options);
    }

    [Theory]
    [InlineData("PipeFlangeShape", "Pipe Flange")]
    [InlineData("HolePatternDiameter", "Hole Pattern Diameter")]
    [InlineData("NgonShape", "Ngon")]
    [InlineData("ABCShape", "ABC")]
    public void FriendlyNamesKeepSuffixAndWordBoundaryBehavior(string input, string expected)
    {
        Assert.Equal(expected, ShapeLibraryForm.FriendlyName(input));
    }

    [Theory]
    [InlineData("PipeFlangeShape", "PipeFlange")]
    [InlineData("HolePatternDiameter", "HolePatternDiameter")]
    public void FriendlyNameTimeoutDiscardsPartialFormatting(string input, string expected)
    {
        var notices = new List<string>();
        var result = ShapeLibraryForm.FriendlyName(input, name =>
        {
            Assert.Equal(expected, name);
            // Simulate a replacement failing after it has transformed a prefix internally.
            var partial = name.Insert(4, " ");
            Assert.NotEqual(expected, partial);
            throw new RegexMatchTimeoutException();
        }, notices.Add);
        Assert.Equal(expected, result);
        Assert.Contains(expected, Assert.Single(notices));
    }

    [Fact]
    public void ParametersAreDimensionsOnTheDrawingNotASidePanel() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");

        Assert.Empty(form.Controls.Find("parametersPanel", true));
        Assert.Equal(new[] { "Length", "Width" }, preview.LabelKeys);
        Assert.NotNull(preview.LabelBounds("Length"));
    });

    [Fact]
    public void TypingADimensionResizesTheShape() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");

        var box = OpenEditor(form, "Length");
        Assert.Equal("Length", preview.ActiveKey);
        box.Text = "20 1/2";
        Press(box, Keys.Enter);

        Assert.Null(form.ActiveEditor);
        Assert.Equal(20.5, preview.Plate.Parts.Single().BoundingBox.Length, 6);
        InvokeAdd(form);
        Assert.Equal(20.5, form.GetDrawings().Single().Program.BoundingBox().Length, 6);
    });

    [Fact]
    public void InvalidEntryStaysOpenInRedAndKeepsTheLastShape() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var length = preview.Plate.Parts.Single().BoundingBox.Length;

        var box = OpenEditor(form, "Length");
        box.Text = "invalid dimension";
        Press(box, Keys.Enter);
        Assert.Same(box, form.ActiveEditor);
        Assert.Equal(Color.Red, box.ForeColor);

        box.Text = "0";
        Press(box, Keys.Enter);
        Assert.Same(box, form.ActiveEditor);

        Press(box, Keys.Escape);
        Assert.Null(form.ActiveEditor);
        Assert.Equal(length, preview.Plate.Parts.Single().BoundingBox.Length, 6);
        Assert.True(Find<Button>(form, "addButton").Enabled);
    });

    [Fact]
    public void TabCommitsAndMovesToTheNextDimension() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");

        var box = OpenEditor(form, "Length");
        box.Text = "15";
        Press(box, Keys.Tab);

        Assert.Equal("Width", preview.ActiveKey);
        Assert.Equal(15, preview.Plate.Parts.Single().BoundingBox.Length, 6);
    });

    [Fact]
    public void FlangeHoleCountSitsBesideTheHoleDiameter() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(PipeFlangeShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");

        var diameter = preview.LabelBounds("HoleDiameter")!.Value;
        var count = preview.LabelBounds("HoleCount")!.Value;
        Assert.Equal(diameter.Right, count.Left);
        Assert.Equal(diameter.Top, count.Top);

        int ArcsWith(int holes)
        {
            var box = OpenEditor(form, "HoleCount");
            box.Text = holes.ToString();
            Press(box, Keys.Enter);
            Assert.Null(form.ActiveEditor);
            return Arcs(preview);
        }
        var ten = ArcsWith(10);
        var perHole = ArcsWith(11) - ten;
        Assert.True(perHole > 0);
        Assert.Equal(ten + 2 * perHole, ArcsWith(12));
    });

    [Fact]
    public void BlindTogglesOnClickAndDropsTheBore() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(PipeFlangeShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        Assert.Contains("Blind", preview.LabelKeys);
        var arcs = Arcs(preview);

        form.EditParameter("Blind");

        Assert.Null(form.ActiveEditor);
        Assert.True(Arcs(preview) < arcs);
    });

    [Fact]
    public void DraggingADimensionLabelMovesItAndAClickStillEdits() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var before = preview.LabelBounds("Length")!.Value;
        var grab = new Point(before.X + before.Width / 2, before.Y + before.Height / 2);

        Mouse(preview, "OnMouseDown", MouseButtons.Left, grab);
        Mouse(preview, "OnMouseMove", MouseButtons.Left, new Point(grab.X + 30, grab.Y + 40));
        Mouse(preview, "OnMouseUp", MouseButtons.Left, new Point(grab.X + 30, grab.Y + 40));

        var after = preview.LabelBounds("Length")!.Value;
        Assert.Null(form.ActiveEditor);
        Assert.InRange(after.Y, before.Y + 39, before.Y + 41);
        Assert.InRange(after.X, before.X + 29, before.X + 31);

        var moved = new Point(after.X + after.Width / 2, after.Y + after.Height / 2);
        Mouse(preview, "OnMouseDown", MouseButtons.Left, moved);
        Mouse(preview, "OnMouseUp", MouseButtons.Left, moved);
        Assert.Equal("Length", preview.ActiveKey);
        Assert.Equal(after.Location, form.ActiveEditor!.Location);
    });

    [Fact]
    public void ALinearDimensionCannotBeDraggedOntoTheDrawing() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var before = preview.LabelBounds("Length")!.Value;

        preview.SetDragOffset("Length", new PointF(0, -500));

        // The length sits below the drawing: it may come closer, never above its bottom edge.
        var after = preview.LabelBounds("Length")!.Value;
        Assert.True(after.Y < before.Y);
        Assert.True(after.Y > before.Y - 20);
    });

    [Fact]
    public void ASlidLabelKeepsItsPlaceBetweenTheArrowsAsTheViewZooms() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var box = preview.Plate.Parts.Single().BoundingBox;

        // Where the label's center sits along the drawing's width: 0 at the left arrow, 1 at the right.
        double Place()
        {
            var bounds = preview.LabelBounds("Length")!.Value;
            var left = preview.PointWorldToControl(box.X, box.Y).X;
            var right = preview.PointWorldToControl(box.X + box.Length, box.Y).X;
            return (bounds.X + bounds.Width / 2.0 - left) / (right - left);
        }

        var width = preview.PointWorldToControl(box.X + box.Length, box.Y).X - preview.PointWorldToControl(box.X, box.Y).X;
        preview.SetDragOffset("Length", new PointF(width * 0.3f, 0));
        var place = Place();
        Assert.InRange(place, 0.75, 0.85);

        for (var i = 0; i < 4; i++)
            Mouse(preview, "OnMouseWheel", MouseButtons.None, new Point(40, 40), -120);
        Assert.InRange(Place(), place - 0.02, place + 0.02);

        // Dragging again starts from where the label is now shown.
        var bounds = preview.LabelBounds("Length")!.Value;
        var grab = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Mouse(preview, "OnMouseDown", MouseButtons.Left, grab);
        Mouse(preview, "OnMouseMove", MouseButtons.Left, new Point(grab.X + 10, grab.Y));
        Mouse(preview, "OnMouseUp", MouseButtons.Left, new Point(grab.X + 10, grab.Y));
        Assert.InRange(preview.LabelBounds("Length")!.Value.X, bounds.X + 9, bounds.X + 11);
    });

    [Fact]
    public void WheelZoomIsKeptWhileEditingAndFitResetsIt() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var fitted = preview.ViewScale;

        Mouse(preview, "OnMouseWheel", MouseButtons.None, new Point(100, 100), 120);
        var zoomed = preview.ViewScale;
        Assert.True(zoomed > fitted);
        Assert.True(preview.IsUserView);

        var box = OpenEditor(form, "Length");
        box.Text = "20";
        Press(box, Keys.Enter);
        Assert.Equal(zoomed, preview.ViewScale);
        Assert.True(preview.IsUserView);

        preview.ZoomToFit();
        Assert.False(preview.IsUserView);

        Mouse(preview, "OnMouseWheel", MouseButtons.None, new Point(100, 100), 120);
        SelectShape(form, typeof(CircleShape));
        Assert.False(preview.IsUserView);
    });

    [Theory]
    [InlineData(MouseButtons.Left)]
    [InlineData(MouseButtons.Middle)]
    public void DraggingTheViewPansItAndMovesTheLabels(MouseButtons button) => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var scale = preview.ViewScale;
        var before = preview.LabelBounds("Length")!.Value;
        var start = new Point(preview.Width / 2, preview.Height / 2);
        var end = new Point(start.X + 25, start.Y - 15);

        Mouse(preview, "OnMouseDown", button, start);
        Mouse(preview, "OnMouseMove", button, end);
        Mouse(preview, "OnMouseUp", button, end);

        var after = preview.LabelBounds("Length")!.Value;
        Assert.Null(form.ActiveEditor);
        Assert.True(preview.IsUserView);
        Assert.Equal(scale, preview.ViewScale);
        Assert.InRange(after.X, before.X + 24, before.X + 26);
        Assert.InRange(after.Y, before.Y - 16, before.Y - 14);

        // A changed parameter keeps the panned view.
        var box = OpenEditor(form, "Width");
        box.Text = "7";
        Press(box, Keys.Enter);
        Assert.True(preview.IsUserView);
    });

    [Fact]
    public void ShapeGridShowsAThumbnailForEveryShape() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        var grid = Find<ListView>(form, "shapeListView");
        var shapeCount = typeof(ShapeDefinition).Assembly.GetTypes()
            .Count(t => t.IsClass && !t.IsAbstract && typeof(ShapeDefinition).IsAssignableFrom(t));

        Assert.Equal(shapeCount, grid.Items.Count);
        Assert.Equal(shapeCount, grid.LargeImageList!.Images.Count);
        foreach (ListViewItem item in grid.Items)
        {
            using var thumbnail = (Bitmap)grid.LargeImageList.Images[item.ImageIndex];
            Assert.True(HasInk(thumbnail), $"'{item.Text}' has a blank thumbnail");
        }
    });

    [Fact]
    public void SelectingATileShowsThatShape() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        var name = Find<TextBox>(form, "nameTextBox");

        SelectShape(form, typeof(CircleShape));
        var circleName = name.Text;
        SelectShape(form, typeof(RectangleShape));

        Assert.NotEqual(circleName, name.Text);
        Assert.NotEmpty(Find<ShapePreviewControl>(form, "previewBox").Plate.Parts);
    });

    private static void SelectShape(ShapeLibraryForm form, Type shapeType)
    {
        var grid = Find<ListView>(form, "shapeListView");
        // Selection events come from the native list, so it needs a window.
        _ = grid.Handle;
        grid.Items.Cast<ListViewItem>().Single(item =>
            (Type)item.Tag!.GetType().GetProperty("ShapeType")!.GetValue(item.Tag)! == shapeType).Selected = true;
    }

    private static bool HasInk(Bitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x += 4)
            for (var y = 0; y < bitmap.Height; y += 4)
                if (bitmap.GetPixel(x, y).A > 0)
                    return true;
        return false;
    }

    private static T Find<T>(Control form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static int Arcs(ShapePreviewControl preview) =>
        preview.Plate.Parts.Single().Program.Codes.OfType<OpenNest.CNC.ArcMove>().Count();

    private static TextBox OpenEditor(ShapeLibraryForm form, string key)
    {
        form.EditParameter(key);
        return Assert.IsAssignableFrom<TextBox>(form.ActiveEditor);
    }

    private static void Mouse(Control control, string method, MouseButtons button, Point location, int delta = 0) =>
        control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, new object[] { new MouseEventArgs(button, 1, location.X, location.Y, delta) });

    private static void Press(Control control, Keys key) => typeof(Control)
        .GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(control, new object[] { new KeyEventArgs(key) });

    private static void InvokeAdd(ShapeLibraryForm form) => typeof(ShapeLibraryForm)
        .GetMethod("AddButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(form, new object?[] { null, EventArgs.Empty });
}

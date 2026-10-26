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
    public void InvalidShapeInputClearsOldPreviewAndBlocksAddUntilCorrected() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new ShapeLibraryForm();
        SelectShape(form, typeof(RectangleShape));
        var preview = Find<ShapePreviewControl>(form, "previewBox");
        var add = Find<Button>(form, "addButton");
        var input = Find<Panel>(form, "parametersPanel").Controls.OfType<TextBox>().First();
        var valid = input.Text;
        Assert.NotEmpty(preview.Plate.Parts);
        Assert.True(add.Enabled);

        input.Text = "invalid dimension";
        Assert.Equal(Color.Red, input.ForeColor);
        Assert.Empty(preview.Plate.Parts);
        Assert.False(add.Enabled);
        InvokeAdd(form);
        Assert.Empty(form.GetDrawings());

        input.Text = valid;
        Assert.Equal(SystemColors.WindowText, input.ForeColor);
        Assert.NotEmpty(preview.Plate.Parts);
        Assert.True(add.Enabled);
        InvokeAdd(form);
        Assert.Single(form.GetDrawings());
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

    private static void InvokeAdd(ShapeLibraryForm form) => typeof(ShapeLibraryForm)
        .GetMethod("AddButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(form, new object?[] { null, EventArgs.Empty });
}

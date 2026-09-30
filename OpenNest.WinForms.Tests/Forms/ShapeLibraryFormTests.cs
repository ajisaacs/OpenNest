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
        var list = Find<ListBox>(form, "shapeListBox");
        list.SelectedItem = list.Items.Cast<object>().Single(entry =>
            (Type)entry.GetType().GetProperty("ShapeType")!.GetValue(entry)! == typeof(RectangleShape));
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

    private static T Find<T>(Control form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static void InvokeAdd(ShapeLibraryForm form) => typeof(ShapeLibraryForm)
        .GetMethod("AddButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(form, new object?[] { null, EventArgs.Empty });
}

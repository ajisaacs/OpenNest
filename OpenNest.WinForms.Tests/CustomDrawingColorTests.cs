using System.Drawing;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests;

[Collection("Fill operation lifetime")]
public class CustomDrawingColorTests
{
    [Fact]
    public void DrawingPropertiesMarksOnlyAChangedColorAsCustom() => StaTestThread.Run(() =>
    {
        var drawing = new Drawing("Bracket") { Color = Color.Coral };
        using (var unchanged = new EditDrawingForm())
        {
            unchanged.LoadDrawing(drawing);
            unchanged.SaveDrawing(drawing);
        }
        Assert.False(drawing.HasCustomColor);

        using var picked = new EditDrawingForm();
        picked.LoadDrawing(drawing);
        var dialog = (System.Windows.Forms.ColorDialog)typeof(EditDrawingForm)
            .GetField("colorDialog1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(picked)!;
        dialog.Color = Color.Red;
        picked.SaveDrawing(drawing);

        Assert.True(drawing.HasCustomColor);
        Assert.Equal(Color.Red.ToArgb(), drawing.Color.ToArgb());
    }, TimeSpan.FromMinutes(1), "Drawing properties color test timed out.");

    [Fact]
    public void DrawingPropertiesAutomaticColorClearsTheCustomColor() => StaTestThread.Run(() =>
    {
        var previous = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorScheme.Default));
        try
        {
            ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Classic"));
            var nest = new Nest("Automatic");
            nest.Drawings.Add(new Drawing("First"));
            nest.Drawings.Add(new Drawing("Cutoff") { IsCutOff = true });
            var drawing = new Drawing("Bracket") { Color = Color.Red, HasCustomColor = true };
            nest.Drawings.Add(drawing);

            using var form = new EditDrawingForm();
            form.LoadDrawing(drawing, nest);
            form.Show();
            var modeLabel = Assert.IsType<System.Windows.Forms.Label>(form.Controls.Find("colorModeLabel", true).Single());
            var automatic = Assert.IsType<System.Windows.Forms.Button>(form.Controls.Find("automaticColorButton", true).Single());
            Assert.Equal("Custom color", modeLabel.Text);
            Assert.True(automatic.Enabled);

            automatic.PerformClick();

            Assert.StartsWith("Automatic", modeLabel.Text);
            Assert.False(automatic.Enabled);
            Assert.True(drawing.HasCustomColor);

            form.SaveDrawing(drawing);

            Assert.False(drawing.HasCustomColor);
            Assert.Equal(Drawing.GetPartColor(1).ToArgb(), drawing.Color.ToArgb());

            using var reopened = new EditDrawingForm();
            reopened.LoadDrawing(drawing, nest);
            Assert.StartsWith("Automatic", reopened.Controls.Find("colorModeLabel", true).Single().Text);
        }
        finally
        {
            ColorSchemeRegistry.Apply(previous);
        }
    }, TimeSpan.FromMinutes(1), "Drawing properties automatic color test timed out.");
}

using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>Edit Plate with a sheet outline: the size follows the outline and is read-only
/// while there is one, Done applies the outline, and Clear makes the sheet a rectangle again.</summary>
public class EditPlateFormOutlineTests
{
    private static SheetOutline Ell() => new(new[]
    {
        new Vector(0, 0), new Vector(96, 0), new Vector(96, 24), new Vector(48, 24), new Vector(48, 48), new Vector(0, 48),
    });

    [Fact]
    public void OutlineSetsTheSizeAndDoneAppliesIt() => RunSta(() =>
    {
        var plate = new Plate(60, 120);
        using var form = new EditPlateForm(plate);
        var size = (TextBox)form.Controls.Find("textBoxSize", true)[0];
        var clear = (Button)form.Controls.Find("buttonClearOutline", true)[0];
        var done = (Button)form.Controls.Find("applyButton", true)[0];
        Assert.False(size.ReadOnly);
        Assert.False(clear.Enabled);

        form.Outline = Ell();

        Assert.True(size.ReadOnly);
        Assert.True(clear.Enabled);
        Assert.Equal(new Size(48, 96).ToString(), form.SizeString);
        Assert.Contains("6 vertices", ((Label)form.Controls.Find("labelOutline", true)[0]).Text);
        Assert.True(done.Enabled);

        done.PerformClick();
        Assert.Equal(Ell(), plate.Outline);
        Assert.Equal(48, plate.Size.Width);
        Assert.Equal(96, plate.Size.Length);
    });

    [Fact]
    public void ClearMakesTheSheetARectangleOfTheSameSize() => RunSta(() =>
    {
        var plate = new Plate { Outline = Ell(), Quadrant = 2 };
        using var form = new EditPlateForm(plate);
        var size = (TextBox)form.Controls.Find("textBoxSize", true)[0];
        Assert.True(size.ReadOnly);

        ((Button)form.Controls.Find("buttonClearOutline", true)[0]).PerformClick();
        Assert.False(size.ReadOnly);
        ((Button)form.Controls.Find("applyButton", true)[0]).PerformClick();

        Assert.Null(plate.Outline);
        Assert.Equal(48, plate.Size.Width);
        Assert.Equal(96, plate.Size.Length);
        Assert.Equal(2, plate.Quadrant);
    });

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "Edit Plate outline test timed out.");
}

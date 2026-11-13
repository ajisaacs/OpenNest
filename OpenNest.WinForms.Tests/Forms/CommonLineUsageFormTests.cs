using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class CommonLineUsageFormTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawingProperties_LoadsAndSavesAllowCommonLine(bool allow) => RunSta(() =>
    {
        var drawing = new Drawing("pair", new Program()) { AllowCommonLine = allow };
        using var form = new EditDrawingForm();

        form.LoadDrawing(drawing);
        var box = Assert.IsType<CheckBox>(form.Controls.Find("allowCommonLineBox", true).Single());
        Assert.Equal(allow, box.Checked);

        box.Checked = !allow;
        form.SaveDrawing(drawing);
        Assert.Equal(!allow, drawing.AllowCommonLine);
    });

    [Theory]
    [InlineData(CommonLineUsage.Off, 0)]
    [InlineData(CommonLineUsage.WhenItAddsParts, 1)]
    [InlineData(CommonLineUsage.Whenever, 2)]
    public void AutoNest_StartsAtTheNestsUsage_AndReadsTheChoice(CommonLineUsage usage, int index) =>
        RunSta(() =>
        {
            var nest = new Nest { CommonLineUsage = usage };
            using var form = new AutoNestForm(nest);
            var combo = Assert.IsType<ComboBox>(form.Controls.Find("commonLineComboBox", true).Single());

            Assert.Equal(index, combo.SelectedIndex);
            Assert.Equal(usage, form.CommonLineUsage);

            combo.SelectedIndex = (index + 1) % 3;
            Assert.Equal((CommonLineUsage)((index + 1) % 3), form.CommonLineUsage);
        });

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}

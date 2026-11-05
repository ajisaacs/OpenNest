using System.Windows.Forms;
using OpenNest.Forms;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

public class SetValueFormTests
{
    [Fact]
    public void OptionShowsACheckBoxThatDisablesTheValue() => StaTestThread.Run(() =>
    {
        using var form = new SetValueForm { ShowInTaskbar = false };
        var bare = form.ClientSize;
        var option = Assert.Single(All<CheckBox>(form));
        var value = Assert.Single(All<OpenNest.Controls.NumericUpDown>(form));

        form.OptionText = "Calculated distance (selection size + part spacing)";
        form.OptionText = "Calculated distance (selection size + part spacing)"; // Grows once.
        form.OptionChecked = true;
        form.Show();

        Assert.True(option.Visible);
        Assert.True(form.ClientSize.Height > bare.Height);
        Assert.True(option.Bottom <= form.ClientSize.Height);
        Assert.True(option.Right <= form.ClientSize.Width);
        Assert.False(value.Enabled);

        form.OptionChecked = false;
        Assert.True(value.Enabled);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void WithoutAnOptionTheDialogKeepsItsSize() => StaTestThread.Run(() =>
    {
        using var form = new SetValueForm();
        var bare = form.ClientSize;

        form.OptionText = "shown";
        form.OptionText = "";

        Assert.Equal(bare, form.ClientSize);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}

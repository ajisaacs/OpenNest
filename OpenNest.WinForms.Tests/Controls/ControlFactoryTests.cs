using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class ControlFactoryTests
{
    [Fact]
    public void TabHoldsOnlyItsContentWithoutPadding() => RunSta(() =>
    {
        using var content = new Panel();
        using var page = ControlFactory.Tab("Plates", content);

        Assert.Equal("Plates", page.Text);
        Assert.Equal(new Padding(0), page.Padding);
        Assert.Same(content, Assert.Single(page.Controls.Cast<Control>()));
    });

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void SettingsGridKeepsTheInputColumnAtTheLabelWidth(int indent) => RunSta(() =>
    {
        using var grid = ControlFactory.SettingsGrid(indent);

        Assert.True(grid.AutoSize);
        Assert.Equal(AutoSizeMode.GrowAndShrink, grid.AutoSizeMode);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(new Padding(indent, 0, 0, 0), grid.Margin);
        Assert.Equal(3, grid.ColumnStyles.Count);
        Assert.Equal(SizeType.Absolute, grid.ColumnStyles[0].SizeType);
        Assert.Equal(150 - indent, grid.ColumnStyles[0].Width);
        Assert.Equal(SizeType.AutoSize, grid.ColumnStyles[1].SizeType);
        Assert.Equal(SizeType.AutoSize, grid.ColumnStyles[2].SizeType);
    });

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "The STA test did not complete.");
}

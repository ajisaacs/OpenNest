using System.Drawing;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class MainWindowSettingsTests
{
    private static readonly Size Minimum = new(588, 381);
    private static readonly Size ScreenSize = new(1920, 1080);

    [Theory]
    [InlineData(0, 0)]       // The default: no size was ever saved.
    [InlineData(588, 381)]   // The minimum the default shrank to, saved on a later close.
    [InlineData(500, 300)]
    [InlineData(1921, 800)]  // Wider than the screen.
    [InlineData(1200, 1081)] // Taller than the screen.
    public void SizesNotWorthRestoringAreIgnored(int width, int height) =>
        Assert.False(
            MainWindowSettings.IsRestorableSize(new Size(width, height), Minimum, ScreenSize));

    [Theory]
    [InlineData(1281, 654)]
    [InlineData(589, 381)]
    [InlineData(588, 382)]
    [InlineData(1920, 1080)]
    public void SizesAboveTheMinimumThatFitAreRestored(int width, int height) =>
        Assert.True(
            MainWindowSettings.IsRestorableSize(new Size(width, height), Minimum, ScreenSize));

    [Fact]
    public void ANormalWindowSavesItsBounds()
    {
        var bounds = new Rectangle(100, 50, 1200, 800);

        var saved = MainWindowSettings.SavedPlacement(
            FormWindowState.Normal, bounds, Rectangle.Empty);

        Assert.Equal((bounds, FormWindowState.Normal), saved);
    }

    [Fact]
    public void AMaximizedWindowSavesTheBoundsItRestoresTo()
    {
        var restore = new Rectangle(100, 50, 1200, 800);

        var saved = MainWindowSettings.SavedPlacement(
            FormWindowState.Maximized, new Rectangle(-8, -8, 1936, 1056), restore);

        Assert.Equal((restore, FormWindowState.Maximized), saved);
    }

    [Fact]
    public void AMinimizedWindowSavesItsRestoreBoundsAsNormal()
    {
        var restore = new Rectangle(100, 50, 1200, 800);

        var saved = MainWindowSettings.SavedPlacement(
            FormWindowState.Minimized, new Rectangle(-32000, -32000, 160, 28), restore);

        Assert.Equal((restore, FormWindowState.Normal), saved);
    }
}

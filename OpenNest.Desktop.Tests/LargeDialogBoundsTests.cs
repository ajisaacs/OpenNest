namespace OpenNest.Desktop.Tests;

public class LargeDialogBoundsTests
{
    [Fact]
    public void Centered_NineTenthsOfTheWorkingArea()
    {
        var bounds = LargeDialogBounds.Centered(new PixelRect(0, 0, 1920, 1040), 800, 600);

        Assert.Equal(new PixelRect(96, 52, 1728, 936), bounds);
    }

    [Fact]
    public void Centered_OnASecondaryScreen()
    {
        var bounds = LargeDialogBounds.Centered(new PixelRect(1920, -200, 2560, 1400), 800, 600);

        Assert.Equal(new PixelRect(1920 + 128, -200 + 70, 2304, 1260), bounds);
    }

    [Fact]
    public void Centered_NeverBelowTheMinimumOrBeforeTheWorkingArea()
    {
        var bounds = LargeDialogBounds.Centered(new PixelRect(100, 50, 1000, 700), 1200, 900);

        Assert.Equal(new PixelRect(100, 50, 1200, 900), bounds);
    }
}

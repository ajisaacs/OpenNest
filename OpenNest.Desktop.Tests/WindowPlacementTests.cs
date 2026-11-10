namespace OpenNest.Desktop.Tests;

public class WindowPlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1400);

    [Fact]
    public void Read_TheWinFormsAppsMaximizedWindow()
    {
        // As DesktopPC's real user-settings.json holds it.
        var settings = new Dictionary<string, string>
        {
            ["MainWindowLocation"] = "-8, -8",
            ["MainWindowSize"] = "1936, 1048",
            ["MainWindowState"] = "Maximized",
        };

        var placement = WindowPlacement.Read(settings, WindowPlacement.MainWindow);

        Assert.Equal(new WindowPlacement((-8, -8), (1936, 1048), true), placement);
    }

    [Fact]
    public void Read_NothingSaved_IsNull()
    {
        var empty = new Dictionary<string, string>();

        Assert.Null(WindowPlacement.Read(empty, WindowPlacement.MainWindow));
    }

    [Theory]
    [InlineData("Minimized")]
    [InlineData("Normal")]
    [InlineData("garbage")]
    public void Read_OnlyMaximizedRestoresMaximized(string state)
    {
        var settings = new Dictionary<string, string>
        {
            ["MainWindowSize"] = "800, 600",
            ["MainWindowState"] = state,
        };

        Assert.False(WindowPlacement.Read(settings, WindowPlacement.MainWindow)!.Maximized);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("800")]
    [InlineData("800, 600, 4")]
    [InlineData("0, 600")]
    [InlineData("800, -1")]
    public void Read_UnusableSizeIsDropped(string size)
    {
        var settings = new Dictionary<string, string>
        {
            ["MainWindowLocation"] = "10, 20",
            ["MainWindowSize"] = size,
        };

        var placement = WindowPlacement.Read(settings, WindowPlacement.MainWindow);

        Assert.Equal(new WindowPlacement((10, 20), null, false), placement);
    }

    [Fact]
    public void WriteTo_UsesTheWinFormsNamesAndFormat()
    {
        var settings = new Dictionary<string, string> { ["Other"] = "kept" };

        var placement = new WindowPlacement((100, 50), (1200, 800), false);

        placement.WriteTo(settings, WindowPlacement.MainWindow);

        Assert.Equal(new Dictionary<string, string>
        {
            ["Other"] = "kept",
            ["MainWindowLocation"] = "100, 50",
            ["MainWindowSize"] = "1200, 800",
            ["MainWindowState"] = "Normal",
        }, settings);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var settings = new Dictionary<string, string>();
        var placement = new WindowPlacement((-1500, 30), (1400, 860), true);

        placement.WriteTo(settings, "DrawingSetsWindow");

        Assert.Equal(placement, WindowPlacement.Read(settings, "DrawingSetsWindow"));
    }

    [Fact]
    public void FitTo_KeepsAPlacementOnASecondScreen()
    {
        var placement = new WindowPlacement((2000, 100), (2400, 1300), false);

        Assert.Equal(placement, placement.FitTo([Primary, Right]));
    }

    [Fact]
    public void FitTo_DropsAPositionOffEveryScreen()
    {
        // The second monitor was unplugged since the window closed there.
        var placement = new WindowPlacement((2000, 100), (1200, 800), false);

        Assert.Equal(new WindowPlacement(null, (1200, 800), false), placement.FitTo([Primary]));
    }

    [Fact]
    public void FitTo_DropsASizeTooBigForItsScreen()
    {
        var placement = new WindowPlacement((100, 100), (2400, 1300), false);

        var fitted = placement.FitTo([Primary, Right]);

        Assert.Equal(new WindowPlacement((100, 100), null, false), fitted);
    }

    [Fact]
    public void FitTo_KeepsMaximized()
    {
        var placement = new WindowPlacement((-8, -8), (1936, 1048), true);

        Assert.Equal(new WindowPlacement(null, null, true), placement.FitTo([Primary]));
    }
}

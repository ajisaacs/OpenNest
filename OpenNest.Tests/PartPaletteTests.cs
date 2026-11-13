using System.Drawing;

namespace OpenNest.Tests;

public class PartPaletteTests
{
    [Fact]
    public void ColorAt_CyclesTheGivenColors()
    {
        var palette = new PartPalette(new[] { Color.Red, Color.Blue });

        Assert.Equal(Color.Red, palette.ColorAt(0));
        Assert.Equal(Color.Blue, palette.ColorAt(1));
        Assert.Equal(Color.Red, palette.ColorAt(2));
    }

    [Fact]
    public void ColorAt_WithNoColors_UsesTheClassicPalette()
    {
        var palette = new PartPalette(System.Array.Empty<Color>());

        for (var i = 0; i < 14; i++)
            Assert.Equal(PartPalette.Classic.ColorAt(i), palette.ColorAt(i));
    }
}

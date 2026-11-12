using System.Drawing;

namespace OpenNest.Desktop.Tests;

public class ColorShadeTests
{
    // Expected values from an independent transcription of WinForms' ControlPaint.HLSColor;
    // OpenNest.WinForms.Tests compares against ControlPaint.Dark itself on Windows.
    [Theory]
    [InlineData(255, 127, 80, 113, 31, 0)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(255, 255, 255, 85, 85, 85)]
    [InlineData(205, 92, 92, 77, 23, 23)]
    [InlineData(128, 128, 128, 42, 42, 42)]
    [InlineData(90, 150, 200, 23, 50, 72)]
    [InlineData(255, 0, 0, 85, 0, 0)]
    [InlineData(12, 34, 250, 2, 11, 85)]
    public void DarkMatchesTheWinFormsShade(int r, int g, int b, int er, int eg, int eb)
    {
        Assert.Equal(Color.FromArgb(er, eg, eb), ColorShade.Dark(Color.FromArgb(r, g, b)));
    }

    [Fact]
    public void DarkIgnoresAlphaAndReturnsAnOpaqueColor()
    {
        // The WinForms selection fill: alpha 90 over (150, 200, 255).
        Assert.Equal(Color.FromArgb(0, 65, 136), ColorShade.Dark(Color.FromArgb(90, 150, 200, 255)));
    }

    [Fact]
    public void SchemesWithoutAPartOutlineDarkenTheFill()
    {
        var classic = ColorSchemeRegistry.Get("Classic");
        var workshop = ColorSchemeRegistry.Get("Workshop");

        Assert.Equal(Color.FromArgb(113, 31, 0), classic.GetPartOutlineColor(Color.Coral));
        Assert.Equal(Color.FromArgb(48, 48, 48), workshop.GetPartOutlineColor(Color.Coral));
    }
}

using System.Drawing;

namespace OpenNest.WinForms.Tests;

public class ClassicPaletteTests
{
    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (System.Math.Max(Luminance(a), Luminance(b)), System.Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    // CIE76 distance in Lab, D65.
    private static (double L, double A, double B) Lab(Color c)
    {
        var r = Linear(c.R);
        var g = Linear(c.G);
        var b = Linear(c.B);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? System.Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static double DeltaE(Color a, Color b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return System.Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    [Fact]
    public void ClassicFillsAreClearlyDistinctFromEachOther()
    {
        var colors = ColorSchemeRegistry.Get("Classic").PartColors;
        Assert.Equal(12, colors.Length);
        for (var i = 0; i < colors.Length; i++)
        {
            for (var j = i + 1; j < colors.Length; j++)
                Assert.True(DeltaE(colors[i], colors[j]) >= 30,
                    $"Classic colors {i} and {j} are too similar ({DeltaE(colors[i], colors[j]):F1}).");
        }
    }

    [Fact]
    public void ClassicFillsStayReadableAndClearOfEtchGreenAndTheSheet()
    {
        var scheme = ColorSchemeRegistry.Get("Classic");
        Assert.All(scheme.PartColors, color =>
        {
            Assert.True(Contrast(color, Color.Black) >= 4.5, $"{color} is too dark for black part IDs.");
            Assert.True(DeltaE(color, scheme.EtchColor) >= 50, $"{color} is too close to the etch color.");
            Assert.True(DeltaE(color, scheme.LayoutFillColor) >= 25, $"{color} blends into the sheet.");
            Assert.True(DeltaE(color, scheme.BackgroundColor) >= 25, $"{color} blends into the canvas.");
        });
    }

    [Theory]
    [InlineData(255, 255, 255, false)]
    [InlineData(206, 153, 85, false)]
    [InlineData(104, 113, 230, false)]
    [InlineData(30, 30, 34, true)]
    [InlineData(60, 60, 140, true)]
    public void LabelIsWhiteOnDarkFillsAndBlackOnLightOnes(int r, int g, int b, bool expectWhite)
    {
        var brush = LayoutPart.LabelBrushFor(Color.FromArgb(r, g, b));
        Assert.Same(expectWhite ? Brushes.White : Brushes.Black, brush);
    }
}

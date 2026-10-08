using System.Drawing;

namespace OpenNest.Tests;

public class PartColorPaletteTests
{
    [Theory]
    [InlineData(0, 216, 49, 49)]
    [InlineData(1, 98, 160, 234)]
    [InlineData(2, 253, 150, 167)]
    [InlineData(3, 157, 211, 226)]
    public void GoldenAngleMatchesWideRangeHslSamples(int index, int r, int g, int b)
        => Assert.Equal(Color.FromArgb(r, g, b), PartColorPalette.GoldenAngle(index));

    [Fact]
    public void GoldenAngleContinuesPastPaletteLengthAndStaysOutOfEtchBand()
    {
        var colors = Enumerable.Range(0, 1000).Select(PartColorPalette.GoldenAngle).ToArray();
        Assert.NotEqual(colors[0], colors[12]);
        Assert.All(colors, color =>
        {
            Assert.Equal(255, color.A);
            // The green band belongs to etch strokes; no generated fill may land in it.
            var hue = color.GetHue();
            Assert.True(hue < 95 || hue >= 165, $"Fill hue {hue:F0} collides with the etch-green band.");
        });
        Assert.Equal(colors, Enumerable.Range(0, 1000).Select(PartColorPalette.GoldenAngle));
        Assert.InRange(PartColorPalette.GoldenAngle(int.MaxValue).GetBrightness(), 0.4f, 0.85f);
    }

    [Fact]
    public void NeighboringPartsStayFarApartInRgb()
    {
        static double Distance(Color a, Color b) =>
            System.Math.Sqrt((a.R - b.R) * (double)(a.R - b.R)
                + (a.G - b.G) * (a.G - b.G)
                + (a.B - b.B) * (a.B - b.B));

        var colors = Enumerable.Range(0, 48).Select(PartColorPalette.GoldenAngle).ToArray();
        Assert.Equal(48, colors.Select(c => c.ToArgb()).Distinct().Count());
        for (var i = 0; i < colors.Length - 1; i++)
            Assert.True(Distance(colors[i], colors[i + 1]) >= 90,
                $"Parts {i} and {i + 1} are too similar: {Distance(colors[i], colors[i + 1]):F0}.");
    }

    [Fact]
    public void GoldenAngleRejectsNegativeIndices()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PartColorPalette.GoldenAngle(-1));
}

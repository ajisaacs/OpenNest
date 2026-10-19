using System.Drawing;

namespace OpenNest.WinForms.Tests;

public class FestiveColorSchemeTests
{
    [Theory]
    [InlineData("Halloween")]
    [InlineData("Christmas")]
    [InlineData("4th of July")]
    public void FestiveSchemeKeepsPartsDistinctAndReadable(string name)
    {
        var scheme = ColorSchemeRegistry.Get(name);

        Assert.Equal(name, scheme.Name);
        Assert.False(scheme.UseGoldenAngleColors);
        Assert.True(scheme.PartColors.Length >= 12);
        Assert.Equal(Color.Lime.ToArgb(), scheme.EtchColor.ToArgb());
        Assert.True(scheme.SelectedPartColor.IsEmpty);

        var colors = scheme.PartColors;
        for (var i = 0; i < colors.Length; i++)
        {
            // Part IDs are drawn in black over the fill.
            Assert.True(Luminance(colors[i]) >= 90, $"{name} part color {i} is too dark for black part IDs.");
            Assert.True(Distance(colors[i], scheme.EtchColor) >= 120, $"{name} part color {i} is too close to the etch color.");
            for (var j = i + 1; j < colors.Length; j++)
                Assert.True(Distance(colors[i], colors[j]) >= 40, $"{name} part colors {i} and {j} are too similar.");
        }
    }

    [Fact]
    public void FestiveSchemesRoundTripAndLeaveBlueprintAsDefault()
    {
        foreach (var name in new[] { "Halloween", "Christmas", "4th of July" })
        {
            var scheme = ColorSchemeRegistry.Get(name);
            var restored = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(scheme));
            Assert.Equal(name, restored.Name);
            Assert.Equal(scheme.BackgroundColor.ToArgb(), restored.BackgroundColor.ToArgb());
            Assert.Equal(scheme.PartColors.Select(c => c.ToArgb()), restored.PartColors.Select(c => c.ToArgb()));
        }

        Assert.Equal("Blueprint", ColorSchemeRegistry.Get(null).Name);
    }

    private static double Distance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return System.Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}

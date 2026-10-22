using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>
/// The thickness box list: gauges and plate fractions read and display as their
/// decimals, and any other typed decimal still works.
/// </summary>
public class StandardThicknessesTests
{
    [Theory]
    [InlineData("16 ga - 0.0598", 0.0598)]
    [InlineData("16 ga", 0.0598)]
    [InlineData("16GA", 0.0598)]
    [InlineData("16 gauge", 0.0598)]
    [InlineData("#10", 0.1345)]
    [InlineData("1/4", 0.25)]
    [InlineData("1/4\"", 0.25)]
    [InlineData("3/32", 0.09375)]
    [InlineData("1-1/4", 1.25)]
    [InlineData("1 1/4\"", 1.25)]
    [InlineData("1-1/2\" - 1.5", 1.5)]
    [InlineData("0.0598", 0.0598)]
    [InlineData("0.123", 0.123)]
    [InlineData("", 0)]
    public void TryParse_ReadsGaugesFractionsAndDecimals(string text, double expected)
    {
        Assert.True(StandardThicknesses.TryParse(text, Units.Inches, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99 ga")]
    [InlineData("-0.1")]
    [InlineData("1/0")]
    [InlineData("1/")]
    public void TryParse_RejectsUnknownText(string text) =>
        Assert.False(StandardThicknesses.TryParse(text, Units.Inches, out _));

    [Fact]
    public void Format_ShowsTheGaugeForAMatchingDecimal()
    {
        Assert.Equal("16 ga - 0.0598", StandardThicknesses.Format(0.0598, Units.Inches));
        Assert.Equal("1/4\" - 0.25", StandardThicknesses.Format(0.25, Units.Inches));
        Assert.Equal("1-1/4\" - 1.25", StandardThicknesses.Format(1.25, Units.Inches));
        Assert.Equal("0.123", StandardThicknesses.Format(0.123, Units.Inches));
    }

    [Fact]
    public void Millimeters_ListAndReadMetricValues()
    {
        Assert.Equal("16 ga - 1.519", StandardThicknesses.Format(0.0598 * 25.4, Units.Millimeters));
        Assert.True(StandardThicknesses.TryParse("16 ga", Units.Millimeters, out var value));
        Assert.Equal(1.51892, value, 5);
        Assert.True(StandardThicknesses.TryParse("3/32", Units.Millimeters, out value));
        Assert.Equal(2.38125, value, 5);
    }

    [Fact]
    public void Entries_ListGaugesThenFractions_ThinnestFirst()
    {
        var gauges = StandardThicknesses.Entries.TakeWhile(e => e.Name.EndsWith(" ga")).ToList();
        Assert.Equal("26 ga", gauges[0].Name);
        Assert.Equal("3 ga", gauges[^1].Name);
        Assert.Equal(gauges.OrderBy(e => e.Inches), gauges);
        var fractions = StandardThicknesses.Entries.Skip(gauges.Count).ToList();
        Assert.Equal(("1/16\"", 0.0625), (fractions[0].Name, fractions[0].Inches));
        Assert.Equal(("2\"", 2.0), (fractions[^1].Name, fractions[^1].Inches));
        Assert.Equal(fractions.OrderBy(e => e.Inches), fractions);
    }
}

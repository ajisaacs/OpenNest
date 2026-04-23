using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.Math;

public class FractionTests
{
    [Theory]
    [InlineData("3/8", 0.375)]
    [InlineData("1 3/4", 1.75)]
    [InlineData("1-3/4", 1.75)]
    [InlineData("1/2", 0.5)]
    public void Parse_ValidFraction_ReturnsDouble(string input, double expected)
    {
        var result = Fraction.Parse(input);

        Assert.Equal(expected, result, 8);
    }

    [Theory]
    [InlineData("3/8", true)]
    [InlineData("abc", false)]
    [InlineData("1 3/4", true)]
    public void IsValid_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, Fraction.IsValid(input));
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        var result = Fraction.TryParse("abc", out var value);

        Assert.False(result);
        Assert.Equal(0, value);
    }

    [Fact]
    public void ReplaceFractionsWithDecimals_ReplacesFractionInString()
    {
        var result = Fraction.ReplaceFractionsWithDecimals("length is 1 3/4 inches");

        Assert.Contains("1.75", result);
        Assert.DoesNotContain("3/4", result);
    }
}

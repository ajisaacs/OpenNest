using System.Globalization;
using System.Text.RegularExpressions;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.Math;

public class FractionTests
{
    // Calibrated on .NET 8: unbounded no-match takes seconds, well above the 250 ms budget.
    private static string TimeoutInput => new string('1', 50000);

    [Fact]
    public void FractionRegex_HasExplicitTimeout()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), Fraction.FractionRegex.MatchTimeout);
    }

    [Theory]
    [InlineData("abc 3/8 xyz", 0.375)]
    [InlineData("abc 1 3/4 xyz", 1.75)]
    [InlineData("abc 1-3/4 xyz", 1.75)]
    public void Fraction_PreservesSubstringMatchingAndMixedSeparators(string input, double expected)
    {
        Assert.True(Fraction.IsValid(input));
        Assert.Equal(expected, Fraction.Parse(input), 8);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Parse_UsesCurrentCultureForOrdinaryIntegerComponents(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            // The implementation uses current-culture double.Parse, not invariant parsing.
            // Ordinary digits are invariant-parseable too; exotic numeric syntax is not this contract.
            var expected = double.Parse("1") + System.Math.Round(double.Parse("3") / double.Parse("4"), 8);
            Assert.Equal(expected, Fraction.Parse("1 3/4"), 8);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ReplaceFractionsWithDecimals_PreservesOrderAndUnmatchedText()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("first 0.5, then 1.75, finally 0.375!",
                Fraction.ReplaceFractionsWithDecimals("first 1/2, then 1-3/4, finally 3/8!"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Parse_OnTimeout_ThrowsRatherThanReturningValue()
    {
        Assert.Throws<RegexMatchTimeoutException>(() => Fraction.Parse(TimeoutInput));
    }

    [Fact]
    public void IsValid_OnTimeout_ThrowsRatherThanReturningValidity()
    {
        Assert.Throws<RegexMatchTimeoutException>(() => Fraction.IsValid(TimeoutInput));
    }

    [Fact]
    public void ReplaceFractionsWithDecimals_OnTimeout_ThrowsRatherThanReturningText()
    {
        Assert.Throws<RegexMatchTimeoutException>(() => Fraction.ReplaceFractionsWithDecimals(TimeoutInput));
    }

    [Fact]
    public void ReplaceFractionsWithDecimals_AfterEarlierMatchTimeout_ReturnsNoPartialText()
    {
        var input = "3/8 " + TimeoutInput;
        var matches = Fraction.FractionRegex.Matches(input).GetEnumerator();
        Assert.True(matches.MoveNext());
        Assert.Equal("3/8", ((Match)matches.Current).Value);
        Assert.Throws<RegexMatchTimeoutException>(() => matches.MoveNext());

        // MatchCollection is lazy; even after an earlier match, the method's local
        // StringBuilder never escapes if enumeration fails (including its sorting step).
        Assert.Throws<RegexMatchTimeoutException>(() => Fraction.ReplaceFractionsWithDecimals(input));
        Assert.StartsWith("3/8 ", input);
    }

    [Fact]
    public void TryParse_OnTimeout_ReturnsFalseAndZero()
    {
        var input = TimeoutInput;
        Assert.Throws<RegexMatchTimeoutException>(() => Fraction.Parse(input));
        Assert.False(Fraction.TryParse(input, out var fraction));
        Assert.Equal(0, fraction);
    }

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

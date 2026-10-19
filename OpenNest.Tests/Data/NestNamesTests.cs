using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestNamesTests
{
    [Theory]
    [InlineData(0, "N26-222")]
    [InlineData(1, "N26-223")]
    [InlineData(29, "N26-22Z")]
    [InlineData(30, "N26-232")]
    [InlineData(900, "N26-322")]
    [InlineData(27000, "N26-3222")]
    public void NamesUseTwoDigitYearAndPaddedBase30Sequence(int number, string expected)
    {
        Assert.Equal(expected, NestNames.Format(2026, number));
        Assert.True(NestNames.TryParse(expected, 2026, out var parsed));
        Assert.Equal(number, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N25-223")]
    [InlineData("N26-23")]
    [InlineData("N26-2223")]
    [InlineData("N26-22B")]
    [InlineData("n26-223")]
    [InlineData("N26-223 copy")]
    [InlineData("N26-ZZZZZZZZZ")]
    public void TryParse_RejectsOtherYearsAndNonCanonicalNames(string? name) =>
        Assert.False(NestNames.TryParse(name, 2026, out _));
}

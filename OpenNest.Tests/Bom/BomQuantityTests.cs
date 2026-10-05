using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public class BomQuantityTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    [InlineData(" 7 ", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void TryParse_AcceptsWholeNumbersOfAtLeastOne(string text, int expected)
    {
        Assert.True(BomQuantity.TryParse(text, out var quantity));
        Assert.Equal(expected, quantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+3")]
    [InlineData("2.5")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void TryParse_RejectsEverythingElse(string? text)
    {
        Assert.False(BomQuantity.TryParse(text!, out var quantity));
        Assert.Equal(0, quantity);
    }
}

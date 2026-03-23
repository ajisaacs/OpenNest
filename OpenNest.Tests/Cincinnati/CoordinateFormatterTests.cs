using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

public class CoordinateFormatterTests
{
    [Theory]
    [InlineData(13.401, 4, "13.401")]
    [InlineData(13.0, 4, "13")]
    [InlineData(0.0, 4, "0")]
    [InlineData(57.4895, 4, "57.4895")]
    [InlineData(13.401, 3, "13.401")]
    [InlineData(13.4016, 3, "13.402")]
    public void FormatCoord_FormatsCorrectly(double value, int accuracy, string expected)
    {
        var formatter = new CoordinateFormatter(accuracy);
        Assert.Equal(expected, formatter.FormatCoord(value));
    }

    [Theory]
    [InlineData(-5.25, 4, "-5.25")]
    [InlineData(-0.001, 4, "-0.001")]
    public void FormatCoord_HandlesNegatives(double value, int accuracy, string expected)
    {
        var formatter = new CoordinateFormatter(accuracy);
        Assert.Equal(expected, formatter.FormatCoord(value));
    }

    [Fact]
    public void Comment_FormatsWithSpaces()
    {
        Assert.Equal("( hello )", CoordinateFormatter.Comment("hello"));
    }

    [Fact]
    public void InlineComment_FormatsWithoutSpaces()
    {
        Assert.Equal("(hello)", CoordinateFormatter.InlineComment("hello"));
    }
}

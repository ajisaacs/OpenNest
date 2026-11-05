using System.Linq;
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class PlateSizesTests
{
    [Fact]
    public void All_IsNotEmpty()
    {
        Assert.NotEmpty(PlateSizes.All);
    }

    [Fact]
    public void All_Smallest_Is48x96()
    {
        var smallest = PlateSizes.All.OrderBy(e => e.Area).First();
        Assert.Equal(48, smallest.Width);
        Assert.Equal(96, smallest.Length);
    }

    [Fact]
    public void All_SortedByAreaAscending()
    {
        for (var i = 1; i < PlateSizes.All.Count; i++)
            Assert.True(PlateSizes.All[i].Area >= PlateSizes.All[i - 1].Area);
    }

    [Fact]
    public void All_Entries_AreCanonical_WidthLessOrEqualLength()
    {
        foreach (var entry in PlateSizes.All)
            Assert.True(entry.Width <= entry.Length, $"{entry.Label} not in canonical orientation");
    }

    [Theory]
    [InlineData(40, 40, true)] // small - fits trivially
    [InlineData(48, 96, true)] // exact
    [InlineData(96, 48, true)] // rotated exact
    [InlineData(90, 40, true)] // rotated
    [InlineData(49, 97, false)] // just over in both dims
    [InlineData(50, 50, false)] // too wide in both orientations
    public void Entry_Fits_RespectsRotation(double w, double h, bool expected)
    {
        var entry = new PlateSizes.Entry("48x96", 48, 96);
        Assert.Equal(expected, entry.Fits(w, h));
    }

    [Fact]
    public void TryGet_KnownLabel_ReturnsEntry()
    {
        Assert.True(PlateSizes.TryGet("48x96", out var entry));
        Assert.Equal(48, entry.Width);
        Assert.Equal(96, entry.Length);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        Assert.True(PlateSizes.TryGet("48X96", out var entry));
        Assert.Equal(48, entry.Width);
        Assert.Equal(96, entry.Length);
    }

    [Fact]
    public void TryGet_UnknownLabel_ReturnsFalse()
    {
        Assert.False(PlateSizes.TryGet("bogus", out _));
    }
}

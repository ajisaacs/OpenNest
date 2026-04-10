using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class PipeSizesTests
{
    [Fact]
    public void All_ContainsExpectedCount()
    {
        Assert.Equal(35, PipeSizes.All.Count);
    }

    [Fact]
    public void All_IsSortedByOuterDiameterAscending()
    {
        for (var i = 1; i < PipeSizes.All.Count; i++)
            Assert.True(PipeSizes.All[i].OuterDiameter > PipeSizes.All[i - 1].OuterDiameter);
    }

    [Theory]
    [InlineData("1/8", 0.405)]
    [InlineData("1/2", 0.840)]
    [InlineData("2", 2.375)]
    [InlineData("2 1/2", 2.875)]
    [InlineData("12", 12.750)]
    [InlineData("48", 48.000)]
    public void TryGetOD_KnownLabel_ReturnsExpectedOD(string label, double expected)
    {
        Assert.True(PipeSizes.TryGetOD(label, out var od));
        Assert.Equal(expected, od, 0.001);
    }

    [Fact]
    public void TryGetOD_UnknownLabel_ReturnsFalse()
    {
        Assert.False(PipeSizes.TryGetOD("bogus", out _));
    }

    [Fact]
    public void GetFittingSizes_FiltersByMaxOD()
    {
        var results = PipeSizes.GetFittingSizes(3.0).ToList();

        Assert.Contains(results, e => e.Label == "2 1/2");
        Assert.DoesNotContain(results, e => e.Label == "3");
        Assert.DoesNotContain(results, e => e.Label == "4");
    }

    [Fact]
    public void GetFittingSizes_MaxSmallerThanSmallest_ReturnsEmpty()
    {
        Assert.Empty(PipeSizes.GetFittingSizes(0.1));
    }
}

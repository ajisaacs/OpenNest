using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class DefaultFillComparerTests
{
    private readonly IFillComparer comparer = new DefaultFillComparer();
    private readonly Box workArea = new(0, 0, 100, 100);

    [Fact]
    public void NullCandidate_ReturnsFalse()
    {
        var current = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.False(comparer.IsBetter(null, current, workArea));
    }

    [Fact]
    public void EmptyCandidate_ReturnsFalse()
    {
        var current = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.False(comparer.IsBetter(new List<Part>(), current, workArea));
    }

    [Fact]
    public void NullCurrent_ReturnsTrue()
    {
        var candidate = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.True(comparer.IsBetter(candidate, null, workArea));
    }

    [Fact]
    public void HigherCount_Wins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10)
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10)
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void SameCount_HigherDensityWins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10)
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(50, 0, 10)
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }
}

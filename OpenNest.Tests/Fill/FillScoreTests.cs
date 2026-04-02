using OpenNest.Geometry;
using OpenNest.Engine.Fill;

namespace OpenNest.Tests.Fill;

public class FillScoreTests
{
    [Fact]
    public void HigherCount_WinsOverLowerCount()
    {
        var a = new FillScore(10, 0.5);
        var b = new FillScore(5, 0.9);

        Assert.True(a > b);
        Assert.False(b > a);
    }

    [Fact]
    public void SameCount_HigherDensityWins()
    {
        var a = new FillScore(10, 0.8);
        var b = new FillScore(10, 0.5);

        Assert.True(a > b);
        Assert.False(b > a);
    }

    [Fact]
    public void EqualScores_AreNotGreaterOrLess()
    {
        var a = new FillScore(10, 0.5);
        var b = new FillScore(10, 0.5);

        Assert.False(a > b);
        Assert.False(a < b);
        Assert.True(a >= b);
        Assert.True(a <= b);
    }

    [Fact]
    public void Default_IsZero()
    {
        var score = default(FillScore);

        Assert.Equal(0, score.Count);
        Assert.Equal(0, score.Density);
    }

    [Fact]
    public void Compute_NullParts_ReturnsDefault()
    {
        var score = FillScore.Compute(null, new Box(0, 0, 100, 100));

        Assert.Equal(0, score.Count);
    }

    [Fact]
    public void Compute_EmptyParts_ReturnsDefault()
    {
        var score = FillScore.Compute(new System.Collections.Generic.List<Part>(), new Box(0, 0, 100, 100));

        Assert.Equal(0, score.Count);
    }

    [Fact]
    public void Compute_WithParts_ReturnsCorrectCount()
    {
        var parts = new System.Collections.Generic.List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10)
        };
        var score = FillScore.Compute(parts, new Box(0, 0, 100, 100));

        Assert.Equal(3, score.Count);
        Assert.True(score.Density > 0);
    }

    [Fact]
    public void CompareTo_IsConsistentWithOperators()
    {
        var a = new FillScore(10, 0.8);
        var b = new FillScore(5, 0.9);

        Assert.True(a.CompareTo(b) > 0);
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(a >= b);
        Assert.True(b <= a);
    }
}

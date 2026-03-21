using OpenNest.Geometry;

namespace OpenNest.Tests;

public class NestProgressTests
{
    [Fact]
    public void BestPartCount_NullParts_ReturnsZero()
    {
        var progress = new NestProgress { BestParts = null };
        Assert.Equal(0, progress.BestPartCount);
    }

    [Fact]
    public void BestPartCount_ReturnsBestPartsCount()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
        };
        var progress = new NestProgress { BestParts = parts };
        Assert.Equal(2, progress.BestPartCount);
    }

    [Fact]
    public void BestDensity_NullParts_ReturnsZero()
    {
        var progress = new NestProgress { BestParts = null };
        Assert.Equal(0, progress.BestDensity);
    }

    [Fact]
    public void BestDensity_MatchesFillScoreFormula()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(5, 0, 5),
        };
        var workArea = new Box(0, 0, 100, 100);
        var progress = new NestProgress { BestParts = parts, ActiveWorkArea = workArea };
        Assert.Equal(1.0, progress.BestDensity, precision: 4);
    }

    [Fact]
    public void NestedWidth_ReturnsPartsSpan()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
        };
        var progress = new NestProgress { BestParts = parts };
        Assert.Equal(15, progress.NestedWidth, precision: 4);
    }

    [Fact]
    public void NestedLength_ReturnsPartsSpan()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(0, 10, 5),
        };
        var progress = new NestProgress { BestParts = parts };
        Assert.Equal(15, progress.NestedLength, precision: 4);
    }

    [Fact]
    public void NestedArea_ReturnsSumOfPartAreas()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
        };
        var progress = new NestProgress { BestParts = parts };
        Assert.Equal(50, progress.NestedArea, precision: 4);
    }

    [Fact]
    public void SettingBestParts_InvalidatesCache()
    {
        var parts1 = new List<Part> { TestHelpers.MakePartAt(0, 0, 5) };
        var parts2 = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
        };

        var progress = new NestProgress { BestParts = parts1 };
        Assert.Equal(1, progress.BestPartCount);
        Assert.Equal(25, progress.NestedArea, precision: 4);

        progress.BestParts = parts2;
        Assert.Equal(2, progress.BestPartCount);
        Assert.Equal(50, progress.NestedArea, precision: 4);
    }
}

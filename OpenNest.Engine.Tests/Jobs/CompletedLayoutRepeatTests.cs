using System;
using System.Collections.Generic;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Tests.Jobs;

public class CompletedLayoutRepeatTests
{
    private static Dictionary<string, int> Counts(int a, int b) => new(StringComparer.Ordinal) { ["a"] = a, ["b"] = b };

    [Theory]
    [InlineData(null, null, 5)]
    [InlineData(3, null, 3)]
    [InlineData(null, 2, 2)]
    [InlineData(4, 3, 3)]
    [InlineData(0, null, 0)]
    [InlineData(null, 0, 0)]
    public void ScarcestRequirementAndEachPhysicalBoundClampTotalCopies(int? stock, int? cap, int expected)
    {
        Assert.Equal(expected, CompletedLayoutRepeat.Limit(Counts(20, 12), Counts(4, 2), true, 1, stock, cap));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void UnknownSearchAndCompetingEntriesUseOne(bool completed, int entries)
    {
        Assert.Equal(1, CompletedLayoutRepeat.Limit(Counts(20, 12), Counts(4, 2), completed, entries, null, null));
    }

    [Fact]
    public void EmptyLayoutAndNoStockCannotProduceASheet()
    {
        Assert.Equal(0, CompletedLayoutRepeat.Limit(Counts(20, 12), new Dictionary<string, int>(), true, 1, null, null));
        Assert.Equal(0, CompletedLayoutRepeat.Limit(Counts(20, 12), Counts(4, 2), true, 0, null, null));
    }

    [Fact]
    public void ExactCaseSensitiveIdsAndOnlyPresentRequirementsMatter()
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 20, ["A"] = 3, ["absent"] = 0 };
        var layout = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 4, ["A"] = 1 };
        Assert.Equal(3, CompletedLayoutRepeat.Limit(remaining, layout, true, 1, null, null));
    }

    [Fact]
    public void IntegerEdgeHasNoMultiplicationOverflowOrFirstCopyOffByOne()
    {
        var remaining = new Dictionary<string, int> { ["a"] = int.MaxValue };
        Assert.Equal(int.MaxValue, CompletedLayoutRepeat.Limit(remaining, new Dictionary<string, int> { ["a"] = 1 }, true, 1, null, null));
        var q = CompletedLayoutRepeat.Limit(remaining, new Dictionary<string, int> { ["a"] = 2 }, true, 1, null, null);
        Assert.Equal(1, checked(int.MaxValue - q * 2));
        Assert.Equal(0, CompletedLayoutRepeat.Limit(Counts(3, 1), Counts(4, 2), true, 1, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCountsAndUnknownIdsAreRefused(int count)
    {
        Assert.Throws<ArgumentException>(() => CompletedLayoutRepeat.Limit(Counts(20, 12), Counts(count, 2), true, 1, null, null));
        Assert.Throws<ArgumentException>(() => CompletedLayoutRepeat.Limit(Counts(20, 12), new Dictionary<string, int> { ["unknown"] = 1 }, true, 1, null, null));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    public void NegativeBoundsAreRefused(int? stock, int? cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompletedLayoutRepeat.Limit(Counts(20, 12), Counts(4, 2), true, 1, stock, cap));
    }
}

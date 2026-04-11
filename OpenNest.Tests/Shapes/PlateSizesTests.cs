using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
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
    public void All_DoesNotContain48x48()
    {
        // 48x48 is not a standard sheet - it's the default MinSheet threshold only.
        Assert.DoesNotContain(PlateSizes.All, e => e.Width == 48 && e.Length == 48);
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
    [InlineData(40, 40, true)]    // small - fits trivially
    [InlineData(48, 96, true)]    // exact
    [InlineData(96, 48, true)]    // rotated exact
    [InlineData(90, 40, true)]    // rotated
    [InlineData(49, 97, false)]   // just over in both dims
    [InlineData(50, 50, false)]   // too wide in both orientations
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

    [Fact]
    public void Recommend_BelowMin_SnapsToDefaultIncrementOfOne()
    {
        var bbox = new Box(0, 0, 10.3, 20.7);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal(11, result.Width);
        Assert.Equal(21, result.Length);
        Assert.Null(result.MatchedLabel);
    }

    [Fact]
    public void Recommend_BelowMin_UsesCustomIncrement()
    {
        var bbox = new Box(0, 0, 10.3, 20.7);
        var options = new PlateSizeOptions { SnapIncrement = 0.25 };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal(10.5, result.Width, 4);
        Assert.Equal(20.75, result.Length, 4);
        Assert.Null(result.MatchedLabel);
    }

    [Fact]
    public void Recommend_ExactlyAtMin_Snaps()
    {
        var bbox = new Box(0, 0, 48, 48);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal(48, result.Width);
        Assert.Equal(48, result.Length);
        Assert.Null(result.MatchedLabel);
    }

    [Fact]
    public void Recommend_AboveMin_PicksSmallestContainingStandardSheet()
    {
        var bbox = new Box(0, 0, 40, 90);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal(48, result.Width);
        Assert.Equal(96, result.Length);
        Assert.Equal("48x96", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_AboveMin_WithRotation_PicksSmallestSheet()
    {
        var bbox = new Box(0, 0, 90, 40);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal("48x96", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_JustOver48_PicksNextStandardSize()
    {
        var bbox = new Box(0, 0, 50, 100);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal(60, result.Width);
        Assert.Equal(120, result.Length);
        Assert.Equal("60x120", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_MarginIsAppliedPerSide()
    {
        // 46 + 2*1 = 48 (fits exactly), 94 + 2*1 = 96 (fits exactly)
        var bbox = new Box(0, 0, 46, 94);
        var options = new PlateSizeOptions { Margin = 1 };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal("48x96", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_MarginPushesToNextSheet()
    {
        // 47 + 2 = 49 > 48, so 48x96 no longer fits -> next standard
        var bbox = new Box(0, 0, 47, 95);
        var options = new PlateSizeOptions { Margin = 1 };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.NotEqual("48x96", result.MatchedLabel);
        Assert.True(result.Width >= 49);
        Assert.True(result.Length >= 97);
    }

    [Fact]
    public void Recommend_AllowedSizes_StandardLabelWhitelist()
    {
        // 60x120 is the only option; 50x50 is above min so it routes to standard
        var bbox = new Box(0, 0, 50, 50);
        var options = new PlateSizeOptions { AllowedSizes = new[] { "60x120" } };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal("60x120", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_AllowedSizes_ArbitraryWxHString()
    {
        // 50x100 isn't in the standard catalog but is valid as an ad-hoc entry.
        // bbox 49x99 doesn't fit 48x96 or 48x120, does fit 50x100 and 60x120,
        // but only 50x100 is allowed.
        var bbox = new Box(0, 0, 49, 99);
        var options = new PlateSizeOptions { AllowedSizes = new[] { "50x100" } };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal(50, result.Width);
        Assert.Equal(100, result.Length);
        Assert.Equal("50x100", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_NothingFits_FallsBackToSnapUp()
    {
        // Larger than any catalog sheet
        var bbox = new Box(0, 0, 100, 300);

        var result = PlateSizes.Recommend(bbox);

        Assert.Equal(100, result.Width);
        Assert.Equal(300, result.Length);
        Assert.Null(result.MatchedLabel);
    }

    [Fact]
    public void Recommend_NothingFitsInAllowedList_FallsBackToSnapUp()
    {
        // Only 48x96 allowed, but bbox is too big for it
        var bbox = new Box(0, 0, 50, 100);
        var options = new PlateSizeOptions { AllowedSizes = new[] { "48x96" } };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal(50, result.Width);
        Assert.Equal(100, result.Length);
        Assert.Null(result.MatchedLabel);
    }

    [Fact]
    public void Recommend_BoxEnumerable_CombinesIntoEnvelope()
    {
        // Two boxes that together span 0..40 x 0..90 -> fits 48x96
        var boxes = new[]
        {
            new Box(0, 0, 40, 50),
            new Box(0, 40, 30, 50),
        };

        var result = PlateSizes.Recommend(boxes);

        Assert.Equal("48x96", result.MatchedLabel);
    }

    [Fact]
    public void Recommend_BoxEnumerable_Empty_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => PlateSizes.Recommend(System.Array.Empty<Box>()));
    }

    [Fact]
    public void PlateSizeOptions_Defaults()
    {
        var options = new PlateSizeOptions();

        Assert.Equal(48, options.MinSheetWidth);
        Assert.Equal(48, options.MinSheetLength);
        Assert.Equal(1.0, options.SnapIncrement);
        Assert.Equal(0, options.Margin);
        Assert.Null(options.AllowedSizes);
        Assert.Equal(PlateSizeSelection.SmallestArea, options.Selection);
    }

    [Fact]
    public void Recommend_NarrowestFirst_PicksNarrowerSheetOverSmallerArea()
    {
        // Hypothetical: bbox (47, 47) fits both 48x96 (area 4608) and some narrower option.
        // With SmallestArea: picks 48x96 (it's already the smallest 48-wide).
        // With NarrowestFirst: also picks 48x96 since that's the narrowest.
        // Better test: AllowedSizes = ["60x120", "48x120"] with bbox that fits both.
        // 48x120 (area 5760) is narrower; 60x120 (area 7200) has more area.
        // SmallestArea picks 48x120; NarrowestFirst also picks 48x120. Both pick the same.
        //
        // Real divergence: AllowedSizes = ["60x120", "72x120"] with bbox 55x100.
        // 60x120 has narrower width (60) AND smaller area (7200 vs 8640), so both agree.
        //
        // To force divergence: AllowedSizes = ["60x96", "48x144"] with bbox 47x95.
        // 60x96 area = 5760, 48x144 area = 6912. SmallestArea -> 60x96.
        // NarrowestFirst width 48 < 60 -> 48x144.
        var bbox = new Box(0, 0, 47, 95);
        var options = new PlateSizeOptions
        {
            AllowedSizes = new[] { "60x96", "48x144" },
            Selection = PlateSizeSelection.NarrowestFirst,
        };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal(48, result.Width);
        Assert.Equal(144, result.Length);
    }

    [Fact]
    public void Recommend_SmallestArea_PicksSmallerAreaOverNarrowerWidth()
    {
        var bbox = new Box(0, 0, 47, 95);
        var options = new PlateSizeOptions
        {
            AllowedSizes = new[] { "60x96", "48x144" },
            Selection = PlateSizeSelection.SmallestArea,
        };

        var result = PlateSizes.Recommend(bbox, options);

        Assert.Equal(60, result.Width);
        Assert.Equal(96, result.Length);
    }
}

using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingLibraryFilterTests
{
    [Fact]
    public void None_IsEmptyAndNeedsNoGeometry()
    {
        Assert.True(new DrawingLibraryFilter().IsEmpty);
        Assert.False(DrawingLibraryFilter.None.NeedsGeometry);
        Assert.Equal("", DrawingLibraryFilter.None.Describe());
        Assert.Null(DrawingLibraryFilter.None.GetValidationError());
        Assert.False(new DrawingLibraryFilter { IncludeBlankThickness = false }.IsEmpty);
        Assert.True(new DrawingLibraryFilter { MinHoles = 0 }.NeedsGeometry);
    }

    public static TheoryData<DrawingLibraryFilter> InvalidFilters() => new()
    {
        new DrawingLibraryFilter { MinThickness = 0.25, MaxThickness = 0.1 },
        new DrawingLibraryFilter { MinThickness = -0.1 },
        new DrawingLibraryFilter { MaxLength = double.PositiveInfinity },
        new DrawingLibraryFilter { MinWidth = 2, MaxWidth = 1 },
        new DrawingLibraryFilter { MinHoles = 3, MaxHoles = 2 },
        new DrawingLibraryFilter { MaxHoles = -1 },
        new DrawingLibraryFilter { SavedFrom = new DateOnly(2026, 2, 1), SavedTo = new DateOnly(2026, 1, 1) },
        new DrawingLibraryFilter { Customer = new string('x', DrawingLibraryRules.MaxTextLength + 1) },
    };

    [Theory]
    [MemberData(nameof(InvalidFilters))]
    public void InvalidRanges_AreReported(DrawingLibraryFilter filter) =>
        Assert.NotNull(filter.GetValidationError());

    [Fact]
    public void Describe_SummarizesEachLimit()
    {
        var filter = new DrawingLibraryFilter
        {
            Customer = " Synthetic Co ",
            MinThickness = 0.0598,
            MaxThickness = 0.1875,
            MaxLength = 4,
            MinWidth = 1,
            MaxWidth = 1,
            MinHoles = 2,
        };

        Assert.Equal("Customer Synthetic Co; Thickness 0.0598-0.1875 in (or none); Length up to 4 in; Width 1 in; Holes 2+",
            filter.Describe());
        Assert.Equal("Has a thickness", new DrawingLibraryFilter { IncludeBlankThickness = false }.Describe());
    }

    [Fact]
    public void EffectiveCustomer_PrefersTheQueryCustomer()
    {
        var filter = new DrawingLibraryFilter { Customer = "Filter Co" };

        Assert.Equal("Filter Co", new DrawingLibraryQuery { Filter = filter }.EffectiveCustomer);
        Assert.Equal("Query Co", new DrawingLibraryQuery { Customer = "Query Co", Filter = filter }.EffectiveCustomer);
    }
}

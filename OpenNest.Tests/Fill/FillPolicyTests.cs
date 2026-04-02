using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

namespace OpenNest.Tests.Fill;

public class FillWithDirectionPreferenceTests
{
    private readonly IFillComparer comparer = new DefaultFillComparer();
    private readonly Box workArea = new(0, 0, 100, 100);

    [Fact]
    public void NullPreference_TriesBothDirections_ReturnsBetter()
    {
        var hParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10), TestHelpers.MakePartAt(12, 0, 10) };
        var vParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = FillHelpers.FillWithDirectionPreference(
            dir => dir == NestDirection.Horizontal ? hParts : vParts,
            null, comparer, workArea);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void PreferredDirection_UsedFirst_WhenProducesResults()
    {
        var hParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10), TestHelpers.MakePartAt(12, 0, 10) };
        var vParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10), TestHelpers.MakePartAt(0, 12, 10), TestHelpers.MakePartAt(0, 24, 10) };

        var result = FillHelpers.FillWithDirectionPreference(
            dir => dir == NestDirection.Horizontal ? hParts : vParts,
            NestDirection.Horizontal, comparer, workArea);

        Assert.Equal(2, result.Count); // H has results, so H is returned (preferred)
    }

    [Fact]
    public void PreferredDirection_FallsBack_WhenPreferredReturnsEmpty()
    {
        var vParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = FillHelpers.FillWithDirectionPreference(
            dir => dir == NestDirection.Horizontal ? new List<Part>() : vParts,
            NestDirection.Horizontal, comparer, workArea);

        Assert.Equal(1, result.Count); // Falls back to V
    }
}

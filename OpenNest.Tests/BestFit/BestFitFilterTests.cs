using OpenNest.Engine.BestFit;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

public class BestFitFilterTests
{
    // A 27.4 x 3.5 bar: aspect 7.8 on its own, past the default limit of 5.
    private static Drawing MakeLongBar() =>
        new RectangleShape { Width = 27.4, Length = 3.5 }.GetDrawing();

    private static BestFitResult MakePair(Drawing drawing, double width, double height) =>
        new()
        {
            Candidate = new PairCandidate { Drawing = drawing },
            BoundingWidth = width,
            BoundingHeight = height,
            RotatedArea = width * height,
            TrueArea = 0.5 * width * height,
            Keep = true,
        };

    private static BestFitFilter MakeFilter() =>
        BestFitFinder.CreateFilter(60, 120);

    [Fact]
    public void Apply_KeepsPairLessElongatedThanThePartAlone()
    {
        // Interlocked side by side, staggered lengthwise: aspect 5.1, under the part's own.
        var pair = MakePair(MakeLongBar(), 5.7, 29.2);

        MakeFilter().Apply([pair]);

        Assert.True(pair.Keep, pair.Reason);
    }

    [Fact]
    public void Apply_RejectsPairMoreElongatedThanThePartAlone()
    {
        // End to end: aspect 15.7, well past the part's own.
        var pair = MakePair(MakeLongBar(), 3.5, 55);

        MakeFilter().Apply([pair]);

        Assert.False(pair.Keep);
        Assert.StartsWith("Aspect ratio", pair.Reason);
    }

    [Fact]
    public void Apply_CompactPartKeepsTheDefaultLimit()
    {
        var drawing = new RectangleShape { Width = 4, Length = 2 }.GetDrawing();
        var pair = MakePair(drawing, 2, 11);

        MakeFilter().Apply([pair]);

        Assert.False(pair.Keep);
    }
}

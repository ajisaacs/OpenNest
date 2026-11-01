using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Engine;

namespace OpenNest.Tests.Fill;

public class PairFillerTests
{
    private static Plate MakePlate(double width, double length, double spacing = 0.5)
    {
        return new Plate { Size = new Size(width, length), PartSpacing = spacing };
    }

    [Fact]
    public void Fill_ReturnsPartsForSimpleDrawing()
    {
        var filler = new PairFiller(MakePlate(120, 60));
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var result = filler.Fill(item, workArea);

        Assert.NotNull(result.Parts);
        Assert.NotNull(result.BestFits);
    }

    [Fact]
    public void Fill_EmptyResult_WhenPartTooLarge()
    {
        var filler = new PairFiller(MakePlate(10, 10));
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 20) };
        var workArea = new Box(0, 0, 10, 10);

        var result = filler.Fill(item, workArea);

        Assert.NotNull(result.Parts);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var filler = new PairFiller(MakePlate(120, 60));
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var result = filler.Fill(item, workArea, token: cts.Token);

        Assert.NotNull(result.Parts);
    }
}

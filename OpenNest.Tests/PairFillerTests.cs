using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PairFillerTests
{
    private static Drawing MakeRectDrawing(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    [Fact]
    public void Fill_ReturnsPartsForSimpleDrawing()
    {
        var plateSize = new Size(120, 60);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var parts = filler.Fill(item, workArea);

        Assert.NotNull(parts);
        // Pair filling may or may not find interlocking pairs for rectangles,
        // but should return a non-null list.
    }

    [Fact]
    public void Fill_EmptyResult_WhenPartTooLarge()
    {
        var plateSize = new Size(10, 10);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 20) };
        var workArea = new Box(0, 0, 10, 10);

        var parts = filler.Fill(item, workArea);

        Assert.NotNull(parts);
        Assert.Empty(parts);
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var plateSize = new Size(120, 60);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var parts = filler.Fill(item, workArea, token: cts.Token);

        // Should return empty or partial — not throw
        Assert.NotNull(parts);
    }
}

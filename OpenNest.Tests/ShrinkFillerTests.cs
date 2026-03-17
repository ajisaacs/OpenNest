using OpenNest.Geometry;

namespace OpenNest.Tests;

public class ShrinkFillerTests
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("square", pgm);
    }

    [Fact]
    public void Shrink_ReducesDimension_UntilCountDrops()
    {
        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Height);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
        Assert.True(result.Dimension <= 50, "Dimension should be <= original");
        Assert.True(result.Dimension > 0);
    }

    [Fact]
    public void Shrink_Width_ReducesHorizontally()
    {
        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Width);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
        Assert.True(result.Dimension <= 100);
    }

    [Fact]
    public void Shrink_RespectsMaxIterations()
    {
        var callCount = 0;
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            callCount++;
            return new List<Part> { TestHelpers.MakePartAt(0, 0, 5) };
        };

        var item = new NestItem { Drawing = MakeSquareDrawing(5) };
        var box = new Box(0, 0, 100, 100);

        ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Height, maxIterations: 3);

        // 1 initial + up to 3 shrink iterations = max 4 calls
        Assert.True(callCount <= 4);
    }

    [Fact]
    public void Shrink_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0,
            ShrinkAxis.Height, token: cts.Token);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
    }
}

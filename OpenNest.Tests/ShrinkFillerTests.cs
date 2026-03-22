using OpenNest.Engine.Fill;
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
    public void Shrink_FillsAndReturnsDimension()
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

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Length);

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
            ShrinkAxis.Length, token: cts.Token);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
    }

    [Fact]
    public void TrimToCount_Width_KeepsPartsNearestToOrigin()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),   // Right = 5
            TestHelpers.MakePartAt(10, 0, 5),  // Right = 15
            TestHelpers.MakePartAt(20, 0, 5),  // Right = 25
            TestHelpers.MakePartAt(30, 0, 5),  // Right = 35
        };

        var trimmed = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Width);

        Assert.Equal(2, trimmed.Count);
        Assert.True(trimmed.All(p => p.BoundingBox.Right <= 15));
    }

    [Fact]
    public void TrimToCount_Length_KeepsPartsNearestToOrigin()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),   // Top = 5
            TestHelpers.MakePartAt(0, 10, 5),  // Top = 15
            TestHelpers.MakePartAt(0, 20, 5),  // Top = 25
            TestHelpers.MakePartAt(0, 30, 5),  // Top = 35
        };

        var trimmed = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Length);

        Assert.Equal(2, trimmed.Count);
        Assert.True(trimmed.All(p => p.BoundingBox.Top <= 15));
    }

    [Fact]
    public void TrimToCount_ReturnsInput_WhenCountAtOrBelowTarget()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
        };

        var same = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Width);
        Assert.Same(parts, same);

        var fewer = ShrinkFiller.TrimToCount(parts, 5, ShrinkAxis.Width);
        Assert.Same(parts, fewer);
    }

    [Fact]
    public void TrimToCount_DoesNotMutateInput()
    {
        var parts = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 5),
            TestHelpers.MakePartAt(10, 0, 5),
            TestHelpers.MakePartAt(20, 0, 5),
        };

        var originalCount = parts.Count;
        var trimmed = ShrinkFiller.TrimToCount(parts, 1, ShrinkAxis.Width);

        Assert.Equal(originalCount, parts.Count);
        Assert.Equal(1, trimmed.Count);
    }
}

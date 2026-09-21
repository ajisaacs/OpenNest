using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Engine;

namespace OpenNest.Tests.Fill;

public class RemnantFillerTests2
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("sq", pgm);
    }

    [Fact]
    public void FillItems_PlacesPartsInRemnants()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        // Place a large obstacle leaving a 40x100 strip on the right
        filler.AddObstacles(new[] { TestHelpers.MakePartAt(0, 0, 50) });

        var drawing = MakeSquareDrawing(10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 5 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var placed = filler.FillItems(items, fillFunc);

        Assert.True(placed.Count > 0, "Should place parts in remaining space");
    }

    [Fact]
    public void FillItems_DoesNotMutateItemQuantities()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        var drawing = MakeSquareDrawing(10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 3 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        filler.FillItems(items, fillFunc);

        Assert.Equal(3, items[0].Quantity);
    }

    [Fact]
    public void FillItems_EmptyItems_ReturnsEmpty()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();

        var result = filler.FillItems(new List<NestItem>(), fillFunc);

        Assert.Empty(result);
    }

    [Fact]
    public void FillItems_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        var drawing = MakeSquareDrawing(10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 5 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = filler.FillItems(items, fillFunc, cts.Token);

        // Should not throw, returns whatever was placed
        Assert.NotNull(result);
    }

    [Fact]
    public void FillItems_SingleDrawing_KeepsEveryPlacedPart()
    {
        // With no other drawing waiting for space there is nothing to keep clear, so a full
        // grid fill must not lose its topmost part.
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 0);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeSquareDrawing(10), Quantity = 4 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part>
            {
                TestHelpers.MakePartAt(0, 0, 10),
                TestHelpers.MakePartAt(10, 0, 10),
                TestHelpers.MakePartAt(0, 10, 10),
                TestHelpers.MakePartAt(10, 10, 10),
            };

        var placed = filler.FillItems(items, fillFunc);

        Assert.Equal(4, placed.Count);
    }
}

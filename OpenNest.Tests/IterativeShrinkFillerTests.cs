using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class IterativeShrinkFillerTests
{
    [Fact]
    public void Fill_NullItems_ReturnsEmpty()
    {
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();
        var result = IterativeShrinkFiller.Fill(null, new Box(0, 0, 100, 100), fillFunc, 1.0);

        Assert.Empty(result.Parts);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public void Fill_EmptyItems_ReturnsEmpty()
    {
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();
        var result = IterativeShrinkFiller.Fill(new List<NestItem>(), new Box(0, 0, 100, 100), fillFunc, 1.0);

        Assert.Empty(result.Parts);
        Assert.Empty(result.Leftovers);
    }

    private static Drawing MakeRectDrawing(double w, double h, string name = "rect")
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing(name, pgm);
    }

    [Fact]
    public void Fill_SingleItem_PlacesParts()
    {
        var drawing = MakeRectDrawing(20, 10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 5 }
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

        Assert.True(result.Parts.Count > 0, "Should place parts");
    }

    [Fact]
    public void Fill_MultipleItems_PlacesFromBoth()
    {
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "large"), Quantity = 5 },
            new NestItem { Drawing = MakeRectDrawing(8, 5, "small"), Quantity = 5 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

        var largeCount = result.Parts.Count(p => p.BaseDrawing.Name == "large");
        var smallCount = result.Parts.Count(p => p.BaseDrawing.Name == "small");

        Assert.True(largeCount > 0, "Should place large parts");
        Assert.True(smallCount > 0, "Should place small parts in remaining space");
    }

    [Fact]
    public void Fill_UnfilledQuantity_ReturnsLeftovers()
    {
        // Huge quantity that can't all fit on a small plate
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 1000 },
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 60, 30), fillFunc, 1.0);

        Assert.True(result.Parts.Count > 0, "Should place some parts");
        Assert.True(result.Leftovers.Count > 0, "Should have leftovers");
        Assert.True(result.Leftovers[0].Quantity > 0, "Leftover quantity should be positive");
    }

    [Fact]
    public void Fill_UnlimitedQuantity_PlacesParts()
    {
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 0 }
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

        Assert.True(result.Parts.Count > 0, "Unlimited qty items should still be placed");
        Assert.Empty(result.Leftovers); // unlimited items never produce leftovers
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 10 }
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 100, 100), fillFunc, 1.0, cts.Token);

        Assert.NotNull(result);
    }
}

using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

public class PlateOptimizerTests
{
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
    public void PicksCheapestPlateThatFitsParts()
    {
        var options = new List<PlateOption>
        {
            new() { Width = 20, Length = 20, Cost = 100 },
            new() { Width = 40, Length = 40, Cost = 400 },
        };

        var templatePlate = new Plate(40, 40) { PartSpacing = 0 };
        var items = new List<NestItem>
        {
            new() { Drawing = MakeRectDrawing(10, 10), Quantity = 1 }
        };

        var result = PlateOptimizer.Optimize(items, options, 0.0, templatePlate);

        Assert.NotNull(result);
        Assert.Equal(20, result.ChosenSize.Width);
        Assert.True(result.Parts.Count >= 1);
    }

    [Fact]
    public void PrefersMorePartsOverCheaperPlate()
    {
        var options = new List<PlateOption>
        {
            new() { Width = 12, Length = 12, Cost = 50 },
            new() { Width = 24, Length = 12, Cost = 100 },
        };

        var templatePlate = new Plate(24, 12) { PartSpacing = 0 };
        var items = new List<NestItem>
        {
            new() { Drawing = MakeRectDrawing(10, 10), Quantity = 2 }
        };

        var result = PlateOptimizer.Optimize(items, options, 0.0, templatePlate);

        Assert.NotNull(result);
        Assert.Equal(24, result.ChosenSize.Width);
        Assert.Equal(2, result.Parts.Count);
    }

    [Fact]
    public void SalvageRateReducesNetCost()
    {
        // Small: 20x20=400sqin, cost $400. Part=10x10=100sqin. Remnant=300.
        //   Net = 400 - 300*(400/400)*1.0 = 400-300 = 100
        // Large: 40x40=1600sqin, cost $800. Part=10x10=100sqin. Remnant=1500.
        //   Net = 800 - 1500*(800/1600)*1.0 = 800-750 = 50
        var options = new List<PlateOption>
        {
            new() { Width = 20, Length = 20, Cost = 400 },
            new() { Width = 40, Length = 40, Cost = 800 },
        };

        var templatePlate = new Plate(40, 40) { PartSpacing = 0 };
        templatePlate.EdgeSpacing = new Spacing();
        var items = new List<NestItem>
        {
            new() { Drawing = MakeRectDrawing(10, 10), Quantity = 1 }
        };

        var result = PlateOptimizer.Optimize(items, options, 1.0, templatePlate);

        Assert.NotNull(result);
        Assert.Equal(40, result.ChosenSize.Width);
    }

    [Fact]
    public void SkipsPlatesThatAreTooSmall()
    {
        var options = new List<PlateOption>
        {
            new() { Width = 20, Length = 20, Cost = 100 },
            new() { Width = 40, Length = 40, Cost = 400 },
        };

        var templatePlate = new Plate(40, 40) { PartSpacing = 0 };
        var items = new List<NestItem>
        {
            new() { Drawing = MakeRectDrawing(30, 30), Quantity = 1 }
        };

        var result = PlateOptimizer.Optimize(items, options, 0.0, templatePlate);

        Assert.NotNull(result);
        Assert.Equal(40, result.ChosenSize.Width);
    }

    [Fact]
    public void ReturnsNullWhenNoPlatesFit()
    {
        var options = new List<PlateOption>
        {
            new() { Width = 10, Length = 10, Cost = 50 },
        };

        var templatePlate = new Plate(10, 10) { PartSpacing = 0 };
        var items = new List<NestItem>
        {
            new() { Drawing = MakeRectDrawing(20, 20), Quantity = 1 }
        };

        var result = PlateOptimizer.Optimize(items, options, 0.0, templatePlate);

        Assert.Null(result);
    }
}

using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

/// <summary>
/// Phase 3.2: MultiPlateNester and PlateOptimizer take an explicit placement strategy at their
/// top-level call boundary instead of consulting process-global selection state.
/// </summary>
public class ExplicitPlacementStrategyTests
{
    private static Drawing MakeRectDrawing(double w, double h, string name = "rect")
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing(name, pgm);
    }

    private static NestItem MakeItem(string name, double w, double h, int qty) =>
        new() { Drawing = MakeRectDrawing(w, h, name), Quantity = qty };

    private static MultiPlateNestOptions MakeOptions()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing();
        return new MultiPlateNestOptions { Template = template };
    }

    [Fact]
    public void MultiPlateNester_UnsetStrategy_MatchesExplicitDefault()
    {
        var unsetOptions = MakeOptions();
        var explicitOptions = MakeOptions();
        explicitOptions.Strategy = "Default";
        Assert.Null(unsetOptions.Strategy);

        var unsetResult = MultiPlateNester.Nest(
            new List<NestItem> { MakeItem("a", 20, 10, 3), MakeItem("b", 12, 8, 4) },
            unsetOptions
        );
        var explicitResult = MultiPlateNester.Nest(
            new List<NestItem> { MakeItem("a", 20, 10, 3), MakeItem("b", 12, 8, 4) },
            explicitOptions
        );

        Assert.NotEmpty(unsetResult.Plates);
        Assert.Equal(
            unsetResult.Plates.Sum(p => p.Parts.Count),
            explicitResult.Plates.Sum(p => p.Parts.Count)
        );
    }

    [Theory]
    [InlineData("Mystery Engine")]
    [InlineData("StockLadder")]
    public void MultiPlateNester_RejectsUnknownStrategy(string strategy)
    {
        var options = MakeOptions();
        options.Strategy = strategy;

        Assert.Throws<NotSupportedException>(() =>
            MultiPlateNester.Nest(new List<NestItem> { MakeItem("a", 20, 10, 1) }, options)
        );
    }

    [Fact]
    public void MultiPlateNester_HonorsExplicitStripStrategy()
    {
        var options = MakeOptions();
        options.Strategy = "Strip";

        var result = MultiPlateNester.Nest(
            new List<NestItem> { MakeItem("a", 20, 10, 3), MakeItem("b", 12, 8, 4) },
            options
        );

        Assert.NotEmpty(result.Plates);
        Assert.Equal(0, result.UnplacedItems.Count);
    }

    [Fact]
    public void PlateOptimizer_UnsetStrategy_MatchesExplicitDefault()
    {
        var options = new List<PlateOption>
        {
            new() { Width = 20, Length = 20, Cost = 100 },
            new() { Width = 40, Length = 40, Cost = 400 },
        };
        var templatePlate = new Plate(40, 40) { PartSpacing = 0 };
        var items = new List<NestItem> { MakeItem("rect", 10, 10, 1) };

        var defaulted = PlateOptimizer.Optimize(items, options, 0.0, templatePlate);
        var explicitDefault = PlateOptimizer.Optimize(
            items,
            options,
            0.0,
            templatePlate,
            strategy: "Default"
        );

        Assert.NotNull(defaulted);
        Assert.NotNull(explicitDefault);
        Assert.Equal(defaulted.ChosenSize.Width, explicitDefault.ChosenSize.Width);
        Assert.Equal(defaulted.Parts.Count, explicitDefault.Parts.Count);
    }

    [Theory]
    [InlineData("Mystery Engine")]
    [InlineData("StockLadder")]
    public void PlateOptimizer_RejectsUnknownStrategy(string strategy)
    {
        var options = new List<PlateOption> { new() { Width = 20, Length = 20, Cost = 100 } };
        var templatePlate = new Plate(40, 40) { PartSpacing = 0 };
        var items = new List<NestItem> { MakeItem("rect", 10, 10, 1) };

        Assert.Throws<NotSupportedException>(() =>
            PlateOptimizer.Optimize(items, options, 0.0, templatePlate, strategy: strategy)
        );
    }

    [Fact]
    public void PlateFillService_Nest_ResolvesNamedStrategyAndRejectsUnknown()
    {
        var plate = new Plate(new Size(60, 80));
        var drawing = MakeRectDrawing(6, 4, "part");

        var parts = PlateFillService.Nest(
            "Vertical Remnant",
            plate,
            new List<NestItem> { new() { Drawing = drawing, Quantity = 4 } },
            null,
            CancellationToken.None
        );

        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Same(drawing, part.BaseDrawing));
        Assert.Empty(plate.Parts);

        Assert.Throws<NotSupportedException>(() =>
            PlateFillService.Nest(
                "Mystery Engine",
                plate,
                new List<NestItem> { new() { Drawing = drawing, Quantity = 1 } },
                null,
                CancellationToken.None
            )
        );
    }
}

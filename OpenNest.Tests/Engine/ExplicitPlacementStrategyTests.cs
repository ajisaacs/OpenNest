using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

/// <summary>
/// Callers name the placement strategy explicitly at the <see cref="PlateFillService"/>
/// boundary instead of consulting process-global selection state.
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveStrategy_UnsetMeansDefault(string strategy)
    {
        Assert.Equal("Default", PlateFillService.ResolveStrategy(strategy));
    }

    [Theory]
    [InlineData("strip", "Strip")]
    [InlineData("VERTICAL REMNANT", "Vertical Remnant")]
    public void ResolveStrategy_ReturnsCanonicalBuiltInName(string strategy, string expected)
    {
        Assert.Equal(expected, PlateFillService.ResolveStrategy(strategy));
    }

    [Theory]
    [InlineData("Mystery Engine")]
    [InlineData("StockLadder")]
    public void ResolveStrategy_RejectsUnknownStrategy(string strategy)
    {
        Assert.Throws<NotSupportedException>(() => PlateFillService.ResolveStrategy(strategy));
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

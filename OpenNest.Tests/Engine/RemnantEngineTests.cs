using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

public class RemnantEngineTests
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
    public void VerticalRemnantPlateFiller_UsesHorizontalPreferredDirection()
    {
        var plate = new Plate(60, 120);
        var filler = new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical);
        Assert.Equal(NestDirection.Horizontal, filler.PreferredDirection);
    }

    [Fact]
    public void HorizontalRemnantPlateFiller_UsesVerticalPreferredDirection()
    {
        var plate = new Plate(60, 120);
        var filler = new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal);
        Assert.Equal(NestDirection.Vertical, filler.PreferredDirection);
    }

    [Fact]
    public void VerticalRemnantPlateFiller_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "VerticalRemnantPlateFiller should fill parts");
    }

    [Fact]
    public void HorizontalRemnantPlateFiller_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "HorizontalRemnantPlateFiller should fill parts");
    }

    [Fact]
    public void PlateFillService_ListsBothRemnantStrategies()
    {
        Assert.Contains("Vertical Remnant", PlateFillService.BuiltInStrategies);
        Assert.Contains("Horizontal Remnant", PlateFillService.BuiltInStrategies);
    }

    [Fact]
    public void VerticalRemnantPlateFiller_ProducesTighterXExtent_ThanDefault()
    {
        var plate = new Plate(60, 120);
        var drawing = MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };

        var defaultFiller = new DefaultPlateFiller(plate);
        var remnantFiller = new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical);

        var defaultParts = defaultFiller.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );
        var remnantParts = remnantFiller.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(defaultParts.Count > 0);
        Assert.True(remnantParts.Count > 0);

        var defaultXExtent =
            defaultParts.Max(p => p.BoundingBox.Right) - defaultParts.Min(p => p.BoundingBox.Left);
        var remnantXExtent =
            remnantParts.Max(p => p.BoundingBox.Right) - remnantParts.Min(p => p.BoundingBox.Left);

        Assert.True(
            remnantXExtent <= defaultXExtent + 0.01,
            $"Remnant X-extent ({remnantXExtent:F1}) should be <= default filler ({defaultXExtent:F1})"
        );
    }
}

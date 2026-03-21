using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

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
    public void VerticalRemnantEngine_UsesVerticalRemnantComparer()
    {
        var plate = new Plate(60, 120);
        var engine = new VerticalRemnantEngine(plate);
        Assert.Equal("Vertical Remnant", engine.Name);
        Assert.Equal(NestDirection.Horizontal, engine.PreferredDirection);
    }

    [Fact]
    public void HorizontalRemnantEngine_UsesHorizontalRemnantComparer()
    {
        var plate = new Plate(60, 120);
        var engine = new HorizontalRemnantEngine(plate);
        Assert.Equal("Horizontal Remnant", engine.Name);
        Assert.Equal(NestDirection.Vertical, engine.PreferredDirection);
    }

    [Fact]
    public void VerticalRemnantEngine_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var engine = new VerticalRemnantEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "VerticalRemnantEngine should fill parts");
    }

    [Fact]
    public void HorizontalRemnantEngine_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var engine = new HorizontalRemnantEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "HorizontalRemnantEngine should fill parts");
    }

    [Fact]
    public void Registry_ContainsBothRemnantEngines()
    {
        var names = NestEngineRegistry.AvailableEngines.Select(e => e.Name).ToList();
        Assert.Contains("Vertical Remnant", names);
        Assert.Contains("Horizontal Remnant", names);
    }

    [Fact]
    public void VerticalRemnantEngine_ProducesTighterXExtent_ThanDefault()
    {
        var plate = new Plate(60, 120);
        var drawing = MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };

        var defaultEngine = new DefaultNestEngine(plate);
        var remnantEngine = new VerticalRemnantEngine(plate);

        var defaultParts = defaultEngine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);
        var remnantParts = remnantEngine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(defaultParts.Count > 0);
        Assert.True(remnantParts.Count > 0);

        var defaultXExtent = defaultParts.Max(p => p.BoundingBox.Right) - defaultParts.Min(p => p.BoundingBox.Left);
        var remnantXExtent = remnantParts.Max(p => p.BoundingBox.Right) - remnantParts.Min(p => p.BoundingBox.Left);

        Assert.True(remnantXExtent <= defaultXExtent + 0.01,
            $"Remnant X-extent ({remnantXExtent:F1}) should be <= default ({defaultXExtent:F1})");
    }
}

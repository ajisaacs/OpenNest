using OpenNest.Geometry;

namespace OpenNest.Tests;

public class EngineRefactorSmokeTests
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
    public void DefaultEngine_FillNestItem_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "DefaultNestEngine should fill parts");
    }

    [Fact]
    public void DefaultEngine_FillGroupParts_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var drawing = MakeRectDrawing(20, 10);
        var groupParts = new List<Part> { new Part(drawing) };

        var parts = engine.Fill(groupParts, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "DefaultNestEngine group fill should produce parts");
    }

    [Fact]
    public void DefaultEngine_ForceFullAngleSweep_StillWorks()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        engine.ForceFullAngleSweep = true;
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "ForceFullAngleSweep should still produce results");
    }

    [Fact]
    public void StripEngine_Nest_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new StripNestEngine(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "large"), Quantity = 10 },
            new NestItem { Drawing = MakeRectDrawing(8, 5, "small"), Quantity = 5 },
        };

        var parts = engine.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "StripNestEngine should nest parts");
    }

    [Fact]
    public void DefaultEngine_Nest_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "a"), Quantity = 5 },
            new NestItem { Drawing = MakeRectDrawing(15, 8, "b"), Quantity = 3 },
        };

        var parts = engine.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "Base Nest method should place parts");
    }

    [Fact]
    public void BruteForceRunner_StillWorks()
    {
        var plate = new Plate(120, 60);
        var drawing = MakeRectDrawing(20, 10);

        var result = OpenNest.Engine.ML.BruteForceRunner.Run(drawing, plate, forceFullAngleSweep: true);

        Assert.NotNull(result);
        Assert.True(result.PartCount > 0);
    }
}

using OpenNest.Geometry;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement.Fillers;

namespace OpenNest.Tests.Engine;

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
    public void DefaultPlateFiller_FillNestItem_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "DefaultPlateFiller should fill parts");
    }

    [Fact]
    public void DefaultPlateFiller_FillGroupParts_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        var drawing = MakeRectDrawing(20, 10);
        var groupParts = new List<Part> { new Part(drawing) };

        var parts = filler.Fill(
            groupParts,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "DefaultPlateFiller group fill should produce parts");
    }

    [Fact]
    public void DefaultPlateFiller_ForceFullAngleSweep_StillWorks()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        filler.ForceFullAngleSweep = true;
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "ForceFullAngleSweep should still produce results");
    }

    [Fact]
    public void StripPlateFiller_Nest_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new StripPlateFiller(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "large"), Quantity = 10 },
            new NestItem { Drawing = MakeRectDrawing(8, 5, "small"), Quantity = 5 },
        };

        var parts = filler.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "StripPlateFiller should nest parts");
    }

    [Fact]
    public void DefaultPlateFiller_Nest_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "a"), Quantity = 5 },
            new NestItem { Drawing = MakeRectDrawing(15, 8, "b"), Quantity = 3 },
        };

        var parts = filler.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "Base Nest method should place parts");
    }

    [Fact]
    public void BruteForceRunner_StillWorks()
    {
        var plate = new Plate(60, 120);
        var drawing = MakeRectDrawing(20, 10);

        var result = OpenNest.Engine.ML.BruteForceRunner.Run(
            drawing,
            plate,
            forceFullAngleSweep: true
        );

        Assert.NotNull(result);
        Assert.True(result.PartCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.WinnerEngine));
        Assert.NotEmpty(result.AngleResults);
    }
}

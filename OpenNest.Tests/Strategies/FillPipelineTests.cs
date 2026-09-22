using OpenNest.Engine.Strategies;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;
using OpenNest.Engine;

namespace OpenNest.Tests.Strategies;

public class FillPipelineTests
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
    public void Pipeline_PopulatesPhaseResults()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        filler.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(
            filler.PhaseResults.Count >= FillStrategyRegistry.Strategies.Count,
            $"Expected phase results from all active strategies, got {filler.PhaseResults.Count}"
        );
    }

    [Fact]
    public void Pipeline_SetsWinnerPhase()
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

        Assert.True(parts.Count > 0);
        Assert.True(
            filler.WinnerPhase == NestPhase.Pairs
                || filler.WinnerPhase == NestPhase.Linear
                || filler.WinnerPhase == NestPhase.RectBestFit
                || filler.WinnerPhase == NestPhase.Extents
                || filler.WinnerPhase == NestPhase.Custom
        );
    }

    [Fact]
    public void Pipeline_RespectsCancellation()
    {
        var plate = new Plate(60, 120);
        var filler = new DefaultPlateFiller(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        // Pre-cancelled token should return empty or partial results without throwing
        var parts = filler.Fill(item, plate.WorkArea(), null, cts.Token);

        // Should not throw — graceful degradation
        Assert.NotNull(parts);
    }
}

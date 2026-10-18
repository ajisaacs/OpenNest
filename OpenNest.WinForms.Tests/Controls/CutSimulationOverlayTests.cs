using System.Drawing;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class CutSimulationOverlayTests
{
    private static readonly CutParameters Parameters = new()
    {
        Feedrate = 100,
        RapidTravelRate = 600,
        PierceTime = TimeSpan.FromSeconds(1),
        Units = Units.Inches,
    };

    private static Program Square(double size)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(size, 0));
        program.Codes.Add(new LinearMove(size, size));
        program.Codes.Add(new LinearMove(0, size));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    private static Plate TwoSquares()
    {
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(new Drawing("a", Square(10)), new Vector(5, 5)));
        plate.Parts.Add(new Part(new Drawing("b", Square(10)), new Vector(25, 5)));
        return plate;
    }

    [Fact]
    public void PartsAheadOfTheTorchArePending() => RunSta(() =>
    {
        using var view = new PlateView { Plate = TwoSquares() };
        var overlay = view.CutSimulation;
        var sim = CutSimulation.Build(view.Plate, Parameters);
        overlay.Load(sim);

        Assert.True(overlay.IsPartPending(0)); // still travelling to the first part
        var firstCut = sim.Steps.First(s => s.Kind == CutSimulationStepKind.Cut);
        overlay.Time = firstCut.StartTime + firstCut.Duration / 2;
        Assert.False(overlay.IsPartPending(0));
        Assert.True(overlay.IsPartPending(1));

        overlay.Time = sim.TotalTime;
        Assert.False(overlay.IsPartPending(1));
    });

    [Fact]
    public void FinishedCutsAreDrawnOnlyBehindThePlayhead() => RunSta(() =>
    {
        using var view = new PlateView { Plate = TwoSquares() };
        var sim = CutSimulation.Build(view.Plate, Parameters);
        view.CutSimulation.Load(sim);
        var secondPartCut = sim.Steps.First(s => s.PartIndex == 1 && s.Kind == CutSimulationStepKind.Cut);
        view.CutSimulation.Time = secondPartCut.StartTime;

        using var bitmap = new Bitmap(400, 400);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TranslateTransform(20, 380);
        graphics.Clear(Color.Black);
        view.CutSimulation.Draw(graphics);

        // Bottom edge midpoints of each square, in graph coordinates.
        var cut = Pixel(view, bitmap, new Vector(10, 5));
        var uncut = Pixel(view, bitmap, new Vector(35, 15));
        Assert.True(cut.R > 200 && cut.G < 160, $"first part trail missing: {cut}");
        Assert.Equal(Color.Black.ToArgb(), uncut.ToArgb());
    });

    [Fact]
    public void PlateSwitchClearsTheSimulation() => RunSta(() =>
    {
        using var view = new PlateView { Plate = TwoSquares() };
        view.CutSimulation.Load(CutSimulation.Build(view.Plate, Parameters));
        view.CutSimulation.Play();
        Assert.True(view.CutSimulation.IsPlaying);

        view.Plate = new Plate();

        Assert.False(view.CutSimulation.IsActive);
        Assert.False(view.CutSimulation.IsPlaying);
    });

    private static Color Pixel(PlateView view, Bitmap bitmap, Vector world)
    {
        var point = view.PointWorldToGraph(world);
        return bitmap.GetPixel((int)System.Math.Round(point.X + 20), (int)System.Math.Round(point.Y + 380));
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "Cut simulation overlay test timed out.");
}

using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class AngleCandidateBuilderTests
{
    private static Drawing MakeRectDrawing(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    [Fact]
    public void Build_ReturnsAtLeastTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, 0, workArea);

        Assert.True(angles.Count >= 2);
    }

    [Fact]
    public void Build_NarrowWorkArea_UsesBaseAnglesOnly()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var narrowArea = new Box(0, 0, 100, 8); // narrower than part's longest side

        var angles = builder.Build(item, 0, narrowArea);

        // Without ForceFullSweep, narrow areas use only base angles (0° and 90°)
        Assert.Equal(2, angles.Count);
    }

    [Fact]
    public void ForceFullSweep_ProducesFullSweep()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(5, 5) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, 0, workArea);

        // Full sweep at 5deg steps = ~36 angles (0 to 175), plus base angles
        Assert.True(angles.Count > 10);
    }

    [Fact]
    public void RecordProductive_PrunesSubsequentBuilds()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 8);

        // First build — full sweep
        var firstAngles = builder.Build(item, 0, workArea);

        // Record some as productive
        var productive = new List<AngleResult>
        {
            new AngleResult { AngleDeg = 0, PartCount = 5 },
            new AngleResult { AngleDeg = 45, PartCount = 3 },
        };
        builder.RecordProductive(productive);

        // Second build — should be pruned to known-good + base angles
        builder.ForceFullSweep = false;
        var secondAngles = builder.Build(item, 0, workArea);

        Assert.True(secondAngles.Count < firstAngles.Count,
            $"Pruned ({secondAngles.Count}) should be fewer than full ({firstAngles.Count})");
    }
}

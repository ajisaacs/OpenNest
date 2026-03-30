using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

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

    private static ClassificationResult MakeClassification(double primaryAngle = 0, PartType type = PartType.Irregular)
        => new ClassificationResult { PrimaryAngle = primaryAngle, Type = type };

    [Fact]
    public void Build_ReturnsAtLeastTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, MakeClassification(), workArea);

        Assert.True(angles.Count >= 2);
    }

    [Fact]
    public void Build_RectangleType_NarrowWorkArea_UsesBaseAnglesOnly()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var narrowArea = new Box(0, 0, 100, 8); // narrower than part's longest side

        var angles = builder.Build(item, MakeClassification(0, PartType.Rectangle), narrowArea);

        // Rectangle classification always returns exactly 2 angles regardless of work area
        Assert.Equal(2, angles.Count);
    }

    [Fact]
    public void ForceFullSweep_ProducesFullSweep()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(5, 5) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, MakeClassification(), workArea);

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
        var firstAngles = builder.Build(item, MakeClassification(), workArea);

        // Record some as productive
        var productive = new List<AngleResult>
        {
            new AngleResult { AngleDeg = 0, PartCount = 5 },
            new AngleResult { AngleDeg = 45, PartCount = 3 },
        };
        builder.RecordProductive(productive);

        // Second build — should be pruned to known-good + base angles
        builder.ForceFullSweep = false;
        var secondAngles = builder.Build(item, MakeClassification(), workArea);

        Assert.True(secondAngles.Count < firstAngles.Count,
            $"Pruned ({secondAngles.Count}) should be fewer than full ({firstAngles.Count})");
    }

    [Fact]
    public void Build_RectanglePart_ReturnsTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);
        var classification = MakeClassification(0, PartType.Rectangle);

        var angles = builder.Build(item, classification, workArea);

        Assert.Equal(2, angles.Count);
    }

    [Fact]
    public void Build_CirclePart_ReturnsOneAngle()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(10, 10) };
        var workArea = new Box(0, 0, 100, 100);
        var classification = MakeClassification(0, PartType.Circle);

        var angles = builder.Build(item, classification, workArea);

        Assert.Single(angles);
        Assert.Equal(0, angles[0]);
    }

    [Fact]
    public void Build_UserConstraints_OverrideRectangleClassification()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(100, 50),
            RotationStart = Angle.ToRadians(10),
            RotationEnd = Angle.ToRadians(90),
            StepAngle = Angle.ToRadians(10),
        };
        var classification = MakeClassification(0, PartType.Rectangle);
        var workArea = new Box(0, 0, 1000, 500);

        var angles = builder.Build(item, classification, workArea);

        Assert.True(angles.Count > 2,
            $"User constraints should override rect classification, got {angles.Count} angles");
    }

    [Fact]
    public void Build_UserConstraints_StartingAtZero_AreRespected()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(100, 50),
            RotationStart = 0,
            RotationEnd = System.Math.PI,
            StepAngle = Angle.ToRadians(45),
        };
        var classification = MakeClassification(0, PartType.Rectangle);
        var workArea = new Box(0, 0, 1000, 500);

        var angles = builder.Build(item, classification, workArea);

        // Start=0, End=PI is NOT "no constraints" — it's a real 0-180 range
        Assert.True(angles.Count > 2,
            $"0-to-PI constraint should produce multiple angles, got {angles.Count}");
    }
}

using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using System.Linq;

namespace OpenNest.Tests.CuttingStrategy;

public class HoleSubProgramTests
{
    [Fact]
    public void SubProgramCall_Offset_DefaultsToZero()
    {
        var call = new SubProgramCall();
        Assert.Equal(0, call.Offset.X);
        Assert.Equal(0, call.Offset.Y);
    }

    [Fact]
    public void SubProgramCall_Offset_StoresValue()
    {
        var call = new SubProgramCall { Offset = new Vector(1.5, 2.5) };
        Assert.Equal(1.5, call.Offset.X);
        Assert.Equal(2.5, call.Offset.Y);
    }

    [Fact]
    public void SubProgramCall_Clone_CopiesOffset()
    {
        var call = new SubProgramCall { Id = 1, Offset = new Vector(3, 4) };
        var clone = (SubProgramCall)call.Clone();
        Assert.Equal(3, clone.Offset.X);
        Assert.Equal(4, clone.Offset.Y);
        Assert.Equal(1, clone.Id);
    }

    [Fact]
    public void SubProgramCall_ToString_IncludesOffset()
    {
        var call = new SubProgramCall { Id = 1000, Offset = new Vector(1.5, 2.5) };
        var str = call.ToString();
        Assert.Contains("P1000", str);
        Assert.Contains("X1.5", str);
        Assert.Contains("Y2.5", str);
    }

    [Fact]
    public void Program_SubPrograms_EmptyByDefault()
    {
        var pgm = new Program();
        Assert.NotNull(pgm.SubPrograms);
        Assert.Empty(pgm.SubPrograms);
    }

    [Fact]
    public void Program_SubPrograms_StoresAndRetrieves()
    {
        var pgm = new Program();
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(0.1, 0.2));

        pgm.SubPrograms[1] = sub;

        Assert.Single(pgm.SubPrograms);
        Assert.Same(sub, pgm.SubPrograms[1]);
    }

    [Fact]
    public void Program_Clone_DeepCopiesSubPrograms()
    {
        var pgm = new Program();
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(0.1, 0.2));
        pgm.SubPrograms[1] = sub;

        var clone = (Program)pgm.Clone();

        Assert.Single(clone.SubPrograms);
        Assert.NotSame(sub, clone.SubPrograms[1]);
        Assert.Equal(Mode.Incremental, clone.SubPrograms[1].Mode);
    }

    [Fact]
    public void Apply_CircleHole_EmitsSubProgramCall()
    {
        // Create a program with a square perimeter and a circle hole at (5, 5) radius 0.5
        var pgm = new Program(Mode.Absolute);
        // Square perimeter
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(0, 0));
        // Circle hole at (5, 5) radius 0.5
        pgm.Codes.Add(new RapidMove(5.5, 5));
        pgm.Codes.Add(new ArcMove(new Vector(5.5, 5), new Vector(5, 5), RotationType.CW));

        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
                ArcCircleLeadOut = new NoLeadOut()
            }
        };

        var result = strategy.Apply(pgm, new Vector(10, 10));

        // Should contain at least one SubProgramCall
        var calls = result.Program.Codes.OfType<SubProgramCall>().ToList();
        Assert.Single(calls);

        // The call's offset should be approximately at the hole center (5, 5)
        var call = calls[0];
        Assert.Equal(5, call.Offset.X, 1);
        Assert.Equal(5, call.Offset.Y, 1);

        // The parent program should have a sub-program registered
        Assert.True(result.Program.SubPrograms.ContainsKey(call.Id));
    }

    [Fact]
    public void Apply_TwoIdenticalCircles_ShareSubProgram()
    {
        // Square perimeter with two identical circle holes at different positions
        var pgm = new Program(Mode.Absolute);
        // Square perimeter
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(0, 0));
        // Circle 1 at (2, 2) radius 0.5
        pgm.Codes.Add(new RapidMove(2.5, 2));
        pgm.Codes.Add(new ArcMove(new Vector(2.5, 2), new Vector(2, 2), RotationType.CW));
        // Circle 2 at (6, 6) radius 0.5
        pgm.Codes.Add(new RapidMove(6.5, 6));
        pgm.Codes.Add(new ArcMove(new Vector(6.5, 6), new Vector(6, 6), RotationType.CW));

        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                RoundLeadInAngles = true,
                LeadInAngleIncrement = 5.0,
                ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
                ArcCircleLeadOut = new NoLeadOut()
            }
        };

        var result = strategy.Apply(pgm, new Vector(10, 10));

        var calls = result.Program.Codes.OfType<SubProgramCall>().ToList();
        Assert.Equal(2, calls.Count);

        // Both calls should reference the same sub-program ID (same radius, same quantized angle)
        Assert.Equal(calls[0].Id, calls[1].Id);

        // But different offsets
        Assert.NotEqual(calls[0].Offset.X, calls[1].Offset.X);
    }
}

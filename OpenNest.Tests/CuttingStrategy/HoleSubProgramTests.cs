using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
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
    public void SubProgramCall_ToString_IncludesOffsetAndRotation()
    {
        var call = new SubProgramCall { Id = 1000, Offset = new Vector(1.5, 2.5), Rotation = 30 };
        var str = call.ToString();
        Assert.Contains("P1000", str);
        Assert.Contains("X1.5", str);
        Assert.Contains("Y2.5", str);
        Assert.Contains("R30", str);
    }

    [Fact]
    public void SubProgramCall_ToString_OmitsZeroFields()
    {
        var call = new SubProgramCall { Id = 1000 };
        var str = call.ToString();
        Assert.Equal("G65 P1000", str);
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

    [Fact]
    public void Apply_HoleCenters_PreservedInGeometry()
    {
        // Square perimeter 10x10 with two circle holes at known positions
        var holeCenter1 = new Vector(3, 3);
        var holeCenter2 = new Vector(7, 5);
        var holeRadius = 0.5;

        var pgm = new Program(Mode.Absolute);
        // Perimeter
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        // Hole 1 at (3, 3)
        pgm.Codes.Add(new RapidMove(holeCenter1.X + holeRadius, holeCenter1.Y));
        pgm.Codes.Add(new ArcMove(
            new Vector(holeCenter1.X + holeRadius, holeCenter1.Y),
            holeCenter1, RotationType.CW));
        // Hole 2 at (7, 5)
        pgm.Codes.Add(new RapidMove(holeCenter2.X + holeRadius, holeCenter2.Y));
        pgm.Codes.Add(new ArcMove(
            new Vector(holeCenter2.X + holeRadius, holeCenter2.Y),
            holeCenter2, RotationType.CW));

        var strategy = new ContourCuttingStrategy
        {
            Parameters = new CuttingParameters
            {
                ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
                ArcCircleLeadOut = new NoLeadOut()
            }
        };

        var result = strategy.Apply(pgm, new Vector(10, 10));

        // Convert to geometry — this is what PlateView renders
        var geometry = ConvertProgram.ToGeometry(result.Program);
        var circles = geometry.OfType<Circle>().ToList();

        Assert.Equal(2, circles.Count);

        // Circle centers must match the original hole positions
        var center1 = circles[0].Center;
        var center2 = circles[1].Center;

        Assert.Equal(holeCenter1.X, center1.X, 2);
        Assert.Equal(holeCenter1.Y, center1.Y, 2);
        Assert.Equal(holeCenter2.X, center2.X, 2);
        Assert.Equal(holeCenter2.Y, center2.Y, 2);
    }

    [Fact]
    public void Part_ApplyLeadIns_HolesAndPerimeter_CorrectPositions()
    {
        // Build a drawing with a square and two holes
        var holeCenter1 = new Vector(3, 3);
        var holeCenter2 = new Vector(7, 5);
        var holeRadius = 0.5;

        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        pgm.Codes.Add(new RapidMove(holeCenter1.X + holeRadius, holeCenter1.Y));
        pgm.Codes.Add(new ArcMove(
            new Vector(holeCenter1.X + holeRadius, holeCenter1.Y),
            holeCenter1, RotationType.CW));
        pgm.Codes.Add(new RapidMove(holeCenter2.X + holeRadius, holeCenter2.Y));
        pgm.Codes.Add(new ArcMove(
            new Vector(holeCenter2.X + holeRadius, holeCenter2.Y),
            holeCenter2, RotationType.CW));

        var drawing = new Drawing("TestPart") { Program = pgm };
        var part = new Part(drawing);

        var parameters = new CuttingParameters
        {
            RoundLeadInAngles = true,
            LeadInAngleIncrement = 5.0,
            ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
            ArcCircleLeadOut = new NoLeadOut(),
            ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
            ExternalLeadOut = new NoLeadOut()
        };

        part.ApplyLeadIns(parameters, new Vector(10, 10));

        // Convert to geometry — this is what PlateView renders
        var geometry = ConvertProgram.ToGeometry(part.Program);
        var circles = geometry.OfType<Circle>().ToList();
        var lines = geometry.OfType<Line>().Where(l => l.Layer != SpecialLayers.Rapid).ToList();

        // Hole circles must be at correct positions
        Assert.Equal(2, circles.Count);
        Assert.Equal(holeCenter1.X, circles[0].Center.X, 2);
        Assert.Equal(holeCenter1.Y, circles[0].Center.Y, 2);
        Assert.Equal(holeCenter2.X, circles[1].Center.X, 2);
        Assert.Equal(holeCenter2.Y, circles[1].Center.Y, 2);
        Assert.Equal(holeRadius, circles[0].Radius, 2);
        Assert.Equal(holeRadius, circles[1].Radius, 2);

        // Perimeter lines must stay within the original 10x10 bounding box.
        // This catches the mode conversion bug where perimeter gets shifted
        // by the last hole's position.
        foreach (var line in lines)
        {
            Assert.True(line.StartPoint.X >= -1 && line.StartPoint.X <= 11,
                $"Perimeter line start X={line.StartPoint.X} is outside the 10x10 part bounds");
            Assert.True(line.StartPoint.Y >= -1 && line.StartPoint.Y <= 11,
                $"Perimeter line start Y={line.StartPoint.Y} is outside the 10x10 part bounds");
            Assert.True(line.EndPoint.X >= -1 && line.EndPoint.X <= 11,
                $"Perimeter line end X={line.EndPoint.X} is outside the 10x10 part bounds");
            Assert.True(line.EndPoint.Y >= -1 && line.EndPoint.Y <= 11,
                $"Perimeter line end Y={line.EndPoint.Y} is outside the 10x10 part bounds");
        }
    }

    [Fact]
    public void Program_BoundingBox_IncludesSubProgramOffset()
    {
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(1, 0));

        var main = new Program(Mode.Absolute);
        main.SubPrograms[1] = sub;
        main.Codes.Add(new SubProgramCall { Id = 1, Program = sub, Offset = new Vector(10, 20) });

        var box = main.BoundingBox();

        // Sub-program line goes from (10,20) to (11,20)
        Assert.True(box.Right >= 11);
        Assert.True(box.Top >= 20);
    }

    [Fact]
    public void Program_Rotate_RotatesSubProgramCallOffsets()
    {
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(1, 0));

        var main = new Program(Mode.Absolute);
        main.SubPrograms[1] = sub;
        main.Codes.Add(new SubProgramCall { Id = 1, Program = sub, Offset = new Vector(10, 0) });

        // Rotate 90 degrees CCW around origin
        main.Rotate(System.Math.PI / 2);

        var call = main.Codes.OfType<SubProgramCall>().First();
        // (10, 0) rotated 90 CCW = (0, 10)
        Assert.Equal(0, call.Offset.X, 1);
        Assert.Equal(10, call.Offset.Y, 1);
    }
}

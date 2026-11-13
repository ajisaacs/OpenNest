using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// <see cref="Part.CloneAtOffset"/> shares one program between the source and its copies. No
/// part may turn that program in place while another part still reads it.
/// </summary>
public class PartProgramSharingTests
{
    private const double QuarterTurn = System.Math.PI / 2;

    private static Part MakeSource()
    {
        // 4 x 1, so a quarter turn changes the bounds.
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 1)));
        pgm.Codes.Add(new LinearMove(new Vector(4, 1)));
        pgm.Codes.Add(new LinearMove(new Vector(4, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Part(new Drawing("test", pgm), new Vector(2, 3));
    }

    private static double[] Bounds(Part part) =>
        [part.BoundingBox.X, part.BoundingBox.Y, part.BoundingBox.Length, part.BoundingBox.Width];

    private static void AssertUntouched(Part part, Program expectedProgram, double expectedRotation,
        double[] expectedBounds)
    {
        Assert.True(ProgramContent.Equal(part.Program, expectedProgram));
        Assert.Equal(expectedRotation, part.Rotation);
        Assert.Equal(expectedRotation, part.Program.Rotation);
        Assert.Equal(expectedBounds, Bounds(part));

        // The stored bounds must still be the bounds of the program the part holds.
        var actual = part.Program.BoundingBox();
        actual.Offset(part.Location);
        Assert.Equal(expectedBounds[0], actual.X, 9);
        Assert.Equal(expectedBounds[1], actual.Y, 9);
        Assert.Equal(expectedBounds[2], actual.Length, 9);
        Assert.Equal(expectedBounds[3], actual.Width, 9);
    }

    [Fact]
    public void RotatingTheSource_LeavesTheCopyUnchanged()
    {
        var source = MakeSource();
        var copy = source.CloneAtOffset(new Vector(10, 0));
        var program = (Program)copy.Program.Clone();
        var rotation = copy.Rotation;
        var bounds = Bounds(copy);

        source.Rotate(QuarterTurn);

        AssertUntouched(copy, program, rotation, bounds);
        Assert.NotSame(source.Program, copy.Program);
        Assert.Equal(QuarterTurn, source.Rotation, 9);
    }

    [Fact]
    public void RotatingTheSourceAroundAnOrigin_LeavesTheCopyUnchanged()
    {
        var source = MakeSource();
        var copy = source.CloneAtOffset(new Vector(10, 0));
        var program = (Program)copy.Program.Clone();
        var rotation = copy.Rotation;
        var bounds = Bounds(copy);

        source.Rotate(QuarterTurn, new Vector(5, 5));

        AssertUntouched(copy, program, rotation, bounds);
    }

    [Fact]
    public void RotatingTheCopy_LeavesTheSourceUnchanged()
    {
        var source = MakeSource();
        var copy = source.CloneAtOffset(new Vector(10, 0));
        var program = (Program)source.Program.Clone();
        var rotation = source.Rotation;
        var bounds = Bounds(source);

        copy.Rotate(QuarterTurn);

        AssertUntouched(source, program, rotation, bounds);
        Assert.NotSame(source.Program, copy.Program);
        Assert.Equal(QuarterTurn, copy.Rotation, 9);
    }

    [Fact]
    public void RotatingOneCopy_LeavesItsSiblingUnchanged()
    {
        var source = MakeSource();
        var first = source.CloneAtOffset(new Vector(10, 0));
        var second = first.CloneAtOffset(new Vector(10, 0));
        var program = (Program)second.Program.Clone();
        var rotation = second.Rotation;
        var bounds = Bounds(second);

        first.Rotate(QuarterTurn);
        source.Rotate(-QuarterTurn);

        AssertUntouched(second, program, rotation, bounds);
    }

    [Fact]
    public void UnrotatedCopies_ShareTheSourceProgram()
    {
        var source = MakeSource();
        var program = source.Program;

        var first = source.CloneAtOffset(new Vector(10, 0));
        var second = source.CloneAtOffset(new Vector(20, 0));
        var third = first.CloneAtOffset(new Vector(0, 10));
        first.Offset(1, 1);

        Assert.Same(program, source.Program);
        Assert.Same(program, first.Program);
        Assert.Same(program, second.Program);
        Assert.Same(program, third.Program);
        Assert.True(program.SharedByParts);
    }

    [Fact]
    public void CopiedProgram_StartsUnshared()
    {
        var source = MakeSource();
        source.CloneAtOffset(new Vector(10, 0));

        Assert.True(source.Program.SharedByParts);
        Assert.False(((Program)source.Program.Clone()).SharedByParts);
        Assert.False(source.Program.HeadCopy(2).SharedByParts);
        Assert.False(((Part)source.Clone()).Program.SharedByParts);

        // The part that turned gets a private program; turning it again copies nothing.
        source.Rotate(QuarterTurn);
        var program = source.Program;
        Assert.False(program.SharedByParts);
        source.Rotate(QuarterTurn);
        Assert.Same(program, source.Program);
    }

    [Fact]
    public void UnsharedPart_RotatesItsProgramInPlace()
    {
        var source = MakeSource();
        var program = source.Program;

        source.Rotate(QuarterTurn);

        Assert.Same(program, source.Program);
    }

    [Fact]
    public void RestoredCuttingState_RoundTripsAndStillProtectsTheCopy()
    {
        var source = MakeSource();
        var state = source.CaptureCuttingState();
        var sourceProgram = (Program)source.Program.Clone();
        var sourceBounds = Bounds(source);

        var copy = source.CloneAtOffset(new Vector(10, 0));
        var copyProgram = (Program)copy.Program.Clone();
        var copyRotation = copy.Rotation;
        var copyBounds = Bounds(copy);

        source.Rotate(QuarterTurn);
        source.RestoreCuttingState(state);

        // Field-exact: the same record comes back, references included.
        Assert.Equal(state, source.CaptureCuttingState());
        Assert.Same(state.Program, source.Program);
        Assert.Same(state.BoundingBox, source.BoundingBox);
        Assert.True(ProgramContent.Equal(source.Program, sourceProgram));
        Assert.Equal(sourceBounds, Bounds(source));

        // The restore re-aliased the copy's program with an owning part.
        Assert.Same(source.Program, copy.Program);

        source.Rotate(QuarterTurn);

        AssertUntouched(copy, copyProgram, copyRotation, copyBounds);
        Assert.NotSame(source.Program, copy.Program);
    }

    [Fact]
    public void RotateAfterUpdate_KeepsTheProgramInstance()
    {
        var copy = MakeSource().CloneAtOffset(new Vector(10, 0));

        copy.Update();
        var program = copy.Program;
        copy.Rotate(QuarterTurn);

        Assert.Same(program, copy.Program);
    }

    [Fact]
    public void RotateAfterApplyLeadIns_KeepsTheProgramInstance()
    {
        var copy = MakeSource().CloneAtOffset(new Vector(10, 0));

        copy.ApplyLeadIns(new OpenNest.CNC.CuttingStrategy.CuttingParameters(), new Vector(-5, -5));
        var program = copy.Program;
        copy.Rotate(QuarterTurn);

        Assert.Same(program, copy.Program);
    }
}

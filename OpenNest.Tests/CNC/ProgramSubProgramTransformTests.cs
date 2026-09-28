using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

/// <summary>
/// Hole sub-programs are shared by reference: identical holes call one sub-program,
/// and program copies must not reach back into the source's sub-programs.
/// </summary>
public class ProgramSubProgramTransformTests
{
    private const double QuarterTurn = System.Math.PI / 2;

    /// <summary>Incremental hole sub-program: a lead-in from the hole centre to
    /// (1, 0), then a full radius-1 circle.</summary>
    private static Program MakeHoleSub()
    {
        var sub = new Program(Mode.Absolute);
        sub.Codes.Add(new LinearMove(new Vector(1, 0)) { Layer = LayerType.Leadin });
        sub.Codes.Add(new ArcMove(new Vector(1, 0), new Vector(0, 0), RotationType.CW));
        sub.Mode = Mode.Incremental;
        return sub;
    }

    /// <summary>Program with two identical holes, at (5, 5) and (15, 5), calling one
    /// shared sub-program, as <c>ContourCuttingStrategy</c> emits them.</summary>
    private static Program MakeTwoHoleProgram()
    {
        var sub = MakeHoleSub();
        var pgm = new Program(Mode.Absolute);
        pgm.SubPrograms[7] = sub;
        pgm.Codes.Add(new SubProgramCall { Id = 7, Program = sub, Offset = new Vector(5, 5) });
        pgm.Codes.Add(new SubProgramCall { Id = 7, Program = sub, Offset = new Vector(15, 5) });
        return pgm;
    }

    /// <summary>End points of the lead-in lines in the program's own frame.</summary>
    private static List<Vector> LeadInEnds(Program pgm) =>
        ConvertProgram
            .ToGeometry(pgm)
            .OfType<Line>()
            .Where(l => l.Layer == SpecialLayers.Leadin)
            .Select(l => l.EndPoint)
            .ToList();

    private static void AssertPoint(double x, double y, Vector actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
    }

    [Fact]
    public void Rotate_SharedSubProgram_TurnsEachHoleOnce()
    {
        var pgm = MakeTwoHoleProgram();

        pgm.Rotate(QuarterTurn);

        // (5, 5) -> (-5, 5) and (15, 5) -> (-5, 15); each lead-in ends one unit along
        // the rotated +X axis, i.e. at +Y from its centre.
        var ends = LeadInEnds(pgm);
        Assert.Equal(2, ends.Count);
        AssertPoint(-5, 6, ends[0]);
        AssertPoint(-5, 16, ends[1]);
    }

    [Fact]
    public void Clone_DoesNotRealignSourceSubProgram()
    {
        var pgm = MakeTwoHoleProgram();
        pgm.Rotate(QuarterTurn);
        var before = LeadInEnds(pgm);

        _ = pgm.Clone();

        var after = LeadInEnds(pgm);
        AssertPoint(before[0].X, before[0].Y, after[0]);
        AssertPoint(before[1].X, before[1].Y, after[1]);
    }

    [Fact]
    public void Clone_OwnsItsSubPrograms()
    {
        var pgm = MakeTwoHoleProgram();

        var copy = (Program)pgm.Clone();
        copy.Rotate(QuarterTurn);

        var sourceEnds = LeadInEnds(pgm);
        AssertPoint(6, 5, sourceEnds[0]);
        AssertPoint(16, 5, sourceEnds[1]);

        var copyEnds = LeadInEnds(copy);
        AssertPoint(-5, 6, copyEnds[0]);
        AssertPoint(-5, 16, copyEnds[1]);
    }

    [Fact]
    public void Clone_KeepsCallsAndDictionaryOnOneSharedCopy()
    {
        var pgm = MakeTwoHoleProgram();

        var copy = (Program)pgm.Clone();

        var calls = copy.Codes.OfType<SubProgramCall>().ToList();
        Assert.NotSame(pgm.SubPrograms[7], copy.SubPrograms[7]);
        Assert.Same(copy.SubPrograms[7], calls[0].Program);
        Assert.Same(copy.SubPrograms[7], calls[1].Program);
    }
}

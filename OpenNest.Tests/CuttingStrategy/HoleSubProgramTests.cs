using OpenNest.CNC;
using OpenNest.Geometry;

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
}

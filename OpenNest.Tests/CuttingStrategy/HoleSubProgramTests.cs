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
}

using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

public class ProgramVariableTests
{
    [Fact]
    public void LinearMove_VariableRefs_NullByDefault()
    {
        var move = new LinearMove(1.0, 2.0);
        Assert.Null(move.VariableRefs);
    }

    [Fact]
    public void LinearMove_Clone_CopiesVariableRefs()
    {
        var move = new LinearMove(1.0, 2.0);
        move.VariableRefs = new Dictionary<string, string> { { "X", "width" } };
        var clone = (LinearMove)move.Clone();
        Assert.NotSame(move.VariableRefs, clone.VariableRefs);
        Assert.Equal("width", clone.VariableRefs["X"]);
    }

    [Fact]
    public void LinearMove_Clone_NullRefs_StaysNull()
    {
        var move = new LinearMove(1.0, 2.0);
        var clone = (LinearMove)move.Clone();
        Assert.Null(clone.VariableRefs);
    }

    [Fact]
    public void ArcMove_Clone_CopiesVariableRefs()
    {
        var move = new ArcMove(1, 0, 0.5, 0);
        move.VariableRefs = new Dictionary<string, string> { { "I", "radius" } };
        var clone = (ArcMove)move.Clone();
        Assert.NotSame(move.VariableRefs, clone.VariableRefs);
        Assert.Equal("radius", clone.VariableRefs["I"]);
    }

    [Fact]
    public void RapidMove_Clone_CopiesVariableRefs()
    {
        var move = new RapidMove(5.0, 0);
        move.VariableRefs = new Dictionary<string, string> { { "X", "start_x" } };
        var clone = (RapidMove)move.Clone();
        Assert.NotSame(move.VariableRefs, clone.VariableRefs);
        Assert.Equal("start_x", clone.VariableRefs["X"]);
    }

    [Fact]
    public void Feedrate_VariableRef_NullByDefault()
    {
        var f = new Feedrate(100.0);
        Assert.Null(f.VariableRef);
    }

    [Fact]
    public void Feedrate_Clone_CopiesVariableRef()
    {
        var f = new Feedrate(100.0) { VariableRef = "cut_speed" };
        var clone = (Feedrate)f.Clone();
        Assert.Equal("cut_speed", clone.VariableRef);
    }

    [Fact]
    public void Motion_Rotate_ClearsVariableRefs()
    {
        var move = new LinearMove(1.0, 0);
        move.VariableRefs = new Dictionary<string, string> { { "X", "width" } };
        move.Rotate(System.Math.PI / 2);
        Assert.Null(move.VariableRefs);
    }

    [Fact]
    public void Motion_Offset_ClearsVariableRefs()
    {
        var move = new LinearMove(1.0, 0);
        move.VariableRefs = new Dictionary<string, string> { { "X", "width" } };
        move.Offset(5.0, 0);
        Assert.Null(move.VariableRefs);
    }

    [Fact]
    public void ArcMove_Rotate_ClearsVariableRefs()
    {
        var move = new ArcMove(1, 0, 0.5, 0);
        move.VariableRefs = new Dictionary<string, string> { { "I", "radius" } };
        move.Rotate(System.Math.PI / 2);
        Assert.Null(move.VariableRefs);
    }

    [Fact]
    public void ArcMove_Offset_ClearsVariableRefs()
    {
        var move = new ArcMove(1, 0, 0.5, 0);
        move.VariableRefs = new Dictionary<string, string> { { "I", "radius" } };
        move.Offset(5.0, 0);
        Assert.Null(move.VariableRefs);
    }
}

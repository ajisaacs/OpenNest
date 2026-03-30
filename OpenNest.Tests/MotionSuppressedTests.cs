using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class MotionSuppressedTests
{
    [Fact]
    public void LinearMove_Suppressed_DefaultsFalse()
    {
        var move = new LinearMove(new Vector(1, 2));
        Assert.False(move.Suppressed);
    }

    [Fact]
    public void ArcMove_Suppressed_DefaultsFalse()
    {
        var move = new ArcMove(new Vector(1, 2), new Vector(0, 0));
        Assert.False(move.Suppressed);
    }

    [Fact]
    public void RapidMove_Suppressed_DefaultsFalse()
    {
        var move = new RapidMove(new Vector(1, 2));
        Assert.False(move.Suppressed);
    }

    [Fact]
    public void Suppressed_CanBeSet()
    {
        var move = new LinearMove(new Vector(1, 2));
        move.Suppressed = true;
        Assert.True(move.Suppressed);
    }

    [Fact]
    public void Clone_PreservesSuppressed()
    {
        var move = new LinearMove(new Vector(1, 2));
        move.Suppressed = true;
        var clone = (LinearMove)move.Clone();
        Assert.True(clone.Suppressed);
    }
}

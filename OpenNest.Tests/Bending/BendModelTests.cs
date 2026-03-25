using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.Tests.Bending;

public class BendModelTests
{
    [Fact]
    public void Bend_StoresStartAndEndPoints()
    {
        var bend = new Bend
        {
            StartPoint = new Vector(0, 5),
            EndPoint = new Vector(10, 5),
            Direction = BendDirection.Up,
            Angle = 90,
            Radius = 0.06,
            NoteText = "UP 90° R0.06"
        };

        Assert.Equal(0, bend.StartPoint.X);
        Assert.Equal(10, bend.EndPoint.X);
        Assert.Equal(BendDirection.Up, bend.Direction);
        Assert.Equal(90, bend.Angle);
        Assert.Equal(0.06, bend.Radius);
        Assert.Equal("UP 90° R0.06", bend.NoteText);
    }

    [Fact]
    public void Bend_ToLine_ReturnsGeometryLine()
    {
        var bend = new Bend
        {
            StartPoint = new Vector(0, 5),
            EndPoint = new Vector(10, 5)
        };

        var line = bend.ToLine();

        Assert.Equal(0, line.StartPoint.X);
        Assert.Equal(5, line.StartPoint.Y);
        Assert.Equal(10, line.EndPoint.X);
        Assert.Equal(5, line.EndPoint.Y);
    }

    [Fact]
    public void Bend_Length_ComputesCorrectly()
    {
        var bend = new Bend
        {
            StartPoint = new Vector(0, 0),
            EndPoint = new Vector(3, 4)
        };

        Assert.Equal(5.0, bend.Length, 0.001);
    }

    [Fact]
    public void Bend_BendAngleRadians_ConvertsDegreesToRadians()
    {
        var bend = new Bend { Angle = 90 };

        Assert.Equal(System.Math.PI / 2, bend.AngleRadians, 0.001);
    }

    [Fact]
    public void Bend_DefaultDirection_IsUnknown()
    {
        var bend = new Bend();

        Assert.Equal(BendDirection.Unknown, bend.Direction);
    }

    [Fact]
    public void Bend_ToString_FormatsNicely()
    {
        var bend = new Bend
        {
            Direction = BendDirection.Up,
            Angle = 90,
            Radius = 0.06
        };

        var str = bend.ToString();

        Assert.Contains("Up", str);
        Assert.Contains("90", str);
        Assert.Contains("0.06", str);
    }
}

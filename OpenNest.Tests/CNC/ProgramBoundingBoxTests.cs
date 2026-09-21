using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

public class ProgramBoundingBoxTests
{
    [Fact]
    public void GeometryAwayFromOrigin_DoesNotIncludeOrigin()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 30)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 30)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));

        var box = pgm.BoundingBox();

        Assert.Equal(10, box.Left, precision: 6);
        Assert.Equal(10, box.Bottom, precision: 6);
        Assert.Equal(20, box.Right, precision: 6);
        Assert.Equal(30, box.Top, precision: 6);
    }

    [Fact]
    public void GeometryBelowAndLeftOfOrigin_IsTrackedExactly()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(-30, -20)));
        pgm.Codes.Add(new LinearMove(new Vector(-10, -20)));
        pgm.Codes.Add(new LinearMove(new Vector(-10, -5)));

        var box = pgm.BoundingBox();

        Assert.Equal(-30, box.Left, precision: 6);
        Assert.Equal(-20, box.Bottom, precision: 6);
        Assert.Equal(-10, box.Right, precision: 6);
        Assert.Equal(-5, box.Top, precision: 6);
    }

    [Fact]
    public void EmptyProgram_ReturnsZeroBox()
    {
        var box = new OpenNest.CNC.Program().BoundingBox();

        Assert.Equal(0, box.Width, precision: 6);
        Assert.Equal(0, box.Length, precision: 6);
    }
}

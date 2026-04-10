using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class PipeFlangeShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesOD()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(10, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaExcludesBoltHoles()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsPipeFlange()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        Assert.Equal("PipeFlange", drawing.Name);
    }
}

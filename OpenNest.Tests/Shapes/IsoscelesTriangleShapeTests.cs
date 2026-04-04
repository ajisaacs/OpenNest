using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class IsoscelesTriangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new IsoscelesTriangleShape { Base = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Length, 0.01);
        Assert.Equal(8, bbox.Width, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsHalfBaseTimesHeight()
    {
        var shape = new IsoscelesTriangleShape { Base = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        Assert.Equal(40, drawing.Area, 0.5);
    }
}

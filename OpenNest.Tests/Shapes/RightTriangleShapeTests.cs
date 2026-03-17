using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RightTriangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new RightTriangleShape { Width = 12, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(12, bbox.Width, 0.01);
        Assert.Equal(8, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsHalfWidthTimesHeight()
    {
        var shape = new RightTriangleShape { Width = 12, Height = 8 };
        var drawing = shape.GetDrawing();

        Assert.Equal(48, drawing.Area, 0.5);
    }
}

using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class CircleShapeTests
{
    [Fact]
    public void GetDrawing_ReturnsDrawingWithCorrectBoundingBox()
    {
        var shape = new CircleShape { Diameter = 10 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(10, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsCircle()
    {
        var shape = new CircleShape { Diameter = 10 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Circle", drawing.Name);
    }
}

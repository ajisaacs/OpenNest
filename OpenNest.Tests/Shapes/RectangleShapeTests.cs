using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RectangleShapeTests
{
    [Fact]
    public void GetDrawing_ReturnsDrawingWithCorrectBoundingBox()
    {
        var shape = new RectangleShape { Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(5, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsRectangle()
    {
        var shape = new RectangleShape { Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Rectangle", drawing.Name);
    }

    [Fact]
    public void GetDrawing_CustomName_IsUsed()
    {
        var shape = new RectangleShape { Name = "Plate1", Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Plate1", drawing.Name);
    }
}

using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class TrapezoidShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new TrapezoidShape { BottomWidth = 20, TopWidth = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Length, 0.01);
        Assert.Equal(8, bbox.Width, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsCorrect()
    {
        var shape = new TrapezoidShape { BottomWidth = 20, TopWidth = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        // Area = (top + bottom) / 2 * height = (10 + 20) / 2 * 8 = 120
        Assert.Equal(120, drawing.Area, 0.5);
    }
}

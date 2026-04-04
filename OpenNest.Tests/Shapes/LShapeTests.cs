using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class LShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Length, 0.01);
        Assert.Equal(20, bbox.Width, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultLegDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20 };
        var drawing = shape.GetDrawing();

        // Default legs: LegWidth = Width/2 = 5, LegHeight = Height/2 = 10
        // Area = Width*Height - (Width - LegWidth) * (Height - LegHeight)
        // Area = 10*20 - 5*10 = 150
        Assert.Equal(150, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_CustomLegDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20, LegWidth = 3, LegHeight = 5 };
        var drawing = shape.GetDrawing();

        // Area = Width*Height - (Width - LegWidth) * (Height - LegHeight)
        // Area = 10*20 - 7*15 = 200 - 105 = 95
        Assert.Equal(95, drawing.Area, 0.5);
    }
}

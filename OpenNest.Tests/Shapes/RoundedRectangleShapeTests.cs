using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RoundedRectangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new RoundedRectangleShape { Length = 20, Width = 10, Radius = 2 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Length, 0.1);
        Assert.Equal(10, bbox.Width, 0.1);
    }

    [Fact]
    public void GetDrawing_AreaIsLessThanFullRectangle()
    {
        var shape = new RoundedRectangleShape { Length = 20, Width = 10, Radius = 2 };
        var drawing = shape.GetDrawing();

        // Area should be less than 20*10=200 because corners are rounded
        // Area = W*H - (4 - pi) * r^2 = 200 - (4 - pi) * 4 ~ 196.57
        Assert.True(drawing.Area < 200);
        Assert.True(drawing.Area > 190);
    }

    [Fact]
    public void GetDrawing_ZeroRadius_MatchesRectangleArea()
    {
        var shape = new RoundedRectangleShape { Length = 20, Width = 10, Radius = 0 };
        var drawing = shape.GetDrawing();

        Assert.Equal(200, drawing.Area, 0.5);
    }
}

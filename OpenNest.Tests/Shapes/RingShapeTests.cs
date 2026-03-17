using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RingShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesOuterDiameter()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Width, 0.01);
        Assert.Equal(20, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaExcludesInnerHole()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        // Area = pi * (10^2 - 5^2) = pi * 75
        var expectedArea = System.Math.PI * 75;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsRing()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Ring", drawing.Name);
    }
}

using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class OctagonShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxFitsWithinExpectedSize()
    {
        var shape = new OctagonShape { Width = 20 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        // Corner-to-corner is larger than flat-to-flat
        Assert.True(bbox.Width >= 20 - 0.01);
        Assert.True(bbox.Length >= 20 - 0.01);
        // But should not be wildly larger (corner-to-corner ~ width / cos(22.5deg) ~ width * 1.0824)
        Assert.True(bbox.Width < 22);
        Assert.True(bbox.Length < 22);
    }

    [Fact]
    public void GetDrawing_HasEightEdges()
    {
        var shape = new OctagonShape { Width = 20 };
        var drawing = shape.GetDrawing();

        // An octagon program should have 8 linear moves (one per edge)
        var moves = drawing.Program.Codes
            .OfType<OpenNest.CNC.LinearMove>()
            .Count();
        Assert.Equal(8, moves);
    }
}

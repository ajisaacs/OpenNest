using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class NgonShapeTests
{
    [Fact]
    public void GetDrawing_Octagon_BoundingBoxFitsWithinExpectedSize()
    {
        var shape = new NgonShape { Sides = 8, Width = 20 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        // Corner-to-corner is larger than flat-to-flat
        Assert.True(bbox.Width >= 20 - 0.01);
        Assert.True(bbox.Length >= 20 - 0.01);
        // But should not be wildly larger (corner-to-corner ~ width / cos(22.5deg) ~ width * 1.0824)
        Assert.True(bbox.Width < 22);
        Assert.True(bbox.Length < 22);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void GetDrawing_HasOneLinearMovePerSide(int sides)
    {
        var shape = new NgonShape { Sides = sides, Width = 20 };
        var drawing = shape.GetDrawing();

        var moves = drawing.Program.Codes
            .OfType<OpenNest.CNC.LinearMove>()
            .Count();
        Assert.Equal(sides, moves);
    }

    [Fact]
    public void GetDrawing_ClampsSidesBelowThreeToTriangle()
    {
        var shape = new NgonShape { Sides = 2, Width = 20 };
        var drawing = shape.GetDrawing();

        var moves = drawing.Program.Codes
            .OfType<OpenNest.CNC.LinearMove>()
            .Count();
        Assert.Equal(3, moves);
    }
}

using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class TShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(12, bbox.Width, 0.01);
        Assert.Equal(18, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultStemAndBarDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18 };
        var drawing = shape.GetDrawing();

        // Default: StemWidth = Width/3 = 4, BarHeight = Height/3 = 6
        // Area = Width * BarHeight + StemWidth * (Height - BarHeight)
        // Area = 12 * 6 + 4 * 12 = 72 + 48 = 120
        Assert.Equal(120, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_CustomStemAndBarDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18, StemWidth = 6, BarHeight = 4 };
        var drawing = shape.GetDrawing();

        // Area = Width * BarHeight + StemWidth * (Height - BarHeight)
        // Area = 12 * 4 + 6 * 14 = 48 + 84 = 132
        Assert.Equal(132, drawing.Area, 0.5);
    }
}

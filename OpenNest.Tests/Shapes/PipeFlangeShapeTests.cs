using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class PipeFlangeShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesOD()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(10, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaExcludesBoltHoles()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            Blind = true
        };
        var drawing = shape.GetDrawing();

        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsPipeFlange()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        Assert.Equal("PipeFlange", drawing.Name);
    }

    [Fact]
    public void GetDrawing_WithPipeSize_CutsCenterBoreAtPipeODPlusClearance()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            PipeSize = "2",       // OD = 2.375
            PipeClearance = 0.125,
            Blind = false
        };
        var drawing = shape.GetDrawing();

        // Expected bore diameter = 2.375 + 0.125 = 2.5
        // Area = pi * (5^2 - 0.5^2 * 4 - 1.25^2) = pi * (25 - 1 - 1.5625) = pi * 22.4375
        var expectedArea = System.Math.PI * 22.4375;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_Blind_OmitsCenterBore()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            PipeSize = "2",
            PipeClearance = 0.125,
            Blind = true
        };
        var drawing = shape.GetDrawing();

        // With Blind=true, area = outer - 4 bolt holes = pi * (25 - 1) = pi * 24
        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_UnknownPipeSize_OmitsCenterBore()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            PipeSize = "not-a-real-pipe",
            PipeClearance = 0.125,
            Blind = false
        };
        var drawing = shape.GetDrawing();

        // Unknown pipe size → no bore, area matches blind case
        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_NullOrEmptyPipeSize_OmitsCenterBore()
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            PipeSize = null,
            PipeClearance = 0.125
        };
        var drawing = shape.GetDrawing();

        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }
}

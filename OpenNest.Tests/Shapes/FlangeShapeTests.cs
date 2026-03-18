using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class FlangeShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesOD()
    {
        var shape = new FlangeShape
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
        var shape = new FlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        // Area = pi * 5^2 - 4 * pi * 0.5^2 = pi * (25 - 1) = pi * 24
        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsFlange()
    {
        var shape = new FlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4
        };
        var drawing = shape.GetDrawing();

        Assert.Equal("Flange", drawing.Name);
    }

    [Fact]
    public void LoadFromJson_ProducesCorrectDrawing()
    {
        var json = """
        [
          {
            "Name": "2in-150#",
            "NominalPipeSize": 2.0,
            "OD": 6.0,
            "HoleDiameter": 0.75,
            "HolePatternDiameter": 4.75,
            "HoleCount": 4
          },
          {
            "Name": "2in-300#",
            "NominalPipeSize": 2.0,
            "OD": 6.5,
            "HoleDiameter": 0.75,
            "HolePatternDiameter": 5.0,
            "HoleCount": 8
          }
        ]
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var flanges = ShapeDefinition.LoadFromJson<FlangeShape>(tempFile);

            Assert.Equal(2, flanges.Count);

            var first = flanges[0];
            Assert.Equal("2in-150#", first.Name);
            var drawing = first.GetDrawing();
            var bbox = drawing.Program.BoundingBox();
            Assert.Equal(6, bbox.Width, 0.01);

            var second = flanges[1];
            Assert.Equal("2in-300#", second.Name);
            Assert.Equal(8, second.HoleCount);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

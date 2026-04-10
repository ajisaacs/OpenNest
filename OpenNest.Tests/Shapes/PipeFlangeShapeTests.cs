using System;
using System.IO;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetDrawing_NullOrEmptyPipeSize_OmitsCenterBore(string pipeSize)
    {
        var shape = new PipeFlangeShape
        {
            OD = 10,
            HoleDiameter = 1,
            HolePatternDiameter = 7,
            HoleCount = 4,
            PipeSize = pipeSize,
            PipeClearance = 0.125
        };
        var drawing = shape.GetDrawing();

        var expectedArea = System.Math.PI * 24;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void LoadFromJson_ProducesCorrectDrawing()
    {
        var json = """
        [
          {
            "Name": "2in-150#",
            "PipeSize": "2",
            "PipeClearance": 0.0625,
            "OD": 6.0,
            "HoleDiameter": 0.75,
            "HolePatternDiameter": 4.75,
            "HoleCount": 4
          },
          {
            "Name": "2in-300#",
            "PipeSize": "2",
            "PipeClearance": 0.0625,
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

            var flanges = ShapeDefinition.LoadFromJson<PipeFlangeShape>(tempFile);

            Assert.Equal(2, flanges.Count);

            var first = flanges[0];
            Assert.Equal("2in-150#", first.Name);
            Assert.Equal("2", first.PipeSize);
            Assert.Equal(0.0625, first.PipeClearance, 0.0001);
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

    [Fact]
    public void LoadFromJson_RealShippedConfig_LoadsAllEntries()
    {
        // Resolve the repo-relative config path from the test binary location.
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OpenNest.sln")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);

        var configPath = Path.Combine(dir, "OpenNest", "Configurations", "PipeFlangeShape.json");
        Assert.True(File.Exists(configPath), $"Config missing at {configPath}");

        var flanges = ShapeDefinition.LoadFromJson<PipeFlangeShape>(configPath);

        Assert.NotEmpty(flanges);
        foreach (var f in flanges)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.PipeSize));
            Assert.True(PipeSizes.TryGetOD(f.PipeSize, out _),
                $"Unknown PipeSize '{f.PipeSize}' in entry '{f.Name}'");
            Assert.Equal(0.0625, f.PipeClearance, 0.0001);
        }
    }
}

using ACadSharp.IO;
using OpenNest.Bending;
using OpenNest.IO.Bending;

namespace OpenNest.Tests.Bending;

public class SolidWorksBendDetectorTests
{
    [Fact]
    public void SolidWorksDetector_IsRegistered()
    {
        var detector = BendDetectorRegistry.GetByName("SolidWorks");
        Assert.NotNull(detector);
        Assert.Equal("SolidWorks", detector.Name);
    }

    [Fact]
    public void Registry_ContainsSolidWorksDetector()
    {
        Assert.Contains(BendDetectorRegistry.Detectors,
            d => d.Name == "SolidWorks");
    }

    [Fact]
    public void AutoDetect_EmptyDocument_ReturnsEmptyList()
    {
        var doc = new ACadSharp.CadDocument();
        var bends = BendDetectorRegistry.AutoDetect(doc);
        Assert.Empty(bends);
    }

    [Fact]
    public void DetectBends_RealDxf_ParsesNotesCorrectly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bending", "TestData", "4526 A14 PT45.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        using var reader = new DxfReader(path);
        var doc = reader.Read();

        var detector = new SolidWorksBendDetector();
        var bends = detector.DetectBends(doc);

        Assert.Equal(2, bends.Count);

        foreach (var bend in bends)
        {
            Assert.NotNull(bend.NoteText);
            Assert.Equal(BendDirection.Up, bend.Direction);
            Assert.Equal(90.0, bend.Angle);
            Assert.Equal(0.313, bend.Radius);
        }
    }
}

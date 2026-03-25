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
}

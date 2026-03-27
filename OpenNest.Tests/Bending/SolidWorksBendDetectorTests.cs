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
    public void EllipseConverter_ProducesArcsDirectly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bending", "TestData", "4526 A14 PT11 Test.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        var importer = new OpenNest.IO.DxfImporter { SplinePrecision = 200 };
        var result = importer.Import(path);

        // EllipseConverter now produces arcs directly during import,
        // so the imported entities should contain Arc instances from the ellipses
        var arcCount = result.Entities.Count(e => e is OpenNest.Geometry.Arc);
        Assert.True(arcCount > 0, "Expected arcs from ellipse conversion");

        // The GeometrySimplifier should find few or no line runs to simplify,
        // because ellipses are already converted to arcs at import time
        var shape = new OpenNest.Geometry.Shape();
        shape.Entities.AddRange(result.Entities);

        var simplifier = new OpenNest.Geometry.GeometrySimplifier();
        var candidates = simplifier.Analyze(shape);

        Assert.True(candidates.Count <= 10,
            $"Expected <=10 simplifier candidates but got {candidates.Count}");
    }

    [Fact]
    public void Import_TrimmedEllipse_NoClosingChord()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bending", "TestData", "4526 A14 PT11.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        var importer = new OpenNest.IO.DxfImporter();
        var result = importer.Import(path);

        // The DXF has 2 trimmed ellipses forming an oblong slot.
        // Trimmed ellipses must not generate a closing chord line.
        // EllipseConverter now produces arcs instead of line segments,
        // changing the entity count. Verify arcs are present and no
        // spurious closing chord exists.
        var arcCount = result.Entities.Count(e => e is OpenNest.Geometry.Arc);
        var circleCount = result.Entities.Count(e => e is OpenNest.Geometry.Circle);

        Assert.True(arcCount > 0, "Expected arcs from ellipse conversion");
        Assert.True(circleCount >= 7, $"Expected at least 7 circles, got {circleCount}");
        Assert.Equal(115, result.Entities.Count);
    }

    [Fact]
    public void DetectBends_SplitBendLine_PropagatesNote()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bending", "TestData", "4526 A14 PT23.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        using var reader = new DxfReader(path);
        var doc = reader.Read();

        var detector = new SolidWorksBendDetector();
        var bends = detector.DetectBends(doc);

        Assert.Equal(5, bends.Count);
        Assert.All(bends, b =>
        {
            Assert.NotNull(b.NoteText);
            Assert.Equal(BendDirection.Up, b.Direction);
            Assert.Equal(90.0, b.Angle);
            Assert.Equal(0.125, b.Radius);
        });
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

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
    public void Simplifier_EllipseSegments_FewLargeArcs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bending", "TestData", "4526 A14 PT11 Test.dxf");
        Assert.True(File.Exists(path), $"Test DXF not found: {path}");

        var importer = new OpenNest.IO.DxfImporter { SplinePrecision = 200 };
        var result = importer.Import(path);

        var shape = new OpenNest.Geometry.Shape();
        shape.Entities.AddRange(result.Entities);

        // Default tolerance is 0.5 — should produce very few large arcs
        var simplifier = new OpenNest.Geometry.GeometrySimplifier();
        var candidates = simplifier.Analyze(shape);

        // With 0.5 tolerance, 2 ellipses (~400 segments) should reduce to a handful of arcs
        // Dump for visibility then assert
        var info = string.Join(", ", candidates.Select(c => $"[{c.StartIndex}..{c.EndIndex}]={c.LineCount}lines R={c.FittedArc.Radius:F3}"));
        Assert.True(candidates.Count <= 10,
            $"Expected <=10 arcs but got {candidates.Count}: {info}");

        // Each arc should cover many lines
        foreach (var c in candidates)
            Assert.True(c.LineCount >= 3, $"Arc [{c.StartIndex}..{c.EndIndex}] only covers {c.LineCount} lines");

        // Arcs should connect to the original geometry within tolerance
        foreach (var c in candidates)
        {
            var firstLine = (OpenNest.Geometry.Line)shape.Entities[c.StartIndex];
            var lastLine = (OpenNest.Geometry.Line)shape.Entities[c.EndIndex];
            var arc = c.FittedArc;

            var startGap = firstLine.StartPoint.DistanceTo(arc.StartPoint());
            var endGap = lastLine.EndPoint.DistanceTo(arc.EndPoint());

            Assert.True(startGap < 1e-9, $"Start gap {startGap} at candidate [{c.StartIndex}..{c.EndIndex}]");
            Assert.True(endGap < 1e-9, $"End gap {endGap} at candidate [{c.StartIndex}..{c.EndIndex}]");
        }
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
        // 83 = 72 lines + 4 arcs + 7 circles + ellipse segments (heavily merged by optimizer)
        Assert.Equal(83, result.Entities.Count);
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

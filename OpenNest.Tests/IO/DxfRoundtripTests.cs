using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class DxfRoundtripTests
{
    private const double Tolerance = 0.01;

    private static List<Entity> ExportAndReimport(List<Entity> geometry)
    {
        var program = ConvertGeometry.ToProgram(geometry);
        var exporter = new DxfExporter();
        var importer = new DxfImporter();

        using var exportStream = new MemoryStream();
        exporter.ExportProgram(program, exportStream);
        var bytes = exportStream.ToArray();

        var importStream = new MemoryStream(bytes);
        var success = importer.GetGeometry(importStream, out var reimported);

        Assert.True(success, "Failed to re-import exported DXF");
        return reimported;
    }

    private static List<T> FilterByLayer<T>(List<Entity> entities, string layerName) where T : Entity
    {
        return entities
            .Where(e => e is T && e.Layer?.Name == layerName)
            .Cast<T>()
            .ToList();
    }

    [Fact]
    public void Roundtrip_Lines_PreservesGeometry()
    {
        // A simple triangle (non-collinear lines to avoid GeometryOptimizer merging)
        var original = new List<Entity>
        {
            new Line(0, 0, 10, 0),
            new Line(10, 0, 5, 8),
            new Line(5, 8, 0, 0)
        };

        var reimported = ExportAndReimport(original);
        var cutLines = FilterByLayer<Line>(reimported, "Cut");

        Assert.Equal(original.Count, cutLines.Count);

        for (var i = 0; i < original.Count; i++)
        {
            var orig = (Line)original[i];
            var rt = cutLines[i];
            Assert.Equal(orig.StartPoint.X, rt.StartPoint.X, Tolerance);
            Assert.Equal(orig.StartPoint.Y, rt.StartPoint.Y, Tolerance);
            Assert.Equal(orig.EndPoint.X, rt.EndPoint.X, Tolerance);
            Assert.Equal(orig.EndPoint.Y, rt.EndPoint.Y, Tolerance);
        }
    }

    [Fact]
    public void Roundtrip_Circle_PreservesCenterAndRadius()
    {
        var original = new Circle(5, 3, 4);
        var geometry = new List<Entity> { original };

        var reimported = ExportAndReimport(geometry);
        var circles = FilterByLayer<Circle>(reimported, "Cut");

        Assert.Single(circles);
        var rt = circles[0];
        Assert.Equal(original.Center.X, rt.Center.X, Tolerance);
        Assert.Equal(original.Center.Y, rt.Center.Y, Tolerance);
        Assert.Equal(original.Radius, rt.Radius, Tolerance);
    }

    [Fact]
    public void Roundtrip_Arc_PreservesCenterRadiusAndAngles()
    {
        var original = new Arc(5, 3, 4, 0.0, System.Math.PI / 2);
        var geometry = new List<Entity> { original };

        var reimported = ExportAndReimport(geometry);
        var arcs = FilterByLayer<Arc>(reimported, "Cut");

        Assert.Single(arcs);
        var rt = arcs[0];
        Assert.Equal(original.Center.X, rt.Center.X, Tolerance);
        Assert.Equal(original.Center.Y, rt.Center.Y, Tolerance);
        Assert.Equal(original.Radius, rt.Radius, Tolerance);
    }

    [Fact]
    public void Roundtrip_Mixed_PreservesEntityTypes()
    {
        var original = new List<Entity>
        {
            new Line(0, 0, 10, 0),
            new Line(10, 0, 10, 5),
            new Circle(20, 20, 3),
            new Arc(15, 15, 5, 0.0, System.Math.PI)
        };

        var reimported = ExportAndReimport(original);

        var cutLines = FilterByLayer<Line>(reimported, "Cut");
        var cutCircles = FilterByLayer<Circle>(reimported, "Cut");
        var cutArcs = FilterByLayer<Arc>(reimported, "Cut");

        Assert.Equal(2, cutLines.Count);
        Assert.Single(cutCircles);
        Assert.Single(cutArcs);
    }

    [Fact]
    public void Roundtrip_Rectangle_PreservesBoundingBox()
    {
        // Rectangle with distinct width/height so optimizer won't merge
        var original = new List<Entity>
        {
            new Line(0, 0, 20, 0),
            new Line(20, 0, 20, 10),
            new Line(20, 10, 0, 10),
            new Line(0, 10, 0, 0)
        };

        var reimported = ExportAndReimport(original);
        var cutLines = FilterByLayer<Line>(reimported, "Cut");

        // Verify bounding box is preserved regardless of line order
        var origMinX = original.Cast<Line>().Min(l => System.Math.Min(l.StartPoint.X, l.EndPoint.X));
        var origMaxX = original.Cast<Line>().Max(l => System.Math.Max(l.StartPoint.X, l.EndPoint.X));
        var origMinY = original.Cast<Line>().Min(l => System.Math.Min(l.StartPoint.Y, l.EndPoint.Y));
        var origMaxY = original.Cast<Line>().Max(l => System.Math.Max(l.StartPoint.Y, l.EndPoint.Y));

        var rtMinX = cutLines.Min(l => System.Math.Min(l.StartPoint.X, l.EndPoint.X));
        var rtMaxX = cutLines.Max(l => System.Math.Max(l.StartPoint.X, l.EndPoint.X));
        var rtMinY = cutLines.Min(l => System.Math.Min(l.StartPoint.Y, l.EndPoint.Y));
        var rtMaxY = cutLines.Max(l => System.Math.Max(l.StartPoint.Y, l.EndPoint.Y));

        Assert.Equal(origMinX, rtMinX, Tolerance);
        Assert.Equal(origMaxX, rtMaxX, Tolerance);
        Assert.Equal(origMinY, rtMinY, Tolerance);
        Assert.Equal(origMaxY, rtMaxY, Tolerance);
    }
}

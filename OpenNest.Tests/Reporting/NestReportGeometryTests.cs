using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Reporting;

namespace OpenNest.Tests.Reporting;

public class NestReportGeometryTests
{
    [Fact]
    public void Capture_PreservesNativeCircleHoleAndAppliesBakedRotationAndLocationOnce()
    {
        var nest = NestReportTestData.CreateNest();
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var holed = snapshot.Plates[0].Parts[0].Geometry;
        Assert.False(holed.StrokeOnly);
        Assert.Equal(2, holed.Contours.Length);
        Assert.All(holed.Contours, c => Assert.True(c.Closed));
        var circle = Assert.Single(holed.Contours.SelectMany(c => c.Segments).Where(s => s.Center != null));
        Assert.Equal(new ReportPoint(7, 7), circle.Center);
        Assert.Equal(1, circle.Radius);
        Assert.Equal(-2 * System.Math.PI, circle.SweepAngle);
        Assert.Equal(new ReportBounds(2, 2, 12, 12), holed.Bounds);
        var turned = snapshot.Plates[0].Parts[2].Geometry;
        AssertBounds(new ReportBounds(28, 2, 30, 6), turned.Bounds);
        AssertBounds(new ReportBounds(0, 0, 4, 2), snapshot.Drawings[1].Geometry.Bounds);
    }

    [Fact]
    public void Capture_RealTabbedLeadInHoleRemainsOpenAndNeverMutatesPrograms()
    {
        var nest = NestReportTestData.CreateTabbedNest();
        var part = nest.Plates[0].Parts[0];
        var converted = ConvertProgram.ToGeometry(part.Program);
        Assert.Contains(converted, e => e.Layer == SpecialLayers.Leadin);
        Assert.Contains(converted, e => e.Layer == SpecialLayers.Leadout);
        Assert.NotEmpty(part.Program.SubPrograms);
        // Deliberately stale settings must not control closure detection.
        part.CuttingParameters.TabsEnabled = false;
        var before = NestReportBuilderTests.Fingerprint(nest);

        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var geometry = Assert.Single(snapshot.Plates[0].Parts).Geometry;

        Assert.True(geometry.StrokeOnly);
        var open = Assert.Single(geometry.Contours.Where(c => !c.Closed));
        Assert.NotEqual(open.Segments[0].Start, open.Segments[^1].End);
        var gapX = open.Segments[^1].End.X - open.Segments[0].Start.X;
        var gapY = open.Segments[^1].End.Y - open.Segments[0].Start.Y;
        Assert.Equal(0.15, System.Math.Sqrt(gapX * gapX + gapY * gapY), 6);
        Assert.Single(geometry.Contours.Where(c => c.Closed));
        AssertBounds(new ReportBounds(10, 5, 20, 15), geometry.Bounds);
        var hole = Assert.Single(geometry.Contours.SelectMany(c => c.Segments).Where(s => s.Center != null));
        Assert.Equal(15, hole.Center!.X, 6);
        Assert.Equal(10, hole.Center.Y, 6);
        var outlineCount = converted.Count(e => SpecialLayers.IsMaterial(e.Layer) && e.Layer != SpecialLayers.Leadin && e.Layer != SpecialLayers.Leadout);
        Assert.Equal(outlineCount, geometry.Contours.Sum(c => c.Segments.Length));
        Assert.False(snapshot.Drawings[0].Geometry.StrokeOnly);
        Assert.Equal(before, NestReportBuilderTests.Fingerprint(nest));
    }

    [Fact]
    public void Capture_PreservesNativeArcSweepAndTrueBounds()
    {
        var nest = NestReportTestData.CreateNest();
        var program = nest.Plates[0].Parts[0].Program;
        program.Codes.Clear();
        program.Codes.Add(new RapidMove(2, 0));
        program.Codes.Add(new ArcMove(0, 2, 0, 0, RotationType.CCW));
        var geometry = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt).Plates[0].Parts[0].Geometry;
        var arc = Assert.Single(Assert.Single(geometry.Contours).Segments);
        Assert.Equal(new ReportPoint(2, 2), arc.Center);
        Assert.Equal(2, arc.Radius);
        Assert.Equal(0, arc.StartAngle);
        Assert.Equal(System.Math.PI / 2, arc.SweepAngle);
        AssertBounds(new ReportBounds(2, 2, 4, 4), geometry.Bounds);
        Assert.True(geometry.StrokeOnly);
    }

    [Fact]
    public void Capture_ExcludedLayersCannotConnectSeparateMaterialPaths()
    {
        var nest = NestReportTestData.CreateNest();
        var codes = nest.Plates[0].Parts[0].Program.Codes;
        codes.Clear();
        codes.Add(new RapidMove(0, 0));
        codes.Add(new LinearMove(1, 0) { Layer = LayerType.Display });
        codes.Add(new LinearMove(1, 1) { Layer = LayerType.Scribe });
        codes.Add(new LinearMove(2, 1));
        codes.Add(new LinearMove(100, 100) { Layer = LayerType.Leadout });
        var geometry = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt).Plates[0].Parts[0].Geometry;
        Assert.Equal(2, geometry.Contours.Length);
        Assert.All(geometry.Contours, c => { Assert.False(c.Closed); Assert.Single(c.Segments); });
        Assert.Equal(new ReportBounds(2, 2, 4, 3), geometry.Bounds);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nonfinite")]
    [InlineData("arc-center")]
    [InlineData("zero-radius")]
    [InlineData("unequal-radii")]
    [InlineData("rotation")]
    [InlineData("unsupported")]
    [InlineData("null-code")]
    [InlineData("null-subprogram")]
    [InlineData("cyclic-subprogram")]
    [InlineData("nonfinite-offset")]
    public void Capture_RejectsMalformedPlacedGeometryWithIdentification(string damage)
    {
        var nest = NestReportTestData.CreateNest();
        var part = nest.Plates[0].Parts[0];
        var codes = part.Program.Codes;
        switch (damage)
        {
            case "missing": codes.Clear(); break;
            case "nonfinite": ((LinearMove)codes[1]).EndPoint = new Vector(double.NaN, 1); break;
            case "arc-center": codes.Add(new ArcMove(1, 0, double.PositiveInfinity, 0)); break;
            case "zero-radius": codes.Add(new ArcMove(6, 5, 6, 5)); break;
            case "unequal-radii": codes.Add(new ArcMove(10, 5, 5, 5)); break;
            case "rotation": codes.Add(new ArcMove(6, 5, 5, 5, (RotationType)42)); break;
            case "unsupported": codes.Add(new UnsupportedCode()); break;
            case "null-code": codes.Add(null!); break;
            case "null-subprogram": codes.Add(new SubProgramCall { Id = 42 }); break;
            case "cyclic-subprogram": codes.Add(new SubProgramCall { Id = 42, Program = part.Program }); break;
            case "nonfinite-offset": part.Location = new Vector(double.NaN, 0); break;
        }
        var count = codes.Count;
        var error = Assert.Throws<InvalidOperationException>(() => NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt));
        Assert.Contains("Plate 1", error.Message);
        Assert.Contains("part 1", error.Message);
        Assert.Contains("Bracket", error.Message);
        Assert.Equal(count, codes.Count);
    }

    [Fact]
    public void Capture_RejectsUnrenderableDemandOnlyDrawingByName()
    {
        var nest = new Nest();
        var drawing = new Drawing("Missing demand geometry");
        drawing.Quantity.Required = 3;
        nest.Drawings.Add(drawing);
        var error = Assert.Throws<InvalidOperationException>(() => NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt));
        Assert.Contains("Missing demand geometry", error.Message);
    }

    private static void AssertBounds(ReportBounds expected, ReportBounds actual)
    {
        Assert.Equal(expected.Left, actual.Left, 6);
        Assert.Equal(expected.Bottom, actual.Bottom, 6);
        Assert.Equal(expected.Right, actual.Right, 6);
        Assert.Equal(expected.Top, actual.Top, 6);
    }

    private sealed class UnsupportedCode : ICode
    {
        public CodeType Type => (CodeType)999;
        public ICode Clone() => new UnsupportedCode();
    }
}

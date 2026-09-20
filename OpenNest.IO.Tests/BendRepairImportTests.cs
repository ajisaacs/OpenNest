using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.Geometry;
using OpenNest.IO.Bending;
using CadLine = ACadSharp.Entities.Line;
using CadLayer = ACadSharp.Tables.Layer;

namespace OpenNest.IO.Tests;

public class BendRepairImportTests
{
    [Theory]
    [InlineData("ETCH", false, false, "Repaired")]
    [InlineData("SCRIBE", false, false, "Repaired")]
    [InlineData("ETCH", true, false, "Skipped")]
    [InlineData("ETCH", false, true, "Skipped")]
    public void ImportPreservesMarksAndHonorsAmbiguityAndHeader(string layer, bool duplicate, bool conflict, string status)
    {
        var doc = Fixture(layer);
        if (duplicate) doc.Entities.Add(new CadLine(new XYZ(0.05, 5, 0), new XYZ(0.55, 5, 0)) { Layer = new CadLayer(layer) });
        if (conflict) doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Millimeters;
        WithFile(doc, path =>
        {
            var raw = Dxf.Import(path, preserveRepairMarks: true);
            var result = CadImporter.Import(path, new CadImportOptions { BendRepair = Options() });
            Assert.Equal(status, Assert.Single(result.BendRepairReports).Status);
            Assert.Equal(raw.Entities.Count, result.Entities.Count);
            Assert.Equal(Signatures(raw.Entities.Where(e => e.Layer.Name == "0")), Signatures(result.Entities.Where(e => e.Layer.Name == "0")));
            Assert.Contains(result.Entities.OfType<Line>(), l => l.StartPoint == new Vector(4, 2) && l.EndPoint == new Vector(4, 3));
            if (status == "Skipped") Assert.Equal(Signatures(raw.Entities), Signatures(result.Entities));
            else
            {
                Assert.Equal(new Vector(0, 5), result.Bends[0].StartPoint);
                Assert.Equal(new Vector(10, 5), result.Bends[0].EndPoint);
                Assert.Equal("Unchanged", Assert.Single(BendRepair.Apply(result.Entities, result.Bends, Options())).Status);
            }
            Assert.Empty(CadImporter.Import(path).BendRepairReports);
            var disabled = CadImporter.Import(path, new CadImportOptions { DetectBends = false, BendRepair = Options() });
            Assert.Empty(disabled.Bends);
            Assert.Equal(Signatures(raw.Entities), Signatures(disabled.Entities));
        });
    }

    [Fact]
    public void UnitlessHeaderRequiresExplicitCallerUnits()
    {
        var doc = Fixture("ETCH");
        doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Unitless;
        WithFile(doc, path =>
        {
            var configured = CadImporter.Import(path, new CadImportOptions { BendRepair = Options() });
            Assert.Equal("Repaired", Assert.Single(configured.BendRepairReports).Status);
            var unspecified = CadImporter.Import(path, new CadImportOptions { BendRepair = new BendRepairOptions { MaxEndpointMovementMillimeters = 2 } });
            Assert.Equal("Skipped", Assert.Single(unspecified.BendRepairReports).Status);
            Assert.Equal(Signatures(Dxf.Import(path, true).Entities), Signatures(unspecified.Entities));
        });
    }

    [Fact]
    public void MarkCircleDoesNotDeduplicateCutCircle()
    {
        var doc = Fixture("SCRIBE");
        doc.Entities.Add(new ACadSharp.Entities.Circle { Center = new XYZ(2, 2, 0), Radius = 0.2, Layer = new CadLayer("SCRIBE") });
        doc.Entities.Add(new ACadSharp.Entities.Circle { Center = new XYZ(2, 2, 0), Radius = 0.2 });
        WithFile(doc, path =>
        {
            var result = CadImporter.Import(path, new CadImportOptions { BendRepair = Options() });
            Assert.Equal(2, result.Entities.OfType<Circle>().Count());
            Assert.Single(result.Entities.OfType<Circle>().Where(e => e.Layer.Name == "0"));
            Assert.Single(result.Entities.OfType<Circle>().Where(e => e.Layer.Name == "SCRIBE"));
        });
    }

    private static BendRepairOptions Options() => new() { DrawingUnits = BendRepairUnits.Inches, MaxEndpointMovementMillimeters = 2 };

    private static CadDocument Fixture(string layer)
    {
        var doc = new CadDocument();
        doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Inches;
        foreach (var line in new[] {
            new CadLine(new XYZ(0, 0, 0), new XYZ(10, 0, 0)),
            new CadLine(new XYZ(10, 0, 0), new XYZ(10, 10, 0)),
            new CadLine(new XYZ(10, 10, 0), new XYZ(0, 10, 0)),
            new CadLine(new XYZ(0, 10, 0), new XYZ(0, 0, 0)),
            new CadLine(new XYZ(0.05, 5, 0), new XYZ(0.55, 5, 0)) { Layer = new CadLayer(layer) },
            new CadLine(new XYZ(9.45, 5, 0), new XYZ(9.95, 5, 0)) { Layer = new CadLayer(layer) },
            new CadLine(new XYZ(4, 2, 0), new XYZ(4, 3, 0)) { Layer = new CadLayer(layer) },
            new CadLine(new XYZ(0.05, 5, 0), new XYZ(9.95, 5, 0)) { Layer = new CadLayer("BEND"), LineType = new ACadSharp.Tables.LineType("CENTER") }
        }) doc.Entities.Add(line);
        return doc;
    }

    private static string[] Signatures(IEnumerable<Entity> entities) => entities.OfType<Line>()
        .Select(l => $"{l.Layer.Name}:{l.StartPoint}:{l.EndPoint}:{l.LineTypeName}").Order().ToArray();

    private static void WithFile(CadDocument doc, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opennest-repair-{Guid.NewGuid()}.dxf");
        try
        {
            DxfWriter.Write(path, doc, false);
            action(path);
        }
        finally { File.Delete(path); }
    }
}

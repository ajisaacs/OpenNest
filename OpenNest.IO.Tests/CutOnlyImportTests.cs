using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.CNC;
using CadLayer = ACadSharp.Tables.Layer;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.IO.Tests;

public class CutOnlyImportTests
{
    [Theory]
    [InlineData("ETCH")]
    [InlineData("Scribe")]
    [InlineData("sCrIbE")]
    public void CutOnlyImportDropsMarksBeforeGeometryAndDoesNotRegenerateBends(string layer)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opennest-cut-only-{Guid.NewGuid()}.dxf");
        try
        {
            var doc = new CadDocument();
            var points = new[]
            {
                new XYZ(0, 0, 0),
                new XYZ(10, 0, 0),
                new XYZ(10, 10, 0),
                new XYZ(0, 10, 0),
            };
            for (var i = 0; i < points.Length; i++)
                doc.Entities.Add(new CadLine(points[i], points[(i + 1) % points.Length]));
            // Marks outside the perimeter must not affect nesting bounds or become cut paths.
            doc.Entities.Add(
                new CadLine(new XYZ(-5, 5, 0), new XYZ(15, 5, 0)) { Layer = new CadLayer(layer) }
            );
            doc.Entities.Add(
                new CadLine(new XYZ(0, 5, 0), new XYZ(10, 5, 0))
                {
                    Layer = new CadLayer("BEND"),
                    LineType = new ACadSharp.Tables.LineType("CENTER"),
                }
            );
            DxfWriter.Write(path, doc, false);
            var drawing = CadImporter.ImportDrawing(
                path,
                new CadImportOptions { DetectBends = false, Quantity = 3 }
            );
            Assert.Empty(drawing.Bends);
            Assert.Equal(4, drawing.SourceEntities.Count);
            Assert.Equal(3, drawing.Quantity.Required);
            Assert.Equal(100, drawing.Area, 6);
            Assert.DoesNotContain(
                drawing.Program.Codes.OfType<LinearMove>(),
                m => m.Layer == LayerType.Scribe
            );
            var job = new NestJob(
                new[] { DrawingJobMapper.FromDrawing("part", drawing, 3) },
                new[] { new NestPlateStock("sheet", new OpenNest.Geometry.Size(30, 30), 1, 0.3) }
            );
            var result = new FixedStrategyNestingEngine("Strip").Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Assert.Equal(3, Assert.Single(result.Fulfillment).Placed);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

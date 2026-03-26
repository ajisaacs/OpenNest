using ACadSharp.IO;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.Tests.Splitting;

public class SplitDxfWriterEtchLayerTests
{
    [Fact]
    public void Write_DrawingWithUpBend_EtchLinesHaveEtchLayer()
    {
        // Create a simple rectangular drawing with an up bend
        var drawing = new RectangleShape { Name = "TEST", Length = 100, Width = 50 }.GetDrawing();
        drawing.Bends = new List<Bend>
        {
            new Bend
            {
                StartPoint = new Vector(0, 25),
                EndPoint = new Vector(100, 25),
                Direction = BendDirection.Up,
                Angle = 90,
                Radius = 0.06,
                NoteText = "UP 90° R0.06"
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"etch_layer_test_{Guid.NewGuid()}.dxf");
        try
        {
            var writer = new SplitDxfWriter();
            writer.Write(tempPath, drawing);

            // Re-read the DXF and check entity layers
            using var reader = new DxfReader(tempPath);
            var doc = reader.Read();

            var etchEntities = new List<ACadSharp.Entities.Entity>();
            var allEntities = new List<(string LayerName, string Type)>();

            foreach (var entity in doc.Entities)
            {
                var layerName = entity.Layer?.Name ?? "(null)";
                allEntities.Add((layerName, entity.GetType().Name));

                // Etch lines are short lines along the bend direction at the ends
                if (entity is ACadSharp.Entities.Line line)
                {
                    // Check if this line is an etch mark (short, near the bend Y=25)
                    var midY = (line.StartPoint.Y + line.EndPoint.Y) / 2;
                    var length = System.Math.Sqrt(
                        System.Math.Pow(line.EndPoint.X - line.StartPoint.X, 2) +
                        System.Math.Pow(line.EndPoint.Y - line.StartPoint.Y, 2));

                    if (System.Math.Abs(midY - 25) < 0.1 && length <= 1.5 && layerName != "BEND")
                    {
                        etchEntities.Add(entity);
                    }
                }
            }

            // Should have etch lines (up bend with length 100 > 3*EtchLength, so 2 etch dashes)
            Assert.True(etchEntities.Count >= 2,
                $"Expected at least 2 etch lines, found {etchEntities.Count}. " +
                $"All entities: {string.Join(", ", allEntities.Select(e => $"{e.Type}@{e.LayerName}"))}");

            // ALL etch lines should be on the ETCH layer, not layer 0
            foreach (var etch in etchEntities)
            {
                var layerName = etch.Layer?.Name ?? "(null)";
                Assert.Equal("ETCH", layerName);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void Write_SplitDrawingWithUpBend_EtchLinesHaveEtchLayer()
    {
        // Create a drawing, split it, then verify etch layers in the split DXFs
        var drawing = new RectangleShape { Name = "TEST", Length = 100, Width = 50 }.GetDrawing();
        drawing.Bends = new List<Bend>
        {
            new Bend
            {
                StartPoint = new Vector(0, 25),
                EndPoint = new Vector(100, 25),
                Direction = BendDirection.Up,
                Angle = 90,
                Radius = 0.06,
                NoteText = "UP 90° R0.06"
            }
        };

        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };
        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        Assert.Equal(2, results.Count);

        foreach (var splitDrawing in results)
        {
            // Each split piece should have the bend (clipped to region)
            Assert.NotNull(splitDrawing.Bends);
            Assert.True(splitDrawing.Bends.Count > 0, $"{splitDrawing.Name} should have bends");

            var tempPath = Path.Combine(Path.GetTempPath(), $"split_etch_test_{splitDrawing.Name}_{Guid.NewGuid()}.dxf");
            try
            {
                var writer = new SplitDxfWriter();
                writer.Write(tempPath, splitDrawing);

                // Re-read and verify
                using var reader = new DxfReader(tempPath);
                var doc = reader.Read();

                var entitySummary = new List<string>();
                var etchLayerEntities = new List<ACadSharp.Entities.Entity>();
                var layer0Entities = new List<ACadSharp.Entities.Entity>();

                foreach (var entity in doc.Entities)
                {
                    var layerName = entity.Layer?.Name ?? "(null)";
                    entitySummary.Add($"{entity.GetType().Name}@{layerName}");

                    if (string.Equals(layerName, "ETCH", StringComparison.OrdinalIgnoreCase))
                        etchLayerEntities.Add(entity);
                    else if (string.Equals(layerName, "0", StringComparison.OrdinalIgnoreCase))
                        layer0Entities.Add(entity);
                }

                // Should have etch entities
                Assert.True(etchLayerEntities.Count > 0,
                    $"{splitDrawing.Name}: No entities on ETCH layer. " +
                    $"All: {string.Join(", ", entitySummary)}");

                // No entities should be on layer 0
                Assert.True(layer0Entities.Count == 0,
                    $"{splitDrawing.Name}: {layer0Entities.Count} entities on layer 0 " +
                    $"(expected all on CUT/BEND/ETCH). " +
                    $"All: {string.Join(", ", entitySummary)}");
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void Write_ReImport_EtchEntitiesFilteredFromCutGeometry()
    {
        // After re-import, ETCH entities should be filtered (like BEND) since
        // etch marks are generated from bends, not treated as cut geometry.
        var drawing = new RectangleShape { Name = "TEST", Length = 100, Width = 50 }.GetDrawing();
        drawing.Bends = new List<Bend>
        {
            new Bend
            {
                StartPoint = new Vector(0, 25),
                EndPoint = new Vector(100, 25),
                Direction = BendDirection.Up,
                Angle = 90,
                Radius = 0.06,
                NoteText = "UP 90° R0.06"
            }
        };

        var splitLines = new List<SplitLine> { new SplitLine(50.0, CutOffAxis.Vertical) };
        var parameters = new SplitParameters { Type = SplitType.Straight };
        var results = DrawingSplitter.Split(drawing, splitLines, parameters);

        foreach (var splitDrawing in results)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"reimport_etch_test_{splitDrawing.Name}_{Guid.NewGuid()}.dxf");
            try
            {
                var writer = new SplitDxfWriter();
                writer.Write(tempPath, splitDrawing);

                // Re-import via DxfImporter (same path as CadConverterForm)
                var importer = new DxfImporter();
                var result = importer.Import(tempPath);

                // ETCH entities should be filtered during import (like BEND)
                var etchEntities = result.Entities
                    .Where(e => string.Equals(e.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var layer0Entities = result.Entities
                    .Where(e => string.Equals(e.Layer?.Name, "0", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Assert.True(etchEntities.Count == 0,
                    $"{splitDrawing.Name}: ETCH entities should be filtered during import, found {etchEntities.Count}");

                Assert.True(layer0Entities.Count == 0,
                    $"{splitDrawing.Name}: {layer0Entities.Count} entities on layer 0 after re-import");

                // All imported entities should be on CUT layer (cut geometry only)
                Assert.True(result.Entities.Count > 0, $"{splitDrawing.Name}: Should have cut geometry");
                Assert.True(result.Entities.All(e => string.Equals(e.Layer?.Name, "CUT", StringComparison.OrdinalIgnoreCase)),
                    $"{splitDrawing.Name}: All imported entities should be on CUT layer. " +
                    $"Found: {string.Join(", ", result.Entities.Select(e => e.Layer?.Name ?? "(null)").Distinct())}");
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}

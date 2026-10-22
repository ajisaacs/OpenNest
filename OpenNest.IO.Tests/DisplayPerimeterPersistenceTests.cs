using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.TabbedCutouts;

namespace OpenNest.IO.Tests;

/// <summary>A supplied blank's display perimeter must survive save/reload as never-cut geometry.</summary>
public class DisplayPerimeterPersistenceTests
{
    private static Drawing Door()
    {
        var outline = new List<Entity>
        {
            new Line(0, 0, 30, 0), new Line(30, 0, 30, 20), new Line(30, 20, 0, 20), new Line(0, 20, 0, 0),
        };
        foreach (var line in outline)
            line.Layer = SpecialLayers.DisplayPerimeter;
        var entities = new List<Entity>(outline)
        {
            new Line(5, 5, 10, 5), new Line(10, 5, 10, 10), new Line(10, 10, 5, 10), new Line(5, 10, 5, 5),
        };
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "door" },
            entities, new List<Bend>(), 1, null, null, null, new List<TabbedCutout>());
    }

    private static int DisplayPerimeterMoves(Program program) =>
        program.Codes.Count(code => code is LinearMove { Layer: LayerType.DisplayPerimeter } or ArcMove { Layer: LayerType.DisplayPerimeter });

    [Fact]
    public void DisplayPerimeter_SurvivesSaveAndReloadOnTheDrawingAndItsPlacements()
    {
        var drawing = Door();
        Assert.Equal(4, DisplayPerimeterMoves(drawing.Program));
        var nest = new Nest("door") { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(20, 30));
        plate.Parts.Add(new Part(drawing, Vector.Zero));
        nest.Plates.Add(plate);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));

        var reader = new NestReader(new MemoryStream(stream.ToArray()));
        var restored = reader.Read();

        Assert.Empty(reader.Warnings);
        var reread = Assert.Single(restored.Drawings);
        Assert.Equal(4, DisplayPerimeterMoves(reread.Program));
        var outline = CadImporter.EditableEntities(reread).Where(e => e.Length > 19.9).ToList();
        Assert.Equal(4, outline.Count);
        Assert.All(outline, e => Assert.Same(SpecialLayers.DisplayPerimeter, e.Layer));
        var part = Assert.Single(restored.Plates.SelectMany(p => p.Parts));
        Assert.Equal(4, DisplayPerimeterMoves(part.Program));
    }
}

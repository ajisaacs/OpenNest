using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Labels;

namespace OpenNest.Tests.Labels;

public class LabelBoxPersistenceTests
{
    private static List<Entity> Rectangle() =>
    [
        new Line(1, 2, 11, 2), new Line(11, 2, 11, 7), new Line(11, 7, 1, 7), new Line(1, 7, 1, 2),
    ];

    private static Nest RoundTrip(Drawing drawing)
    {
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);

        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        ms.Position = 0;
        return new NestReader(ms).Read();
    }

    [Fact]
    public void LabelBox_SurvivesNestRoundTrip()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.LabelBox = new LabelBox(new Vector(3.5, 4.25), 1.75, 0.25, 0.3);

        var loaded = RoundTrip(drawing).Drawings.Single();

        Assert.NotNull(loaded.LabelBox);
        Assert.Equal(3.5, loaded.LabelBox.Center.X, 9);
        Assert.Equal(4.25, loaded.LabelBox.Center.Y, 9);
        Assert.Equal(1.75, loaded.LabelBox.Width, 9);
        Assert.Equal(0.25, loaded.LabelBox.Height, 9);
        Assert.Equal(0.3, loaded.LabelBox.Rotation, 9);
    }

    [Fact]
    public void NoLabelBox_StaysNullAndIsOmittedFromTheFile()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        Assert.Null(RoundTrip(drawing).Drawings.Single().LabelBox);

        var nest = new Nest();
        nest.Drawings.Add(drawing);
        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open());
            Assert.DoesNotContain("labelBox", reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void InvalidLabelBox_IsNotWritten()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.LabelBox = new LabelBox(new Vector(1, 1), 0, 0.25, 0);

        Assert.Null(RoundTrip(drawing).Drawings.Single().LabelBox);
    }

    [Fact]
    public void LabelBox_LeavesTheProgramUnchanged()
    {
        var entities = Rectangle();
        var result = new CadImportResult { Entities = entities, Name = "part" };
        var plain = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null);
        var labeled = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null,
            labelBox: new LabelBox(new Vector(6, 4.5), 1.75, 0.25, 0));

        Assert.NotNull(labeled.LabelBox);
        Assert.Equal(plain.Program.Codes.Select(c => c.ToString()), labeled.Program.Codes.Select(c => c.ToString()));
        Assert.Equal(plain.Source.Offset, labeled.Source.Offset);
    }

    [Fact]
    public void BuildDrawing_KeepsACopy()
    {
        var entities = Rectangle();
        var result = new CadImportResult { Entities = entities, Name = "part" };
        var box = new LabelBox(new Vector(6, 4.5), 1.75, 0.25, 0);

        var drawing = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null, labelBox: box);
        box.Center = new Vector(0, 0);

        Assert.Equal(new Vector(6, 4.5), drawing.LabelBox.Center);
    }

    [Fact]
    public void ApplyEdit_TakesTheEditedLabelBox()
    {
        var entities = Rectangle();
        var result = new CadImportResult { Entities = entities, Name = "part" };
        var drawing = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null,
            labelBox: new LabelBox(new Vector(2, 2), 1, 0.25, 0));
        var edited = CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null,
            labelBox: new LabelBox(new Vector(6, 4.5), 1.75, 0.25, 0));

        CadImporter.ApplyEdit(drawing, edited);
        Assert.Equal(new Vector(6, 4.5), drawing.LabelBox.Center);

        CadImporter.ApplyEdit(drawing, CadImporter.BuildDrawing(result, entities, new List<Bend>(), 1, null, null));
        Assert.Null(drawing.LabelBox);
    }
}

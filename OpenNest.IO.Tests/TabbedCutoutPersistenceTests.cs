using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.TabbedCutouts;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

public class TabbedCutoutPersistenceTests
{
    private static Drawing WithCutout()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.TabbedCutouts.Add(new TabbedCutout
        {
            EdgeIds = [Guid.NewGuid(), Guid.NewGuid()],
            TabWidth = 0.05,
            TabCenters = [0.025, 1.5, 3.2],
        });
        return drawing;
    }

    [Fact]
    public void TabbedCutouts_RoundTripEveryField()
    {
        var drawing = WithCutout();
        var original = drawing.TabbedCutouts[0];

        var (restored, warnings) = RoundTrip(Write(drawing));

        Assert.Empty(warnings);
        var cutout = Assert.Single(Assert.Single(restored.Drawings).TabbedCutouts);
        Assert.Equal(original.EdgeIds, cutout.EdgeIds);
        Assert.Equal(0.05, cutout.TabWidth);
        Assert.Equal(original.TabCenters, cutout.TabCenters);
    }

    [Fact]
    public void BuiltTabbedCutout_KeepsItsTabMovesAndStaysEditable()
    {
        var hole = new[] { new Line(3, 3, 7, 3), new Line(7, 3, 7, 7), new Line(7, 7, 3, 7), new Line(3, 7, 3, 3) };
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 10), new Line(10, 10, 0, 10), new Line(0, 10, 0, 0),
        };
        entities.AddRange(hole);
        var drawing = CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "tabbed" },
            entities, new List<Bend>(), 1, null, null, null,
            [new TabbedCutout { EdgeIds = hole.Select(e => e.Id).ToList() }]);

        var (restored, warnings) = RoundTrip(Write(drawing));

        Assert.Empty(warnings);
        var reread = Assert.Single(restored.Drawings);
        Assert.Equal(TabLength(drawing.Program), TabLength(reread.Program), 9);
        Assert.Equal(2 * TabbedCutout.DefaultTabWidth, TabLength(reread.Program), 9);
        Assert.Equal(drawing.Area, reread.Area, 9);
        var layout = TabbedCutoutBuilder.Layout(CadImporter.EditableEntities(reread), Assert.Single(reread.TabbedCutouts));
        Assert.True(layout.IsValid, layout.Error);
    }

    [Fact]
    public void NoTabbedCutouts_AreOmittedFromTheFile()
    {
        var archive = Write(DrawingPackageFixture.Build());

        Assert.False(NestJson(archive)["drawings"]![0]!.AsObject().ContainsKey("tabbedCutouts"));
        Assert.Empty(Assert.Single(RoundTrip(archive).Nest.Drawings).TabbedCutouts);
    }

    [Fact]
    public void UnreadableTabbedCutout_IsDroppedWithAWarning_AndTheDrawingLoads()
    {
        var archive = Write(WithCutout());
        var json = NestJson(archive);
        json["drawings"]![0]!["tabbedCutouts"]![0]!["tabWidth"] = 0;

        var (restored, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.Empty(Assert.Single(restored.Drawings).TabbedCutouts);
        Assert.Contains(warnings, w => w.Contains("tabbed cutout 1 could not be read"));
    }

    // Tab moves may be split or merged by program round trips; their total length is what counts.
    private static double TabLength(OpenNest.CNC.Program program)
    {
        var length = 0.0;
        var position = Vector.Zero;
        foreach (var code in program.Codes.OfType<Motion>())
        {
            var end = program.Mode == Mode.Incremental ? position + code.EndPoint : code.EndPoint;
            if (code is LinearMove { Layer: LayerType.Tab })
                length += position.DistanceTo(end);
            position = end;
        }
        return length;
    }

    private static byte[] Write(Drawing drawing)
    {
        var nest = new Nest(drawing.Name) { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }
}

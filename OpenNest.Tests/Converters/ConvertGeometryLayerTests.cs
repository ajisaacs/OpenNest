using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Converters;

public class ConvertGeometryLayerTests
{
    private static Program ProgramFor(Entity entity)
    {
        var shape = new Shape();
        shape.Entities.Add(entity);
        return ConvertGeometry.ToProgram(shape);
    }

    [Fact]
    public void AddLine_EngraveLayer_TagsScribe()
    {
        var line = new Line(0, 0, 1, 0) { Layer = new Layer("ENGRAVE") };

        var pgm = ProgramFor(line);

        Assert.All(pgm.Codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Scribe, m.Layer));
    }

    [Fact]
    public void AddLine_EtchLayer_TagsScribe()
    {
        var line = new Line(0, 0, 1, 0) { Layer = new Layer("etch") };

        var pgm = ProgramFor(line);

        Assert.All(pgm.Codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Scribe, m.Layer));
    }

    [Fact]
    public void AddArc_EngraveLayer_TagsScribe()
    {
        var arc = new Arc(new Vector(0, 0), 1, 0, System.Math.PI / 2) { Layer = new Layer("ENGRAVE") };

        var pgm = ProgramFor(arc);

        var arcs = pgm.Codes.OfType<ArcMove>().ToList();
        Assert.NotEmpty(arcs);
        Assert.All(arcs, m => Assert.Equal(LayerType.Scribe, m.Layer));
    }

    [Fact]
    public void AddCircle_EngraveLayer_TagsScribe()
    {
        var circle = new Circle(0, 0, 1) { Layer = new Layer("ENGRAVE") };

        var pgm = ProgramFor(circle);

        var arcs = pgm.Codes.OfType<ArcMove>().ToList();
        Assert.NotEmpty(arcs);
        Assert.All(arcs, m => Assert.Equal(LayerType.Scribe, m.Layer));
    }

    [Fact]
    public void AddLine_DefaultLayer_StaysCut()
    {
        var line = new Line(0, 0, 1, 0) { Layer = new Layer("0") };

        var pgm = ProgramFor(line);

        Assert.All(pgm.Codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Cut, m.Layer));
    }
}

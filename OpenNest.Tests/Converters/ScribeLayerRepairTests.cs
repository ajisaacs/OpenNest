using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Converters;

public class ScribeLayerRepairTests
{
    // Mirrors a PEP-exported drawing: incremental program whose first rapid was moved to the
    // origin (Source.Offset), with an etch tick saved as a plain cut move.
    private static readonly Vector Offset = new(5, 2);

    private static Program RectangleWithCutEtch()
    {
        var program = new Program(Mode.Incremental);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(1, 0)); // etch tick, source (5,2)->(6,2)
        program.Codes.Add(new RapidMove(-5, -2));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(-10, 0));
        program.Codes.Add(new LinearMove(0, -10));
        return program;
    }

    private static List<Entity> SourceEntities() =>
        new()
        {
            new Line(5, 2, 6, 2) { Layer = SpecialLayers.Scribe },
            new Line(0, 0, 10, 0),
            new Line(10, 0, 10, 10),
            new Line(10, 10, 0, 10),
            new Line(0, 10, 0, 0),
        };

    [Fact]
    public void RestoresScribeOnMovesThatLieOnMarkEntities()
    {
        var program = RectangleWithCutEtch();

        var repaired = ScribeLayerRepair.Apply(program, SourceEntities(), Offset);

        var lines = program.Codes.OfType<LinearMove>().ToList();
        Assert.Equal(1, repaired);
        Assert.Equal(LayerType.Scribe, lines[0].Layer);
        Assert.All(lines.Skip(1), m => Assert.Equal(LayerType.Cut, m.Layer));
    }

    [Fact]
    public void RecognizesRawEtchLayerNames()
    {
        var program = RectangleWithCutEtch();
        var entities = SourceEntities();
        entities[0].Layer = new Layer("etch");

        Assert.Equal(1, ScribeLayerRepair.Apply(program, entities, Offset));
    }

    [Fact]
    public void LeavesProgramAloneWithoutMarkEntities()
    {
        var program = RectangleWithCutEtch();

        var repaired = ScribeLayerRepair.Apply(program, SourceEntities().Skip(1), Offset);

        Assert.Equal(0, repaired);
        Assert.All(program.Codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Cut, m.Layer));
    }

    [Fact]
    public void WrongOffsetMatchesNothing()
    {
        var program = RectangleWithCutEtch();

        Assert.Equal(0, ScribeLayerRepair.Apply(program, SourceEntities(), new Vector(0, 0)));
    }
}

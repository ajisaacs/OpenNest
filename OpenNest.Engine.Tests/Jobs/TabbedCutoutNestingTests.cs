using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using OpenNest.TabbedCutouts;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// A 20 x 20 frame with a 10 x 10 hole and a 6 x 6 insert on a 22 x 22 sheet: the insert only
/// fits inside the hole, which a tabbed cutout's attached slug fills.
/// </summary>
[Collection("Cutout pipeline registry")]
public class TabbedCutoutNestingTests
{
    private static Program Frame(bool tabbed)
    {
        var hole = new Entity[] { new Line(5, 5, 15, 5), new Line(15, 5, 15, 15), new Line(15, 15, 5, 15), new Line(5, 15, 5, 5) };
        var entities = new List<Entity>
        {
            new Line(0, 0, 20, 0), new Line(20, 0, 20, 20), new Line(20, 20, 0, 20), new Line(0, 20, 0, 0),
        };
        entities.AddRange(hole);
        if (tabbed)
            entities = TabbedCutoutBuilder.Apply(entities,
                [new TabbedCutout { EdgeIds = hole.Select(e => e.Id).ToList() }], out _);
        return ConvertGeometry.ToProgram(ShapeProfile.NormalizeEntities(entities));
    }

    private static NestPipelineRequest Request(string engine, bool tabbed) =>
        new(engine,
            new[] { new NestItem { Drawing = new Drawing("frame", Frame(tabbed)), Quantity = 1 },
                new NestItem { Drawing = new Drawing("insert", TestDrawingFactory.Rectangle(6, 6)), Quantity = 1 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) },
            new NestJobOptions(maxPlates: 1));

    [Fact]
    public void TabbedHole_IsNoCutoutForNesting_AndItsSlugCountsAsMaterial()
    {
        var geometry = JobPartGeometry.Read(DrawingJobMapper.FromDrawing("frame", new Drawing("frame", Frame(true)), 1).Geometry);

        Assert.Empty(geometry.Cutouts);
        Assert.Equal(400, geometry.MaterialArea, 6);
        Assert.Single(JobPartGeometry.Read(
            DrawingJobMapper.FromDrawing("frame", new Drawing("frame", Frame(false)), 1).Geometry).Cutouts);
    }

    [Fact]
    public void TabbedHole_KeepsItsHoleForAreaAndCutting()
    {
        var drawing = new Drawing("frame", Frame(true));

        Assert.Equal(300, drawing.Area, 6);
        // Nesting sees only the frame's outline: the hole's pieces and tabs are gone.
        var nesting = ConvertProgram.ToGeometry(drawing.Program).ForNesting();
        Assert.Equal(4, nesting.Count);
        Assert.Equal(400, new ShapeProfile(nesting).Perimeter.Area(), 6);
    }

    [Fact]
    public void UntabbedEntities_PassThroughUnchanged()
    {
        var material = ConvertProgram.ToGeometry(Frame(false));

        Assert.Equal(material.Where(e => SpecialLayers.IsMaterial(e.Layer)), material.ForNesting());
    }

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void NoPartIsNestedInsideATabbedHole(string engine)
    {
        var plain = NestPipeline.Run(Request(engine, tabbed: false));
        Assert.Equal(NestJobStatus.Complete, plain.Status);

        var result = NestPipeline.Run(Request(engine, tabbed: true));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.DoesNotContain(result.Plates.SelectMany(p => p.Parts), p => p.BaseDrawing.Name == "insert");
    }
}

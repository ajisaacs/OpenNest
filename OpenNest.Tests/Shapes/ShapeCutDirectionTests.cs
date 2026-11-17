using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.CNC.Posting;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

/// <summary>
/// Shape library drawings cut in the CAD converter's directions: the outside clockwise and
/// every cutout counter-clockwise.
/// </summary>
public class ShapeCutDirectionTests
{
    public static TheoryData<ShapeDefinition> Shapes()
    {
        var shapes = new ShapeDefinition[]
        {
            new AngleShape(),
            new ChannelShape(),
            new CircleShape(),
            new IsoscelesTriangleShape(),
            new LShape(),
            new NgonShape(),
            new PipeFlangeShape(),
            new PipeFlangeShape { Blind = true },
            new RectangleShape(),
            new RightTriangleShape(),
            new RingShape(),
            new RolledPlateShape(),
            new RoundedRectangleShape(),
            new TShape(),
            new TrapezoidShape(),
        };

        var data = new TheoryData<ShapeDefinition>();
        foreach (var shape in shapes)
        {
            shape.SetPreviewDefaults();
            data.Add(shape);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void GetDrawing_CutsOutsideClockwiseAndCutoutsCounterClockwise(ShapeDefinition shape)
    {
        AssertConverterWinding(shape.GetDrawing().Program, shape.Name);
    }

    /// <summary>
    /// Opening a shape drawing in the CAD Converter and saving it rebuilds it from its program
    /// geometry; the cut directions must come back unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void OpenInConverter_KeepsTheShapesCutDirections(ShapeDefinition shape)
    {
        var drawing = shape.GetDrawing();
        var editable = CadImporter.EditableEntities(drawing);

        var rebuilt = CadImporter.BuildDrawing(
            new CadImportResult { Entities = editable, Name = drawing.Name }, editable,
            new List<Bend>(), 1, null, null);

        Assert.Equal(Directions(drawing.Program), Directions(rebuilt.Program));
        AssertConverterWinding(rebuilt.Program, shape.Name);
    }

    private static void AssertConverterWinding(Program program, string name)
    {
        var contours = PostContours.Extract(program, Vector.Zero);
        var perimeter = ContourWinding.PerimeterIndex(contours);

        Assert.True(perimeter >= 0, $"{name}: no perimeter");
        for (var i = 0; i < contours.Count; i++)
        {
            // Marks such as etched bend lines are open: they have no winding.
            if (contours[i].IsMark)
                continue;
            var area = ContourWinding.SignedArea(contours[i]);
            if (i == perimeter)
                Assert.True(area < 0, $"{name}: outside is not clockwise");
            else
                Assert.True(area > 0, $"{name}: cutout {i} is not counter-clockwise");
        }
    }

    /// <summary>Each contour's winding sign, in a stable order so reordering does not matter.</summary>
    private static List<int> Directions(Program program) =>
        PostContours.Extract(program, Vector.Zero)
            .Where(c => !c.IsMark)
            .Select(c => System.Math.Sign(ContourWinding.SignedArea(c)))
            .OrderBy(s => s)
            .ToList();

    [Fact]
    public void PipeFlange_HasCutouts()
    {
        var shape = new PipeFlangeShape();
        shape.SetPreviewDefaults();

        var contours = PostContours.Extract(shape.GetDrawing().Program, Vector.Zero);

        Assert.True(contours.Count > 1);
    }
}

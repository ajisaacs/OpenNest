using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.TabbedCutouts;

namespace OpenNest.Tests;

/// <summary>
/// Editing placed drawings in the converter rebuilds their programs around a new origin (the first
/// pierce). Placed parts must stay where they were on the plate, whatever the edit did to that origin.
/// </summary>
public class ConverterEditPlacementTests
{
    [Fact]
    public void TabbingAHoleInALegacyDrawing_KeepsPlacedPartsInPlace()
    {
        // Like drawings converted from CNC programs: no source entities, no source offset,
        // and a program origin away from every pierce.
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(-1.049212, -43.0103)));
        program.Codes.Add(new ArcMove(new Vector(-1.049212, -43.0103), new Vector(-1.049212, -42.7603)));
        program.Codes.Add(new RapidMove(new Vector(3.555639, -20.6378)));
        program.Codes.Add(new ArcMove(new Vector(3.555639, -20.6378), new Vector(2.276111, -20.6378)));
        program.Codes.Add(new RapidMove(new Vector(6.503735, -15.1378)));
        program.Codes.Add(new LinearMove(new Vector(6.503735, -67.5753)));
        program.Codes.Add(new LinearMove(new Vector(-1.946512, -67.5753)));
        program.Codes.Add(new LinearMove(new Vector(-1.946512, -15.1378)));
        program.Codes.Add(new LinearMove(new Vector(6.503735, -15.1378)));
        var drawing = new Drawing("4980 A80-10 PT01", program);
        Assert.Equal(Vector.Zero, drawing.Source.Offset);

        var parts = new[]
        {
            Place(drawing, 3 * Angle.HalfPI, new Vector(52.6875, 35.9414)),
            Place(drawing, Angle.ToRadians(37), new Vector(20, 10)),
        };

        ConverterEdit(drawing, entities =>
        {
            var hole = entities.OfType<Circle>().Single(c => c.Radius.IsEqualTo(1.279528));
            return new TabbedCutout { EdgeIds = [hole.Id] };
        }, parts, out var tabbed);

        Assert.Single(tabbed.TabbedCutouts);
        Assert.NotEqual(Vector.Zero, tabbed.Source.Offset);
    }

    [Fact]
    public void HidingTheFirstCutHole_KeepsPlacedPartsInPlace()
    {
        var firstHole = new Circle(29, 15, 0.5);
        var entities = new List<Entity>
        {
            new Line(20, 10, 32, 10),
            new Line(32, 10, 32, 18),
            new Line(32, 18, 20, 18),
            new Line(20, 18, 20, 10),
            firstHole,
            new Circle(23, 13, 1),
        };
        var result = new CadImportResult { Entities = entities, Name = "Plate", SourcePath = "plate.dxf" };
        var drawing = CadImporter.BuildDrawing(result, entities, result.Bends, 1, null, null);
        var before = drawing.Source.Offset;
        var parts = new[]
        {
            Place(drawing, 0, new Vector(5, 5)),
            Place(drawing, Angle.HalfPI, new Vector(40, 5)),
            Place(drawing, Angle.ToRadians(37), new Vector(70, 20)),
        };

        ConverterEdit(drawing, editable =>
        {
            editable.Single(e => e.Id == firstHole.Id).IsVisible = false;
            return null;
        }, parts, out var edited);

        Assert.NotEqual(before, edited.Source.Offset);
    }

    private static Part Place(Drawing drawing, double rotation, Vector location)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Location = location;
        return part;
    }

    /// <summary>
    /// Runs the desktop's converter round trip on <paramref name="drawing"/> (snapshot, editable
    /// entities, rebuild, in-place apply, part update) and asserts every part's placed bounds held.
    /// </summary>
    private static void ConverterEdit(
        Drawing drawing,
        Func<List<Entity>, TabbedCutout> edit,
        Part[] parts,
        out Drawing edited)
    {
        var plate = new Plate(120, 60);
        foreach (var part in parts)
            plate.Parts.Add(part);
        var expected = parts.Select(p => p.BoundingBox).ToArray();
        var snapshot = DrawingProgramSnapshot.Capture(
            new[] { drawing },
            program => NestWriter.GetProgramText(program) + "\0" + NestWriter.GetSubProgramsText(program));

        var editable = CadImporter.EditableEntities(drawing);
        var cutout = edit(editable);
        // Derived program entities sit on SpecialLayers.Cut, whose visibility the converter's layer list owns.
        var visible = editable.Where(e => e.IsVisible).ToList();
        edited = CadImporter.BuildDrawing(
            new CadImportResult { Entities = editable, Name = drawing.Name }, visible,
            new List<OpenNest.Bending.Bend>(), 1, null, null, null,
            cutout == null ? null : [cutout]);
        CadImporter.ApplyEdit(drawing, edited);

        Assert.Equal(parts, snapshot.UpdateChangedParts(new[] { plate }));
        for (var i = 0; i < parts.Length; i++)
        {
            var actual = parts[i].BoundingBox;
            Assert.Equal(expected[i].X, actual.X, 6);
            Assert.Equal(expected[i].Y, actual.Y, 6);
            Assert.Equal(expected[i].Length, actual.Length, 6);
            Assert.Equal(expected[i].Width, actual.Width, 6);
        }
    }
}

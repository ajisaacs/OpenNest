using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests;

internal static class TestNests
{
    /// <summary>A nest with one 48x96 plate holding one 10x10 square part.</summary>
    public static Nest OneSquare()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 10)));
        program.Codes.Add(new LinearMove(new Vector(0, 10)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));

        var drawing = new Drawing("square", program);
        drawing.Quantity.Required = 1;

        var plate = new Plate(new Size(48, 96));
        plate.Parts.Add(new Part(drawing));

        var nest = new Nest { Name = "test" };
        nest.Drawings.Add(drawing);
        nest.Plates.Add(plate);
        return nest;
    }
}

using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests;

internal static class TestHelpers
{
    public static Part MakePartAt(double x, double y, double size = 1)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    public static Plate MakePlate(double width = 60, double length = 120, params Part[] parts)
    {
        var plate = new Plate(width, length);
        foreach (var p in parts)
            plate.Parts.Add(p);
        return plate;
    }
}

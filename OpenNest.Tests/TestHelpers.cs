using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

internal static class TestHelpers
{
    public static Part MakePartAt(double x, double y, double size = 1)
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
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

    public static Drawing MakeSquareDrawing(double size = 10)
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("square", pgm);
    }

    public static Drawing MakeLShapeDrawing()
    {
        // CW winding matches CNC convention (OffsetSide.Left = outward)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(5, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 5)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("lshape", pgm);
    }
}

using OpenNest.CNC;

namespace OpenNest.Engine.Tests.NestingEngines;

public static class Shapes
{
    public static Program Polyline(params (double X, double Y)[] points)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1))
            program.Codes.Add(new LinearMove(x, y));
        program.Codes.Add(new LinearMove(points[0].X, points[0].Y));
        return program;
    }

    public static Program Rectangle(double w, double h) => Polyline((0, 0), (w, 0), (w, h), (0, h));

    public static Program Triangle(double w, double h) => Polyline((0, 0), (w, 0), (w * 0.3, h));

    public static Program LShape(double w, double h, double t) => Polyline((0, 0), (w, 0), (w, t), (t, t), (t, h), (0, h));

    public static Program Disc(double r)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(r, 0));
        program.Codes.Add(new ArcMove(-r, 0, 0, 0, RotationType.CCW));
        program.Codes.Add(new ArcMove(r, 0, 0, 0, RotationType.CCW));
        return program;
    }

    /// <summary>Stadium: two semicircular ends joined by straight sides, offset from the origin.</summary>
    public static Program Obround(double length, double width)
    {
        var r = width / 2;
        var program = new Program();
        program.Codes.Add(new RapidMove(1 + r, 1));
        program.Codes.Add(new LinearMove(1 + length - r, 1));
        program.Codes.Add(new ArcMove(1 + length - r, 1 + width, 1 + length - r, 1 + r, RotationType.CCW));
        program.Codes.Add(new LinearMove(1 + r, 1 + width));
        program.Codes.Add(new ArcMove(1 + r, 1, 1 + r, 1 + r, RotationType.CCW));
        return program;
    }
    public static Program NotchedPartWithEtch()
    {
        var p = new Program();
        p.MoveTo(0, 0); p.LineTo(10, 0); p.LineTo(10, 4); p.LineTo(8, 4); p.LineTo(8, 6);
        p.LineTo(10, 6); p.LineTo(10, 10); p.LineTo(0, 10); p.LineTo(0, 0);
        p.MoveTo(7.5, 5);
        p.Codes.Add(new LinearMove(9, 5) { Layer = LayerType.Scribe });
        return p;
    }

    public static Program Ring(double outerDiameter, double innerDiameter) =>
        new OpenNest.Shapes.RingShape { OuterDiameter = outerDiameter, InnerDiameter = innerDiameter }.GetDrawing().Program;
}

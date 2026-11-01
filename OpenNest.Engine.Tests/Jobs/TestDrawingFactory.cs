using OpenNest.CNC;

namespace OpenNest.Engine.Tests.Jobs;

internal static class TestDrawingFactory
{
    public static Program Rectangle(double width = 10, double length = 20)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, length);
        program.LineTo(0, length);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>A 6 x 4 L: a 6 x 2 base with a 3 x 2 block on its right half.</summary>
    public static Program LShape()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 4);
        program.LineTo(3, 4);
        program.LineTo(3, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>D-shape: a 3 x 5 block closed on the right by a semicircular arc.</summary>
    public static Program ArcPart()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 0);
        program.ArcTo(3, 5, 3, 2.5, RotationType.CCW);
        program.LineTo(0, 5);
        program.LineTo(0, 0);
        return program;
    }
}

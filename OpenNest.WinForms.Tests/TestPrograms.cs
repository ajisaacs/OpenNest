using OpenNest.CNC;

namespace OpenNest.WinForms.Tests;

/// <summary>Part programs the desktop tests share.</summary>
internal static class TestPrograms
{
    /// <summary>A width x height rectangle from the origin, counterclockwise: +X first.</summary>
    public static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, height);
        program.LineTo(0, height);
        program.LineTo(0, 0);
        return program;
    }

    /// <summary>A size x size square from the origin, counterclockwise: +X first.</summary>
    public static Program Square(double size) => Rectangle(size, size);

    /// <summary>A size x size square from the origin, clockwise: up first.</summary>
    public static Program ClockwiseSquare(double size)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, size);
        program.LineTo(size, size);
        program.LineTo(size, 0);
        program.LineTo(0, 0);
        return program;
    }
}

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
}

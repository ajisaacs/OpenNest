using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class DrawingProgramChangedTests
{
    private static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(width, 0)));
        program.Codes.Add(new LinearMove(new Vector(width, height)));
        program.Codes.Add(new LinearMove(new Vector(0, height)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return program;
    }

    [Fact]
    public void InPlaceEditIsStaleUntilProgramChangedRefreshesAreaAndAngle()
    {
        var drawing = new Drawing("r", Rectangle(100, 50));
        Assert.Equal(5000, drawing.Area, 6);
        Assert.Equal(0.0, drawing.Source.Angle, 6);

        // Neither edit goes through the Program setter.
        foreach (var move in drawing.Program.Codes.OfType<LinearMove>())
            move.EndPoint = new Vector(move.EndPoint.X * 2, move.EndPoint.Y);
        drawing.Program.Rotate(0.5, drawing.Program.BoundingBox().Center);
        Assert.Equal(5000, drawing.Area, 6);
        Assert.Equal(0.0, drawing.Source.Angle, 6);

        drawing.ProgramChanged();

        Assert.Equal(10000, drawing.Area, 6);
        Assert.InRange(drawing.Source.Angle, -0.52, -0.48);
    }

    [Fact]
    public void ClearingTheMaterialZeroesTheArea()
    {
        var drawing = new Drawing("r", Rectangle(100, 50));

        foreach (var move in drawing.Program.Codes.OfType<LinearMove>())
            move.Layer = LayerType.Scribe;
        drawing.ProgramChanged();

        Assert.Equal(0.0, drawing.Area);
    }

    [Fact]
    public void AnEmptiedProgramZeroesTheArea()
    {
        var drawing = new Drawing("r", Rectangle(100, 50));

        drawing.Program.Codes.Clear();
        drawing.ProgramChanged();

        Assert.Equal(0.0, drawing.Area);
    }

    [Fact]
    public void ANullProgramZeroesAreaAndAngle()
    {
        var program = Rectangle(100, 50);
        program.Rotate(0.5, program.BoundingBox().Center);
        var drawing = new Drawing("r", program);
        Assert.NotEqual(0.0, drawing.Source.Angle);

        drawing.Program = null;

        Assert.Equal(0.0, drawing.Area);
        Assert.Equal(0.0, drawing.Source.Angle);
    }

    [Fact]
    public void ACutOffDrawingKeepsAngleZero()
    {
        var drawing = new Drawing("cut", Rectangle(100, 50)) { IsCutOff = true };

        drawing.Program.Rotate(0.5, drawing.Program.BoundingBox().Center);
        drawing.ProgramChanged();

        Assert.Equal(0.0, drawing.Source.Angle);
        Assert.Equal(5000, drawing.Area, 6);
    }
}

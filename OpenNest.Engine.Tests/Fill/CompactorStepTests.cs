using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// Compactor.Step moves a selection by its own repeat distance at the part spacing,
/// and like a push never passes the work-area edge or another part.
/// </summary>
public class CompactorStepTests
{
    // Plate: 24 x 24, 0.5 edge spacing, part spacing 1.0; work area 0.5..23.5.
    private static Plate MakePlate()
    {
        var plate = new Plate(new Size(24, 24));
        plate.EdgeSpacing = new Spacing(0.5, 0.5);
        plate.PartSpacing = 1.0;
        return plate;
    }

    private static Program Polygon(params (double X, double Y)[] points)
    {
        var program = new Program();
        program.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
            program.LineTo(points[i].X, points[i].Y);
        program.LineTo(points[0].X, points[0].Y);
        return program;
    }

    private static Part Add(Plate plate, Program program, double x, double y)
    {
        var part = new Part(new Drawing($"p{plate.Parts.Count}", program), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Part AddSquare(Plate plate, double x, double y) =>
        Add(plate, Polygon((0, 0), (3, 0), (3, 3), (0, 3)), x, y);

    [Theory]
    [InlineData(PushDirection.Right, 4, 0)]
    [InlineData(PushDirection.Left, -4, 0)]
    [InlineData(PushDirection.Up, 0, 4)]
    [InlineData(PushDirection.Down, 0, -4)]
    public void SquareStepsItsSizePlusTheSpacing(PushDirection direction, double dx, double dy)
    {
        var plate = MakePlate();
        var part = AddSquare(plate, 10, 10);

        var moved = Compactor.Step(new List<Part> { part }, plate, direction);

        Assert.Equal(4, moved, 3);
        Assert.Equal(10 + dx, part.Location.X, 3);
        Assert.Equal(10 + dy, part.Location.Y, 3);
    }

    [Fact]
    public void ShapeThatNestsIntoItsCopyStepsLessThanItsSize()
    {
        // Parallelogram 6 wide whose slanted sides run along (2, 3): a copy beside it
        // needs only sqrt(13)/3 beyond the 4-unit base for a 1-unit gap between them.
        var plate = MakePlate();
        var part = Add(plate, Polygon((0, 0), (4, 0), (6, 3), (2, 3)), 2, 10);

        var moved = Compactor.Step(new List<Part> { part }, plate, PushDirection.Right);

        Assert.Equal(4 + System.Math.Sqrt(13) / 3, moved, 2);
    }

    [Fact]
    public void GroupStepsAsOnePatternKeepingItsOwnSpacing()
    {
        var plate = MakePlate();
        var a = AddSquare(plate, 2, 10);
        var b = AddSquare(plate, 6, 10);

        var moved = Compactor.Step(new List<Part> { a, b }, plate, PushDirection.Right);

        Assert.Equal(8, moved, 3);
        Assert.Equal(10, a.Location.X, 3);
        Assert.Equal(14, b.Location.X, 3);
    }

    [Fact]
    public void StopsAtAnotherPartInsteadOfPassingIt()
    {
        var plate = MakePlate();
        var part = AddSquare(plate, 2, 10);
        AddSquare(plate, 7, 10); // 2 clear of the mover: it may close 1 at 1.0 spacing.

        var moved = Compactor.Step(new List<Part> { part }, plate, PushDirection.Right);

        Assert.Equal(1, moved, 3);
    }

    [Fact]
    public void StopsAtTheWorkAreaEdge()
    {
        var plate = MakePlate();
        var part = AddSquare(plate, 18, 10); // Right side at 21, 2.5 from the work area's.

        var moved = Compactor.Step(new List<Part> { part }, plate, PushDirection.Right);

        Assert.Equal(2.5, moved, 3);
    }
}

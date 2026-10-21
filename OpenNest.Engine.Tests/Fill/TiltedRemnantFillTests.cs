using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A long part fills a 60 x 120 plate four high at 90 degrees, leaving a 55 x 59 strip too
/// narrow for it upright. Two more fit tilted, one with a corner tucked beside the grid in the
/// strip its slack leaves (a reported nest that Fill left at four).
/// </summary>
public class TiltedRemnantFillTests
{
    private static Plate LongPartPlate() =>
        new(new Size(60, 120))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.25, 0.75, 0.25, 0.25),
        };

    /// <summary>
    /// The reported part's outline: 11.86 x 63.99 with corner reliefs. The tucked corner needs
    /// its relief; a plain rectangle of the same size only fits one tilted part.
    /// </summary>
    private static Drawing LongPart()
    {
        var steps = new (double X, double Y)[]
        {
            (0, -61.859322), (-0.895, 0), (-0.265621, 0.097161), (0.097161, -0.265622),
            (0, -0.895), (-9.734323, 0), (0, 0.895), (0.097162, 0.265622),
            (-0.265622, -0.097161), (-0.895, 0), (0, 61.859322), (0.895, 0),
            (0.265622, -0.097161), (-0.097162, 0.265621), (0, 0.895), (9.734323, 0),
            (0, -0.895), (-0.097161, -0.265621), (0.265621, 0.097161), (0.895, 0),
        };

        // Start at the outline's top-right relief so every point is non-negative.
        double x = 11.861, y = 62.923;
        var program = new CNC.Program();
        program.MoveTo(x, y);
        foreach (var (dx, dy) in steps)
            program.LineTo(x += dx, y += dy);

        return new Drawing("long part", program);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    public void FillAddsTiltedPartsBesideTheGrid(int quantity, int expected)
    {
        var plate = LongPartPlate();
        var drawing = LongPart();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = quantity }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(expected, parts.Count);
        Assert.Contains(parts, p => !IsSquare(p.Rotation));
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("long part", expected) }));
    }

    [Fact]
    public void ExplicitRotationRangeKeepsPartsSquare()
    {
        var plate = LongPartPlate();
        var drawing = LongPart();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, RotationStart = 0, RotationEnd = Angle.HalfPI, StepAngle = Angle.HalfPI },
            plate.WorkArea(), null, CancellationToken.None);

        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.True(IsSquare(p.Rotation)));
    }

    [Fact]
    public void RemnantThatFitsThePartUprightIsLeftToTheOtherStrategies()
    {
        var plate = new Plate(new Size(60, 80)) { PartSpacing = 0.25 };
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle(10, 20));
        var workArea = plate.WorkArea();
        var layout = new List<Part> { new(drawing, new Vector(workArea.X, workArea.Y)) };

        var result = new TiltedRemnantFiller(workArea, plate.PartSpacing, new DefaultFillComparer())
            .Fill(drawing, layout, CancellationToken.None);

        Assert.Null(result);
    }

    private static bool IsSquare(double rotation)
    {
        var quarterTurns = rotation / Angle.HalfPI;
        return System.Math.Abs(quarterTurns - System.Math.Round(quarterTurns)) < 1e-6;
    }
}

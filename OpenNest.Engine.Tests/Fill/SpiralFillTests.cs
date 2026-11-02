using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: 9 x 6.5 parts with two small chamfers on a 60 x 120 plate. The least-waste
/// rows and columns make a spiral that overlaps itself, so Fill stopped at two blocks and 109
/// parts, while a slacker spiral of four blocks and a center holds 112.
/// </summary>
public class SpiralFillTests
{
    private static Plate PinwheelPlate() =>
        new(new Size(60, 120))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25),
        };

    private static Drawing ChamferedPart()
    {
        double x = 9, y = 0;
        var program = new CNC.Program();
        program.MoveTo(x, y);
        foreach (var (dx, dy) in new (double, double)[]
            { (-8.5, 0), (-0.5, 0.5), (0, 6), (8.5, 0), (0.5, -0.5), (0, -6) })
            program.LineTo(x += dx, y += dy);

        return new Drawing("chamfered part", program);
    }

    [Fact]
    public void FillFindsTheSpiralTheLeastWasteCombinationCannotMake()
    {
        var plate = PinwheelPlate();
        var drawing = ChamferedPart();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(112, parts.Count);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("chamfered part", 112) }));
    }
}

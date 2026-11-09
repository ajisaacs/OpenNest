using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: 8" gussets (right triangles with clipped corners and a 2.2" hole) on a
/// 16.85 x 60 strip. Fill tiled 7 gusset pairs in a 10.35" column (14) and left a strip too
/// narrow for a gusset square on. Sliding each pair along its hypotenuse so 6 span the height
/// narrows the column to 8.75" and leaves a strip that holds 8 more turned 45 degrees (20), as
/// the user laid it out by hand.
/// </summary>
public class GussetColumnFillTests
{
    private static Plate GussetPlate() =>
        new(new Size(60, 16.85))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.5, 0.25, 0.5, 0.25),
        };

    private static Drawing Gusset()
    {
        var program = new Program();
        program.MoveTo(3.7, 2.6);
        program.ArcTo(new Vector(3.7, 2.6), new Vector(2.6, 2.6), RotationType.CCW);
        program.MoveTo(0, 0);
        foreach (var (x, y) in new[] { (0.0, 8.0), (1.0, 8.0), (8.0, 1.0), (8.0, 0.0), (0.0, 0.0) })
            program.LineTo(x, y);
        return new Drawing("Gusset", program);
    }

    [Theory]
    [InlineData("Fill")]
    [InlineData("Vertical Remnant")]
    public void FillSlidesThePairColumnToFitTurnedGussetsBesideIt(string strategy)
    {
        var plate = GussetPlate();
        var drawing = Gusset();

        var parts = PlateFillService.FillItem(strategy, plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(20, parts.Count);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("Gusset", 20) }));
    }
}

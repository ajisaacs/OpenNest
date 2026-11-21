using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: 30 x 7.5 J-shaped bars (a 3.6" bar with a 2" leg at one end) on a 48 x 96
/// plate. Pairs stack bar on bar with each leg tucked just past the other bar's end. Best fit
/// sampled the pair's offset along the bars a quarter inch apart and landed 0.2" short of the
/// leg's contact, so two columns of pairs left a 29.8" strip, too short for a 30" bar: Fill
/// placed 30. With the pair settled against both contacts the strip is 30.2" and holds nine
/// more, as laid out by hand (33): three pairs stood on end, one more bar dropped 2.3" beside
/// them so its leg passes under its neighbor's, and a pair lying under all seven.
/// </summary>
public class JBarFillTests
{
    private static Plate JBarPlate() =>
        new(new Size(48, 96))
        {
            PartSpacing = 0.3,
            EdgeSpacing = new Spacing(0.3, 0.75, 0.3, 0.25),
        };

    /// <summary>The reported drawing's outline, in absolute coordinates.</summary>
    private static Drawing JBar()
    {
        var program = new Program();
        program.MoveTo(-2.21875, -2.5126780932);
        program.LineTo(-2.21875, 0.625);
        program.ArcTo(new Vector(-1.96875, 0.875), new Vector(-1.96875, 0.625), RotationType.CW);
        program.LineTo(27.53125, 0.875);
        program.ArcTo(new Vector(27.78125, 0.625), new Vector(27.53125, 0.625), RotationType.CW);
        program.LineTo(27.78125, -5.625);
        program.ArcTo(new Vector(25.78125, -5.625), new Vector(26.78125, -5.625), RotationType.CW);
        program.LineTo(25.78125, -0.875);
        program.LineTo(25.71875, -0.875);
        program.LineTo(25.71875, -2.5126780932);
        program.ArcTo(new Vector(25.46875, -2.7626780932), new Vector(25.46875, -2.5126780932), RotationType.CW);
        program.LineTo(-1.96875, -2.7626780932);
        program.ArcTo(new Vector(-2.21875, -2.5126780932), new Vector(-1.96875, -2.5126780932), RotationType.CW);
        return new Drawing("J-Bar", program);
    }

    [Fact]
    public void BestFitSettlesThePairAgainstBothContacts()
    {
        var fits = new BestFitFinder(48, 96).FindBestFits(JBar(), spacing: 0.3);

        // Leg 2" + spacing 0.3" past a 30" bar: 32.3" end to end, not 32.5".
        var tightest = fits
            .Where(f => f.Keep && System.Math.Min(f.BoundingWidth, f.BoundingHeight) < 8)
            .Min(f => System.Math.Max(f.BoundingWidth, f.BoundingHeight));
        Assert.Equal(32.3, tightest, 4);
    }

    [Fact]
    public void FillLeavesAStripThatHoldsNineMore()
    {
        var plate = JBarPlate();
        var drawing = JBar();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(33, parts.Count);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("J-Bar", 33) }));
    }
}

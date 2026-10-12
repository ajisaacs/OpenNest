using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// A template plate following the machine clamp rail offers every stock size with the rail
/// resolved for that size (D2): no clamp beyond a shorter sheet, every clamp on a longer
/// one, measured from the origin in every quadrant.
/// </summary>
public class ClampRailStockTests
{
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private static Plate RailTemplate(int quadrant = 1)
    {
        var template = new Plate(48, 120) { Quadrant = quadrant, EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25) };
        template.SetClampRail(new ClampRail(1.5, 0.625, MachineCenters));
        return template;
    }

    private static PlateOption[] Options() =>
    [
        new PlateOption { Width = 48, Length = 96 },
        new PlateOption { Width = 60, Length = 144 },
        new PlateOption { Width = 48, Length = 60 },
    ];

    [Fact]
    public void EachOptionGetsTheRailResolvedForItsOwnSize()
    {
        var stock = NestStockBuilder.FromTemplate(RailTemplate(), Options());

        Assert.Equal(new[] { 7, 8, 4 }, stock.Select(s => s.ClampZones.Count));
        Assert.Equal(new[] { 5.25, 20.25, 35.25, 50.25 }, stock[2].ClampZones.Select(z => z.Bounds.X));
        // Every offered sheet passes job validation: no clamp lies outside its sheet.
        NestJobValidator.Validate(new NestJob([], stock));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OptionsResolveFromTheOriginWhenSheetsExtendToNegativeX(int quadrant)
    {
        var stock = NestStockBuilder.FromTemplate(RailTemplate(quadrant), Options());

        foreach (var sheet in stock)
        {
            // Placement frame: the first clamp is 6" from the origin, toward -X.
            var first = sheet.ClampZones.Single(z => z.Id == "clamp-1").Bounds;
            Assert.Equal(-6.0, first.Center.X, 9);
        }
        NestJobValidator.Validate(new NestJob([], stock));
    }

    [Fact]
    public void SingleSheetStockCarriesTheTemplatesRailZones()
    {
        var template = RailTemplate();

        var single = Assert.Single(NestStockBuilder.SinglePlate(template));

        Assert.Equal(8, single.ClampZones.Count);
        Assert.Equal(template.ClampZones.Select(z => z.Bounds.X), single.ClampZones.Select(z => z.Bounds.X));
    }
}

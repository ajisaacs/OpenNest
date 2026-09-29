using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestStockBuilderTests
{
    private static Plate Template() => new(new Size(48, 96))
    {
        Quantity = 7,
        PartSpacing = 0.25,
        EdgeSpacing = new Spacing(1, 2, 3, 4),
        Quadrant = 3,
    };

    [Fact]
    public void TemplateWithoutOptionsUsesUnlimitedStockAndSnapshotsSettings()
    {
        var template = Template();
        var stock = Assert.Single(NestStockBuilder.FromTemplate(template, null));
        Assert.Equal("plate", stock.Id);
        Assert.Null(stock.Quantity);
        Assert.Equal(template.Size, stock.Size);
        Assert.Equal(template.PartSpacing, stock.PartSpacing);
        Assert.Equal(template.EdgeSpacing, stock.EdgeSpacing);
        Assert.Equal(template.Quadrant, stock.Quadrant);
        template.Size = new Size(60, 120);
        template.EdgeSpacing = default;
        Assert.Equal(new Size(48, 96), stock.Size);
        Assert.Equal(new Spacing(1, 2, 3, 4), stock.EdgeSpacing);
    }

    [Fact]
    public void OptionsHaveStableDistinctIdsAndCopyTemplateSettings()
    {
        var template = Template();
        var options = new[]
        {
            new PlateOption { Width = 60, Length = 120 },
            new PlateOption { Width = 72, Length = 144 },
        };
        var stock = NestStockBuilder.FromTemplate(template, options, quantityPerOption: 2);
        Assert.Equal(new[] { "option-0", "option-1" }, stock.Select(s => s.Id));
        Assert.Equal(new[] { new Size(60, 120), new Size(72, 144) }, stock.Select(s => s.Size));
        Assert.All(stock, s =>
        {
            Assert.Equal(2, s.Quantity);
            Assert.Equal(template.EdgeSpacing, s.EdgeSpacing);
            Assert.Equal(template.PartSpacing, s.PartSpacing);
            Assert.Equal(template.Quadrant, s.Quadrant);
        });
        Assert.Equal(new Size(48, 96), template.Size);
    }

    [Fact]
    public void SinglePlateOffersExactlyOneSheetNotPlateRepeatQuantity()
    {
        var stock = Assert.Single(NestStockBuilder.SinglePlate(Template()));
        Assert.Equal(1, stock.Quantity);
    }

    [Fact]
    public void MissingTemplateIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => NestStockBuilder.FromTemplate(null, null));
        Assert.Throws<ArgumentNullException>(() => NestStockBuilder.SinglePlate(null));
    }
}

using OpenNest.Engine.Jobs;
using OpenNest.IO;

namespace OpenNest.IO.Tests;

public class StockCostPersistenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(12.5)]
    public void ExistingOptionsRoundTripWithoutChangingMetadata(double cost)
    {
        var nest = new Nest("priced options")
        {
            Customer = "customer",
            Notes = "notes",
            Units = Units.Inches,
            SalvageRate = 0.25,
            PlateOptions = [new PlateOption { Width = 48, Length = 96, Cost = cost }],
        };
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var reopened = new NestReader(stream).Read();
        Assert.Equal(cost, Assert.Single(reopened.PlateOptions).Cost);
        Assert.Equal(nest.Customer, reopened.Customer);
        Assert.Equal(nest.Notes, reopened.Notes);
        Assert.Equal(nest.SalvageRate, reopened.SalvageRate);
        var stock = Assert.Single(NestStockBuilder.FromTemplate(new Plate(48, 96), reopened.PlateOptions));
        Assert.Equal(cost == 0 ? (double?)null : cost, stock.Cost);
    }
}

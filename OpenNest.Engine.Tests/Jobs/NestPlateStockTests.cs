using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestPlateStockTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void WorkAreaMatchesPlateInEveryQuadrant(int quadrant, double spacing)
    {
        var stock = new NestPlateStock("s", new Size(80, 120),
            edgeSpacing: new Spacing(spacing, 2 * spacing, 3 * spacing, 4 * spacing),
            quadrant: quadrant);
        var actual = stock.WorkArea;
        var expected = DrawingJobMapper.CreatePlate(stock).WorkArea();

        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal((quadrant is 1 or 4 ? 0 : -120) + spacing, actual.X);
        Assert.Equal((quadrant is 1 or 2 ? 0 : -80) + 2 * spacing, actual.Y);
        Assert.Equal(120 - 4 * spacing, actual.Length);
        Assert.Equal(80 - 6 * spacing, actual.Width);
        Assert.Equal(9600, stock.Area);
        actual.Length = 0;
        Assert.Equal(expected.Length, stock.WorkArea.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FractionalWorkAreaPreservesValidatorDoublePrecision(int quadrant)
    {
        var stock = new NestPlateStock("s", new Size(80.123456789, 120.123456789),
            edgeSpacing: new Spacing(0.1, 0.2, 0.3, 0.4), quadrant: quadrant);
        var work = stock.WorkArea;

        // Plate.BoundingBox casts negative origins to float. Keep the validator's original
        // double arithmetic, including subtraction order, instead of adopting that rounding.
        Assert.Equal((quadrant is 1 or 4 ? 0 : -stock.Size.Length) + 0.1, work.X);
        Assert.Equal((quadrant is 1 or 2 ? 0 : -stock.Size.Width) + 0.2, work.Y);
        Assert.Equal(stock.Size.Length - 0.1 - 0.3, work.Length);
        Assert.Equal(stock.Size.Width - 0.2 - 0.4, work.Width);
    }

    [Fact]
    public void FitsUsesXYExtentsAndTolerance()
    {
        var stock = new NestPlateStock("s", new Size(80, 120),
            edgeSpacing: new Spacing(1, 2, 3, 4));

        Assert.True(stock.Fits(116, 74));
        Assert.False(stock.Fits(74, 116));
        Assert.True(stock.Fits(116 + 5e-10, 74 + 5e-10));
        Assert.False(stock.Fits(116 + 2e-9, 74));
        Assert.False(stock.Fits(116, 74 + 2e-9));
        Assert.True(stock.Fits(116.01, 74.01, 0.02));
        Assert.False(stock.Fits(116.01, 74, 0));
    }
}

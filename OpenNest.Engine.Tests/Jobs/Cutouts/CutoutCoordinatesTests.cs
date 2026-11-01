using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutCoordinatesTests
{
    private const double Max = CutoutCoordinates.MaxCoordinate;

    [Fact]
    public void WithinRange_AcceptsTheLimitOnEverySide() =>
        Assert.True(CutoutCoordinates.WithinRange(new Box(-Max, -Max, 2 * Max, 2 * Max)));

    [Theory]
    [InlineData(-2e8, 0, 2e8, 1)] // left only: right edge stays at 0
    [InlineData(0, 0, 2e8, 1)] // right
    [InlineData(0, -2e8, 1, 2e8)] // bottom only: top edge stays at 0
    [InlineData(0, 0, 1, 2e8)] // top
    public void WithinRange_RejectsASideBeyondTheLimit(double x, double y, double w, double h) =>
        Assert.False(CutoutCoordinates.WithinRange(new Box(x, y, w, h)));

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, 0, double.PositiveInfinity, 1)]
    [InlineData(0, double.NegativeInfinity, 1, 1)]
    [InlineData(0, 0, 1, double.NaN)]
    public void WithinRange_RejectsNonFiniteSides(double x, double y, double w, double h) =>
        Assert.False(CutoutCoordinates.WithinRange(new Box(x, y, w, h)));
}

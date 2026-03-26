using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests;

public class GeometrySimplifierTests
{
    [Fact]
    public void FitCircle_PointsOnKnownCircle_ReturnsCorrectCenterAndRadius()
    {
        // 21 points on a semicircle centered at (5, 3) with radius 10
        var center = new Vector(5, 3);
        var radius = 10.0;
        var points = new List<Vector>();
        for (var i = 0; i <= 20; i++)
        {
            var angle = i * System.Math.PI / 20;
            points.Add(new Vector(
                center.X + radius * System.Math.Cos(angle),
                center.Y + radius * System.Math.Sin(angle)));
        }

        var (fitCenter, fitRadius) = GeometrySimplifier.FitCircle(points);

        Assert.InRange(fitCenter.X, 4.999, 5.001);
        Assert.InRange(fitCenter.Y, 2.999, 3.001);
        Assert.InRange(fitRadius, 9.999, 10.001);
    }

    [Fact]
    public void FitCircle_CollinearPoints_ReturnsInvalidCenter()
    {
        // Collinear points should produce degenerate result
        var points = new List<Vector>
        {
            new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)
        };

        var (fitCenter, _) = GeometrySimplifier.FitCircle(points);

        Assert.False(fitCenter.IsValid());
    }
}

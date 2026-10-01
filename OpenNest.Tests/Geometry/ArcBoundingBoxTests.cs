using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// An arc whose sweep is below <c>Tolerance.Epsilon</c> must not be bounded as a full
/// circle: its box covers only the endpoints. Ordinary arcs still reach the cardinal
/// extents they cross.
/// </summary>
public class ArcBoundingBoxTests
{
    private const double Tol = 1e-9;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearZeroSweep_BoundsCoverOnlyTheEndpoints(bool reversed)
    {
        var center = new Vector(10, 20);
        const double radius = 50;
        const double a = 0.3;
        const double b = 0.3 + 1e-6; // sweep well below Tolerance.Epsilon (1e-5)

        var arc = reversed
            ? new Arc(center, radius, b, a, reversed: true)
            : new Arc(center, radius, a, b);

        var x1 = center.X + radius * System.Math.Cos(a);
        var y1 = center.Y + radius * System.Math.Sin(a);
        var x2 = center.X + radius * System.Math.Cos(b);
        var y2 = center.Y + radius * System.Math.Sin(b);
        var box = arc.BoundingBox;

        Assert.Equal(System.Math.Min(x1, x2), box.X, Tol);
        Assert.Equal(System.Math.Min(y1, y2), box.Y, Tol);
        Assert.Equal(System.Math.Max(x1, x2), box.X + box.Length, Tol);
        Assert.Equal(System.Math.Max(y1, y2), box.Y + box.Width, Tol);
    }

    [Fact]
    public void ArcCrossingQuarterTurn_StillReachesTopOfCircle()
    {
        const double radius = 10;
        const double a = 0.1;
        var arc = new Arc(new Vector(0, 0), radius, a, System.Math.PI - a);

        var box = arc.BoundingBox;

        Assert.Equal(-radius * System.Math.Cos(a), box.X, Tol);
        Assert.Equal(radius * System.Math.Sin(a), box.Y, Tol);
        Assert.Equal(radius * System.Math.Cos(a), box.X + box.Length, Tol);
        Assert.Equal(radius, box.Y + box.Width, Tol);
    }
}

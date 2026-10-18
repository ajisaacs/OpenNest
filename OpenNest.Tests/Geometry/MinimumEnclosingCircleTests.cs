using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class MinimumEnclosingCircleTests
{
    [Fact]
    public void SinglePoint_HasZeroRadius()
    {
        var circle = MinimumEnclosingCircle.Compute(new[] { new Vector(3, 4) });

        Assert.Equal(new Vector(3, 4), circle.Center);
        Assert.Equal(0, circle.Radius);
    }

    [Fact]
    public void Square_IsCircumscribed()
    {
        var circle = MinimumEnclosingCircle.Compute(new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10), new Vector(5, 5),
        });

        Assert.Equal(5, circle.Center.X, 9);
        Assert.Equal(5, circle.Center.Y, 9);
        Assert.Equal(System.Math.Sqrt(50), circle.Radius, 9);
    }

    [Fact]
    public void ObtuseTriangle_UsesLongestSideAsDiameter()
    {
        var circle = MinimumEnclosingCircle.Compute(new[] { new Vector(0, 0), new Vector(10, 0), new Vector(5, 1) });

        Assert.Equal(5, circle.Center.X, 9);
        Assert.Equal(0, circle.Center.Y, 9);
        Assert.Equal(5, circle.Radius, 9);
    }

    [Fact]
    public void CollinearPoints_SpanTheEnds()
    {
        var circle = MinimumEnclosingCircle.Compute(new[] { new Vector(0, 0), new Vector(4, 4), new Vector(2, 2), new Vector(1, 1) });

        Assert.Equal(2, circle.Center.X, 9);
        Assert.Equal(2, circle.Center.Y, 9);
        Assert.Equal(System.Math.Sqrt(8), circle.Radius, 9);
    }

    [Fact]
    public void RandomPoints_AreAllCoveredByAMinimalCircle()
    {
        var random = new Random(7);
        var points = Enumerable.Range(0, 500)
            .Select(_ => new Vector(random.NextDouble() * 100 - 50, random.NextDouble() * 40 - 20))
            .ToList();

        var circle = MinimumEnclosingCircle.Compute(points);

        Assert.All(points, p => Assert.True(p.DistanceTo(circle.Center) <= circle.Radius));
        // A minimal circle touches at least two points; a larger one could shrink.
        Assert.True(points.Count(p => circle.Radius - p.DistanceTo(circle.Center) <= 1e-9) >= 2);
    }

    [Fact]
    public void Result_IsRepeatable()
    {
        var points = Enumerable.Range(0, 50).Select(i => new Vector(System.Math.Cos(i * 0.7) * i, System.Math.Sin(i * 1.3) * i)).ToList();

        var first = MinimumEnclosingCircle.Compute(points);
        var second = MinimumEnclosingCircle.Compute(points);

        Assert.Equal(first.Center, second.Center);
        Assert.Equal(first.Radius, second.Radius);
    }
}

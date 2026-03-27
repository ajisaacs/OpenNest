using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests;

public class SplineConverterTests
{
    [Fact]
    public void Convert_SemicirclePoints_ProducesSingleArc()
    {
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 50; i++)
        {
            var t = System.Math.PI * i / 50;
            points.Add(new Vector(10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        Assert.Single(result);
        Assert.IsType<Arc>(result[0]);
        var arc = (Arc)result[0];
        Assert.InRange(arc.Radius, 9.99, 10.01);
    }

    [Fact]
    public void Convert_StraightLinePoints_ProducesSingleLine()
    {
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 10; i++)
            points.Add(new Vector(i, 2 * i + 1));

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        Assert.All(result, e => Assert.IsType<Line>(e));
    }

    [Fact]
    public void Convert_SCurve_ProducesMultipleArcs()
    {
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 30; i++)
        {
            var t = System.Math.PI * i / 30;
            points.Add(new Vector(10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }
        for (var i = 1; i <= 30; i++)
        {
            var t = -System.Math.PI * i / 30;
            points.Add(new Vector(-20 + 10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        var arcCount = result.Count(e => e is Arc);
        Assert.True(arcCount >= 2, $"Expected at least 2 arcs, got {arcCount}");
    }

    [Fact]
    public void Convert_TwoPoints_ProducesSingleLine()
    {
        var points = new System.Collections.Generic.List<Vector>
        {
            new Vector(0, 0),
            new Vector(10, 5)
        };

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        Assert.Single(result);
        Assert.IsType<Line>(result[0]);
    }

    [Fact]
    public void Convert_EndpointContinuity_EntitiesConnect()
    {
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 80; i++)
        {
            var t = Angle.TwoPI * i / 80;
            points.Add(new Vector(15 * System.Math.Cos(t), 8 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        for (var i = 0; i < result.Count - 1; i++)
        {
            var endPt = GetEndPoint(result[i]);
            var startPt = GetStartPoint(result[i + 1]);
            var gap = endPt.DistanceTo(startPt);
            Assert.True(gap < 0.001,
                $"Gap of {gap:F6} between entity {i} and {i + 1}");
        }
    }

    [Fact]
    public void Convert_EmptyPoints_ReturnsEmpty()
    {
        var result = SplineConverter.Convert(new System.Collections.Generic.List<Vector>(),
            isClosed: false, tolerance: 0.001);
        Assert.Empty(result);
    }

    [Fact]
    public void Convert_SinglePoint_ReturnsEmpty()
    {
        var points = new System.Collections.Generic.List<Vector> { new Vector(5, 5) };
        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);
        Assert.Empty(result);
    }

    private static Vector GetStartPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.StartPoint(),
            Line l => l.StartPoint,
            _ => throw new System.Exception("Unexpected entity type")
        };
    }

    private static Vector GetEndPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.EndPoint(),
            Line l => l.EndPoint,
            _ => throw new System.Exception("Unexpected entity type")
        };
    }
}

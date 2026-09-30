using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.Geometry;

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
            new Vector(10, 5),
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
            Assert.True(gap < 0.001, $"Gap of {gap:F6} between entity {i} and {i + 1}");
        }
    }

    [Fact]
    public void Convert_EmptyPoints_ReturnsEmpty()
    {
        var result = SplineConverter.Convert(
            new System.Collections.Generic.List<Vector>(),
            isClosed: false,
            tolerance: 0.001
        );
        Assert.Empty(result);
    }

    [Fact]
    public void Convert_SinglePoint_ReturnsEmpty()
    {
        var points = new System.Collections.Generic.List<Vector> { new Vector(5, 5) };
        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);
        Assert.Empty(result);
    }

    // Recorded on the unmodified 5bf3c5f converter. Preserve even the final CW
    // arc of the rotated ellipse: this extraction must not change fitting decisions.
    [Theory]
    [MemberData(nameof(SignedAngleCharacterizationCases))]
    public void Convert_SignedAngleCharacterization_PreservesOrderedArcs(
        string scenario, double[][] expected, bool[] reversed
    )
    {
        var points = SignedAngleCharacterizationPoints(scenario);
        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.05);

        Assert.Collection(result, expected.Select((parameters, i) =>
            (Action<Entity>)(entity => AssertCharacterizedArc(entity, parameters, reversed[i]))
        ).ToArray());
    }

    public static IEnumerable<object[]> SignedAngleCharacterizationCases()
    {
        yield return new object[]
        {
            "ccw",
            new double[][]
            {
                new[] { 3.0000000000000004, -1.9999999999999991, 4.999999999999999, 0.2999999999999998, 2.3000000000000003 },
            },
            new[] { false },
        };
        yield return new object[]
        {
            "cw",
            new double[][]
            {
                new[] { 3, -1.9999999999999991, 4.999999999999999, 2.3, 0.2999999999999999 },
            },
            new[] { true },
        };
        yield return new object[]
        {
            "reversed",
            new double[][]
            {
                new[] { 3, -1.9999999999999991, 4.999999999999999, 2.3, 0.2999999999999998 },
            },
            new[] { true },
        };
        yield return new object[]
        {
            "atan-seam",
            new double[][]
            {
                new[] { 2.9999999999999982, -2.0000000000000004, 4.999999999999998, 2.7999999999999994, 3.8 },
            },
            new[] { false },
        };
        yield return new object[]
        {
            "positive-x-seam",
            new double[][]
            {
                new[] { 2.999999999999999, -2, 5.000000000000002, 5.8, 0.5168146928204133 },
            },
            new[] { false },
        };
        yield return new object[]
        {
            "rotated-partial-ellipse",
            new double[][]
            {
                new[] { 4.7398647354210635, -2.2095236378718557, 4.9619610272064785, 1.0038820776565072, 2.0068607655184447 },
                new[] { 0.4338148276802185, 7.03129213222217, 5.232877956552578, 5.148453419108238, 5.046209652075572 },
            },
            new[] { false, true },
        };
    }

    internal static List<Vector> SignedAngleCharacterizationPoints(string scenario)
    {
        var (start, end, ellipse) = scenario switch
        {
            "cw" => (2.3, 0.3, false),
            "atan-seam" => (2.8, 3.8, false),
            "positive-x-seam" => (5.8, 6.8, false),
            "rotated-partial-ellipse" => (0.2, 1.3, true),
            _ => (0.3, 2.3, false),
        };
        var points = new List<Vector>();
        for (var i = 0; i <= 24; i++)
        {
            var angle = start + (end - start) * i / 24;
            var x = (ellipse ? 6 : 5) * System.Math.Cos(angle);
            var y = (ellipse ? 4 : 5) * System.Math.Sin(angle);
            var rotation = ellipse ? 0.6 : 0;
            points.Add(new Vector(
                3 + x * System.Math.Cos(rotation) - y * System.Math.Sin(rotation),
                -2 + x * System.Math.Sin(rotation) + y * System.Math.Cos(rotation)
            ));
        }
        if (scenario == "reversed")
            points.Reverse();
        return points;
    }

    private static void AssertCharacterizedArc(Entity entity, double[] expected, bool reversed)
    {
        var arc = Assert.IsType<Arc>(entity);
        var actual = new[]
        {
            arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle,
        };
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-10, expected[i] + 1e-10);
        Assert.Equal(reversed, arc.IsReversed);
    }

    private static Vector GetStartPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.StartPoint(),
            Line l => l.StartPoint,
            _ => throw new System.Exception("Unexpected entity type"),
        };
    }

    private static Vector GetEndPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.EndPoint(),
            Line l => l.EndPoint,
            _ => throw new System.Exception("Unexpected entity type"),
        };
    }
}

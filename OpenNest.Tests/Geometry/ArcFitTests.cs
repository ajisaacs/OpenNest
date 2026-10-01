using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class ArcFitTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("single")]
    [InlineData("positive-half-turn")]
    [InlineData("negative-half-turn")]
    [InlineData("repeated")]
    [InlineData("translated")]
    [InlineData("closed-ccw")]
    [InlineData("closed-cw")]
    [InlineData("multiple-turns-ccw")]
    [InlineData("multiple-turns-cw")]
    [InlineData("irregular-multiple-turns")]
    [InlineData("two-points")]
    [InlineData("atan-seam-ccw")]
    [InlineData("atan-seam-cw")]
    [InlineData("positive-x-seam")]
    [InlineData("collinear-through-center")]
    [InlineData("nan-point-x")]
    [InlineData("nan-point-y")]
    [InlineData("nan-center")]
    [InlineData("nan-empty-center")]
    [InlineData("nan-single-center")]
    [InlineData("nan-single-point")]
    public void SumSignedAngles_MatchesFrozenAccumulatorBitExactlyWithoutMutation(string scenario)
    {
        var (center, points) = Corpus(scenario);
        var centerBefore = Snapshot(center);
        var pointsBefore = points.Select(Snapshot).ToArray();
        var expected = FrozenSumSignedAngles(center, points);

        var actual = ArcFit.SumSignedAngles(center, points);

        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        Assert.Equal(centerBefore, Snapshot(center));
        Assert.Equal(pointsBefore, points.Select(Snapshot).ToArray());

        switch (scenario)
        {
            // Strict > / < comparisons must leave exact +PI and -PI unreduced.
            case "positive-half-turn":
                Assert.Equal(BitConverter.DoubleToInt64Bits(System.Math.PI), BitConverter.DoubleToInt64Bits(actual));
                break;
            case "negative-half-turn":
                Assert.Equal(BitConverter.DoubleToInt64Bits(-System.Math.PI), BitConverter.DoubleToInt64Bits(actual));
                break;
            case "empty":
            case "single":
            case "repeated":
            case "collinear-through-center":
            case "nan-empty-center":
            case "nan-single-center":
            case "nan-single-point":
                Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(actual));
                break;
            case "multiple-turns-ccw":
            case "irregular-multiple-turns":
                Assert.True(actual > 2 * System.Math.PI);
                break;
            case "multiple-turns-cw":
                Assert.True(actual < -2 * System.Math.PI);
                break;
            case "nan-point-x":
            case "nan-point-y":
            case "nan-center":
                // Characterized before extraction: a visited NaN propagates into the
                // total; neither normalization loop runs because comparisons are false.
                // Empty/single lists never evaluate Atan2, even with a NaN center/point.
                Assert.True(double.IsNaN(actual));
                break;
        }
    }

    private static (long x, long y) Snapshot(Vector point) =>
        (BitConverter.DoubleToInt64Bits(point.X), BitConverter.DoubleToInt64Bits(point.Y));

    private static (Vector center, List<Vector> points) Corpus(string scenario)
    {
        var center = new Vector(0, 0);
        var points = scenario switch
        {
            "empty" => new List<Vector>(),
            "single" => new List<Vector> { new Vector(2, -3) },
            "positive-half-turn" => new List<Vector> { new Vector(1, 0), new Vector(-1, 0) },
            "negative-half-turn" => new List<Vector> { new Vector(-1, 0), new Vector(1, 0) },
            "repeated" => new List<Vector> { new Vector(1, 2), new Vector(1, 2), new Vector(1, 2) },
            "translated" => new List<Vector>
            {
                new Vector(9, -11), new Vector(7, -9), new Vector(5, -11),
                new Vector(7, -13), new Vector(9, -11),
            },
            "closed-ccw" or "closed-cw" => CardinalLoop(1),
            "multiple-turns-ccw" or "multiple-turns-cw" => CardinalLoop(3),
            "irregular-multiple-turns" => IrregularTurns(),
            "two-points" => new List<Vector> { new Vector(1, 1), new Vector(-1, 1) },
            "atan-seam-ccw" => new List<Vector> { new Vector(-1, 0.001), new Vector(-1, -0.001) },
            "atan-seam-cw" => new List<Vector> { new Vector(-1, -0.001), new Vector(-1, 0.001) },
            "positive-x-seam" => new List<Vector> { new Vector(1, -0.001), new Vector(1, 0.001) },
            "collinear-through-center" => new List<Vector>
            {
                new Vector(1, 0), new Vector(0, 0), new Vector(-1, 0),
                new Vector(0, 0), new Vector(1, 0),
            },
            "nan-point-x" => new List<Vector>
            {
                new Vector(1, 0), new Vector(double.NaN, 1), new Vector(0, 1),
            },
            "nan-point-y" => new List<Vector>
            {
                new Vector(1, 0), new Vector(1, double.NaN), new Vector(0, 1),
            },
            "nan-center" => new List<Vector> { new Vector(1, 0), new Vector(0, 1) },
            "nan-empty-center" => new List<Vector>(),
            "nan-single-center" => new List<Vector> { new Vector(1, 0) },
            "nan-single-point" => new List<Vector> { new Vector(double.NaN, double.NaN) },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        if (scenario == "translated")
            center = new Vector(7, -11);
        else if (scenario is "nan-center" or "nan-empty-center" or "nan-single-center")
            center = new Vector(double.NaN, 0);
        if (scenario is "closed-cw" or "multiple-turns-cw")
            points.Reverse();
        return (center, points);
    }

    private static List<Vector> CardinalLoop(int turns)
    {
        var cycle = new[] { new Vector(1, 0), new Vector(0, 1), new Vector(-1, 0), new Vector(0, -1) };
        var points = new List<Vector>();
        for (var i = 0; i <= turns * 4; i++)
            points.Add(cycle[i % 4]);
        return points;
    }

    private static List<Vector> IrregularTurns()
    {
        var steps = new[] { 0.13, 0.81, -0.27, 1.01, 0.34, 0.0 };
        var points = new List<Vector>();
        var angle = 2.9;
        for (var i = 0; i < 80; i++)
        {
            points.Add(new Vector(System.Math.Cos(angle), System.Math.Sin(angle)));
            angle += steps[i % steps.Length];
        }
        return points;
    }

    // Independent frozen test oracle, written before the production extraction.
    // Do not route this through ArcFit or any other production angle helper.
    private static double FrozenSumSignedAngles(Vector center, List<Vector> points)
    {
        const double fullTurn = 2.0 * System.Math.PI;
        var sum = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var previousAngle = System.Math.Atan2(previous.Y - center.Y, previous.X - center.X);
            var currentAngle = System.Math.Atan2(current.Y - center.Y, current.X - center.X);
            var change = currentAngle - previousAngle;
            while (change > System.Math.PI)
                change -= fullTurn;
            while (change < -System.Math.PI)
                change += fullTurn;
            sum += change;
        }
        return sum;
    }
}

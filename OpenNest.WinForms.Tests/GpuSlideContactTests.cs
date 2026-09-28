using System.Reflection;
using ILGPU.Runtime;
using OpenNest.Geometry;
using OpenNest.Gpu;
using OpenNest.Math;
using Xunit;
using Xunit.Abstractions;

namespace OpenNest.WinForms.Tests;

// The real ILGPU kernels run on its deterministic CPU accelerator, not a mock or
// CpuDistanceComputer. This also runs without GPU hardware on Windows CI.
public sealed class GpuSlideContactFixture : IDisposable
{
    public GpuSlideComputer Computer { get; } = new GpuSlideComputer(preferCPU: true);

    public void Dispose() => Computer.Dispose();
}

public class GpuSlideContactTests : IClassFixture<GpuSlideContactFixture>
{
    private readonly GpuSlideComputer computer;
    private readonly ITestOutputHelper output;

    public GpuSlideContactTests(GpuSlideContactFixture fixture, ITestOutputHelper output)
    {
        computer = fixture.Computer;
        this.output = output;
    }

    public static IEnumerable<object[]> Paths()
    {
        foreach (var multiDir in new[] { false, true })
            foreach (var reverse in new[] { false, true })
                for (var turns = 0; turns < 4; turns++)
                    yield return new object[] { multiDir, reverse, turns };
    }

    [Fact]
    public void Kernels_ExecuteOnCpuAccelerator()
    {
        var accelerator = Assert.IsAssignableFrom<Accelerator>(typeof(GpuSlideComputer)
            .GetField("_accelerator", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(computer));
        output.WriteLine($"ILGPU backend: {accelerator.AcceleratorType}; device: {accelerator.Name}");
        Assert.Equal(AcceleratorType.CPU, accelerator.AcceleratorType);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void TouchingRectangles_EnterBlocksButDepartureAndTangentsDoNot(
        bool multiDir, bool reverse, int turns)
    {
        var stationary = Rect(0, 0, 2, 2);
        var moving = Rect(2, 0, 2, 2);
        AssertSlide(multiDir, reverse, turns, stationary, moving, PushDirection.Left, 0);
        AssertSlide(multiDir, reverse, turns, stationary, moving, PushDirection.Right, double.MaxValue);
        AssertSlide(multiDir, reverse, turns, stationary, moving, PushDirection.Up, double.MaxValue);
        AssertSlide(multiDir, reverse, turns, stationary, moving, PushDirection.Down, double.MaxValue);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void SlidingContact_StopsAtLaterHookOnSameObstacle(
        bool multiDir, bool reverse, int turns)
    {
        var stationary = Loop((0, 0), (2, 0), (2, 4), (5, 4), (5, 6), (0, 6));
        AssertSlide(multiDir, reverse, turns, stationary, Rect(2, 0, 1, 1), PushDirection.Up, 3);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void PositiveGrazingContact_StopsAtLaterFeatureOnSameObstacle(
        bool multiDir, bool reverse, int turns)
    {
        var stationary = Loop((4, 1), (10, 1), (10, -2), (12, -2), (12, 3), (4, 3));
        AssertSlide(multiDir, reverse, turns, stationary, Rect(0, 0, 1, 1), PushDirection.Right, 9);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void PositiveGrazingContact_WithoutLaterBlockerIsUnbounded(
        bool multiDir, bool reverse, int turns)
    {
        AssertSlide(multiDir, reverse, turns, Rect(4, 1, 2, 2), Rect(0, 0, 1, 1),
            PushDirection.Right, double.MaxValue);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void HoleContact_LeavingWallStillStopsAtOppositeWall(
        bool multiDir, bool reverse, int turns)
    {
        var stationary = Rect(0, 0, 10, 10);
        stationary.AddRange(Rect(2, 2, 6, 6)); // Hole depth must not depend on winding.
        AssertSlide(multiDir, reverse, turns, stationary, Rect(2, 3, 1, 1), PushDirection.Right, 5);
        AssertSlide(multiDir, reverse, turns, stationary, Rect(2, 3, 1, 1), PushDirection.Up, 4);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void ReverseRayWitness_StationaryVertexHitsMiddleOfMovingEdge(
        bool multiDir, bool reverse, int turns)
    {
        // No moving vertex can hit the shorter stationary rectangle.
        AssertSlide(multiDir, reverse, turns, Rect(5, 2, 1, 1), Rect(0, 0, 1, 6),
            PushDirection.Right, 4);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void SnappedZeroDistance_KeepsUnsnappedWitnessOnBothBoundaries(
        bool multiDir, bool reverse, int turns)
    {
        var gap = Tolerance.Epsilon / 2;
        AssertSlide(multiDir, reverse, turns, Rect(1 + gap, 0, 1, 1), Rect(0, 0, 1, 1),
            PushDirection.Right, 0);
        AssertSlide(multiDir, reverse, turns, Rect(1 + gap, 2, 1, 1), Rect(0, 0, 1, 6),
            PushDirection.Right, 0);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void ToleranceNearMiss_DoesNotBecomeABlockingWitness(
        bool multiDir, bool reverse, int turns)
    {
        AssertSlide(multiDir, reverse, turns, Rect(4, 0, 1, 1),
            Rect(0, 1 + Tolerance.Epsilon / 2, 1, 1), PushDirection.Right, double.MaxValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearParallelEdges_UseTheSharedAxisRayTolerance(bool multiDir)
    {
        var dy = Tolerance.Epsilon / 2;
        AssertSlide(multiDir, false, 0,
            new List<Line> { new Line(2, 0, 3, dy) },
            new List<Line> { new Line(0, 0, 1, dy) }, PushDirection.Right, double.MaxValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TiedGrazingAndBlockingContacts_DoNotDropBlockingContact(bool multiDir)
    {
        var stationary = Rect(4, 1, 1, 1);
        stationary.AddRange(Rect(4, -2, 1, 2.5));
        var moving = Loop((1, 1), (0, 1), (0, 0), (1, 0));
        AssertSlide(multiDir, false, 0, stationary, moving, PushDirection.Right, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenBoundaries_RemainConservative(bool multiDir)
    {
        AssertSlide(multiDir, false, 0,
            new List<Line> { new Line(2, 0, 2, 2) },
            new List<Line> { new Line(2, 0, 2, 2) }, PushDirection.Right, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyGeometryAndOffsets_ReturnNoHitWithoutStaleResults(bool multiDir)
    {
        var rectangle = SpatialQuery.FlattenLines(Rect(0, 0, 1, 1));
        var offsets = new[] { 0.0, 0.0, 2.0, 0.0 };
        var directions = new[] { (int)PushDirection.Left, (int)PushDirection.Left };
        Assert.Equal(new[] { 0.0, 1.0 },
            Compute(multiDir, rectangle, 4, rectangle, 4, offsets, 2, directions, PushDirection.Left));
        Assert.Empty(Compute(multiDir, rectangle, 4, rectangle, 4, offsets, 0, directions));
        Assert.All(Compute(multiDir, Array.Empty<double>(), 0, rectangle, 4, offsets, 2, directions),
            distance => Assert.Equal(double.MaxValue, distance));
        Assert.All(Compute(multiDir, rectangle, 4, Array.Empty<double>(), 0, offsets, 2, directions),
            distance => Assert.Equal(double.MaxValue, distance));
        Assert.Equal(new[] { 0.0, 1.0 },
            Compute(multiDir, rectangle, 4, rectangle, 4, offsets, 2, directions, PushDirection.Left));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedOffsetBuffers_GrowShrinkAndRespectActivePrefixes(bool multiDir)
    {
        var stationary = Rect(0, 0, 2, 2);
        var moving = Rect(0, 0, 1, 1);
        var stationaryData = SpatialQuery.FlattenLines(stationary);
        var movingData = SpatialQuery.FlattenLines(moving);
        // Odd lengths exercise rounded-up thread groups and retained excess capacity.
        foreach (var count in new[] { 1, 37, 3, 65, 2, 97, 0, 5, 129, 1 })
        {
            var offsets = new double[(count + 7) * 2];
            var directions = new int[count + 7];
            var expected = new double[count];
            for (var i = 0; i < count; i++)
            {
                var dx = 2 + i % 4;
                var dy = i % 3;
                var direction = multiDir ? (PushDirection)(i % 4) : PushDirection.Left;
                offsets[i * 2] = dx;
                offsets[i * 2 + 1] = dy;
                directions[i] = (int)direction;
                expected[i] = SpatialQuery.DirectionalDistance(moving, dx, dy, stationary, direction);
            }
            var actual = Compute(multiDir, stationaryData, 4, movingData, 4, offsets, count,
                directions, PushDirection.Left);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedSegmentArrays_ChangingCountsAndCoordinatesRefreshesBothCaches(bool multiDir)
    {
        var stationary = SpatialQuery.FlattenLines(Rect(4, 0, 1, 1).Concat(Rect(2, 0, 1, 1)).ToList());
        var moving = SpatialQuery.FlattenLines(Rect(0, 0, 1, 1).Concat(Rect(2, 0, 1, 1)).ToList());
        var offsets = new[] { 0.0, 0.0 };
        var directions = new[] { (int)PushDirection.Right };
        foreach (var counts in new[] { (4, 4, 3.0), (8, 4, 1.0), (4, 8, 1.0), (4, 4, 3.0) })
            Assert.Equal(counts.Item3,
                Compute(multiDir, stationary, counts.Item1, moving, counts.Item2, offsets, 1, directions)[0]);

        for (var i = 0; i < stationary.Length; i += 2)
            stationary[i] += 1;
        Assert.Equal(4, Compute(multiDir, stationary, 4, moving, 4, offsets, 1, directions)[0]);
        for (var i = 0; i < moving.Length; i += 2)
            moving[i] -= 1;
        Assert.Equal(5, Compute(multiDir, stationary, 4, moving, 4, offsets, 1, directions)[0]);
        computer.InvalidateStationary();
        computer.InvalidateMoving();
        Assert.Equal(5, Compute(multiDir, stationary, 4, moving, 4, offsets, 1, directions)[0]);
    }

    [Fact]
    public void MultiDir_UsesEachOffsetAndDirectionIndependently()
    {
        var stationary = SpatialQuery.FlattenLines(Rect(0, 0, 2, 2));
        var moving = SpatialQuery.FlattenLines(Rect(0, 0, 1, 1));
        var offsets = new[] { 2.0, 0.0, 2.0, 0.0, 2.0, 0.0, 2.0, 0.0, -3.0, 0.0, 0.0, -4.0 };
        var directions = new[] { PushDirection.Left, PushDirection.Right, PushDirection.Up,
            PushDirection.Down, PushDirection.Right, PushDirection.Up }.Select(d => (int)d).ToArray();
        Assert.Equal(new[] { 0.0, double.MaxValue, double.MaxValue, double.MaxValue, 2.0, 3.0 },
            computer.ComputeBatchMultiDir(stationary, 4, moving, 4, offsets, 6, directions));
    }

    private void AssertSlide(bool multiDir, bool reverse, int turns, List<Line> stationary,
        List<Line> moving, PushDirection direction, double expected)
    {
        // Exact quarter turns cover all axis signs without trigonometric rounding.
        // Offset the world and template independently to expose mixed-frame witnesses.
        var origin = new Vector(13, -7);
        stationary = Transform(stationary, turns, new Vector(17, -23), reverse);
        moving = Transform(moving, turns, new Vector(17, -23) - origin, reverse);
        for (var turn = 0; turn < turns; turn++)
            direction = direction switch
            {
                PushDirection.Right => PushDirection.Up,
                PushDirection.Up => PushDirection.Left,
                PushDirection.Left => PushDirection.Down,
                _ => PushDirection.Right,
            };

        Assert.Equal(expected,
            SpatialQuery.DirectionalDistance(moving, origin.X, origin.Y, stationary, direction), 9);
        var actual = Compute(multiDir, SpatialQuery.FlattenLines(stationary), stationary.Count,
            SpatialQuery.FlattenLines(moving), moving.Count, new[] { origin.X, origin.Y }, 1,
            new[] { (int)direction }, direction);
        Assert.Single(actual);
        Assert.Equal(expected, actual[0], 9);
    }

    private double[] Compute(bool multiDir, double[] stationary, int stationaryCount,
        double[] moving, int movingCount, double[] offsets, int count, int[] directions,
        PushDirection direction = PushDirection.Right) =>
        multiDir
            ? computer.ComputeBatchMultiDir(stationary, stationaryCount, moving, movingCount,
                offsets, count, directions)
            : computer.ComputeBatch(stationary, stationaryCount, moving, movingCount,
                offsets, count, direction);

    private static List<Line> Transform(List<Line> lines, int turns, Vector origin, bool reverse)
    {
        Vector Map(Vector point)
        {
            for (var i = 0; i < turns; i++)
                point = new Vector(-point.Y, point.X);
            return point + origin;
        }
        var result = lines.Select(line => new Line(Map(line.StartPoint), Map(line.EndPoint))).ToList();
        if (reverse)
        {
            result.Reverse();
            foreach (var line in result)
                line.Reverse();
        }
        return result;
    }

    private static List<Line> Rect(double x, double y, double width, double height) =>
        Loop((x, y), (x + width, y), (x + width, y + height), (x, y + height));

    private static List<Line> Loop(params (double X, double Y)[] points) =>
        points.Select((point, i) => new Line(point.X, point.Y,
            points[(i + 1) % points.Length].X, points[(i + 1) % points.Length].Y)).ToList();
}

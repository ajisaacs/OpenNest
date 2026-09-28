using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class SlideContactTests
{
    public static IEnumerable<object[]> LinePaths()
    {
        foreach (var path in new[] { "axis", "offset", "edges", "vector", "entities", "cpu-lines", "cpu-entities" })
            foreach (var reverse in new[] { false, true })
                yield return new object[] { path, reverse };
    }

    [Theory]
    [MemberData(nameof(LinePaths))]
    public void TouchingRectangles_LeaveOrSlideButCannotEnter(string path, bool reverse)
    {
        var stationary = Rect(0, 0, 2, 2);
        var moving = Rect(2, 0, 2, 2);
        if (reverse)
        {
            Reverse(stationary);
            Reverse(moving);
        }
        Assert.Equal(0, Distance(path, moving, stationary, PushDirection.Left));
        Assert.Equal(double.MaxValue, Distance(path, moving, stationary, PushDirection.Right));
        Assert.Equal(double.MaxValue, Distance(path, moving, stationary, PushDirection.Up));
        Assert.Equal(double.MaxValue, Distance(path, moving, stationary, PushDirection.Down));
    }

    [Theory]
    [MemberData(nameof(LinePaths))]
    public void SlidingContact_StillStopsAtLaterHookOnSameObstacle(string path, bool reverse)
    {
        var stationary = Loop((0, 0), (2, 0), (2, 4), (5, 4), (5, 6), (0, 6));
        var moving = Rect(2, 0, 1, 1);
        if (reverse)
        {
            Reverse(stationary);
            Reverse(moving);
        }
        Assert.Equal(3, Distance(path, moving, stationary, PushDirection.Up), 9);
    }

    [Theory]
    [MemberData(nameof(LinePaths))]
    public void HoleContact_LeavingWallStillStopsAtOppositeWall(string path, bool reverse)
    {
        var stationary = Rect(0, 0, 10, 10);
        stationary.AddRange(Rect(2, 2, 6, 6)); // depth, not winding, defines the hole
        var moving = Rect(2, 3, 1, 1);
        if (reverse)
        {
            Reverse(stationary);
            Reverse(moving);
        }
        Assert.Equal(0, Distance(path, moving, stationary, PushDirection.Left));
        Assert.Equal(5, Distance(path, moving, stationary, PushDirection.Right), 9);
        Assert.Equal(4, Distance(path, moving, stationary, PushDirection.Up), 9);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 0.37)]
    [InlineData(true, 0.37)]
    public void RotatedHook_StopsAtFirstBlockingContact(bool cpu, double angle)
    {
        var stationary = Loop((0, 0), (2, 0), (2, 4), (5, 4), (5, 6), (0, 6)).Cast<Entity>().ToList();
        var moving = Rect(2, 0, 1, 1).Cast<Entity>().ToList();
        foreach (var entity in stationary.Concat(moving))
        {
            entity.Rotate(angle);
            entity.Offset(17, -23);
        }
        Assert.Equal(3, EntityDistance(cpu, moving, stationary, new Vector(0, 1).Rotate(angle)), 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Circles_TangentEscapeAndEnteringContact(bool cpu)
    {
        var moving = new List<Entity> { new Circle(2, 0, 1) };
        var stationary = new List<Entity> { new Circle(0, 0, 1) };
        Assert.Equal(0, EntityDistance(cpu, moving, stationary, new Vector(-1, 0)));
        Assert.Equal(double.MaxValue, EntityDistance(cpu, moving, stationary, new Vector(1, 0)));
        Assert.Equal(double.MaxValue, EntityDistance(cpu, moving, stationary, new Vector(0, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CircleInsideHole_TangentBlocksButDepartureFindsFarSide(bool cpu)
    {
        var moving = new List<Entity> { new Circle(3, 0, 1) };
        var stationary = new List<Entity> { new Circle(0, 0, 6), new Circle(0, 0, 4) };
        Assert.Equal(0, EntityDistance(cpu, moving, stationary, new Vector(1, 0)));
        Assert.Equal(0, EntityDistance(cpu, moving, stationary, new Vector(0, 1)));
        Assert.Equal(6, EntityDistance(cpu, moving, stationary, new Vector(-1, 0)), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositiveGrazingContact_DoesNotHideLaterCircle(bool cpu)
    {
        var moving = new List<Entity> { new Circle(0, 0, 1) };
        var stationary = new List<Entity> { new Circle(4, 2, 1), new Circle(10, 0, 1) };
        Assert.Equal(8, EntityDistance(cpu, moving, stationary, new Vector(1, 0)), 9);
    }

    [Fact]
    public void ReusedEdgeArrays_KeepTopologyAfterPreviousQuerySortedThem()
    {
        var moving = Rect(2, 0, 2, 2).Select(l => (l.StartPoint, l.EndPoint)).ToArray();
        var stationary = Rect(0, 0, 2, 2).Select(l => (l.StartPoint, l.EndPoint)).ToArray();
        Assert.Equal(0, SpatialQuery.DirectionalDistance(moving, Vector.Zero, stationary, Vector.Zero, PushDirection.Left));
        Assert.Equal(double.MaxValue, SpatialQuery.DirectionalDistance(moving, Vector.Zero, stationary, Vector.Zero, PushDirection.Right));
        Assert.Equal(double.MaxValue, SpatialQuery.DirectionalDistance(moving, Vector.Zero, stationary, Vector.Zero, PushDirection.Up));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullCircleArcSeam_AllowsSeparation(bool cpu)
    {
        var moving = new List<Entity> { new Arc(0, 0, 1, 0, 2 * System.Math.PI) };
        var stationary = new List<Entity> { new Circle(2, 0, 1) };
        Assert.Equal(double.MaxValue, EntityDistance(cpu, moving, stationary, new Vector(-1, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcaveCorner_DoesNotBlockSlidingAlongItsStraightSide(bool cpu)
    {
        var moving = Rect(3, -1, 2, 1).Cast<Entity>().ToList();
        var stationary = new List<Entity>
        {
            new Arc(0, 0, 5, System.Math.PI / 2, 0, true),
            new Line(5, 0, 10, 0),
            new Arc(0, 0, 10, 0, System.Math.PI / 2),
            new Line(0, 10, 0, 5),
        };
        Assert.Equal(double.MaxValue, EntityDistance(cpu, moving, stationary, new Vector(1, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThinRing_ArcSeamDoesNotChangeHoleClassification(bool cpu)
    {
        var a = System.Math.PI / 72;
        var moving = new List<Entity> { new Circle(8.995, 0, 1) };
        var stationary = new List<Entity>
        {
            new Circle(0, 0, 10),
            new Arc(0, 0, 9.995, a, a + System.Math.PI),
            new Arc(0, 0, 9.995, a + System.Math.PI, a + 2 * System.Math.PI),
        };
        Assert.Equal(17.99, EntityDistance(cpu, moving, stationary, new Vector(-1, 0)), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CircleAgainstInclinedWall_UsesInteriorCurveContact(bool cpu)
    {
        var moving = new List<Entity> { new Circle(0, 0, 1) };
        var stationary = Loop((-10, 0), (10, 10), (10, 12), (-10, 2)).Cast<Entity>().ToList();
        Assert.Equal(5 - System.Math.Sqrt(1.25), EntityDistance(cpu, moving, stationary, new Vector(0, 1)), 9);
    }

    [Fact]
    public void GpuAdapter_ArbitraryDirectionUsesExactCpuFallback()
    {
        using var axesOnly = new RejectSlideComputer();
        var distance = new GpuDistanceComputer(axesOnly).ComputeDistances(Rect(10, 10, 2, 2), Rect(0, 0, 2, 2),
            new[] { new SlideOffset(0, 0, 0.6, 0.8) })[0];
        Assert.Equal(8 / 0.6, distance, 9);
    }

    private sealed class RejectSlideComputer : ISlideComputer
    {
        public double[] ComputeBatch(double[] s, int sc, double[] m, int mc, double[] o, int oc, PushDirection d) =>
            throw new InvalidOperationException("Non-cardinal direction reached axis-only GPU");
        public double[] ComputeBatchMultiDir(double[] s, int sc, double[] m, int mc, double[] o, int oc, int[] d) =>
            throw new InvalidOperationException("Non-cardinal direction reached axis-only GPU");
        public void Dispose() { }
    }

    [Fact]
    public void OpenBoundaries_RemainConservative()
    {
        var moving = new List<Line> { new Line(2, 0, 2, 2) };
        var stationary = new List<Line> { new Line(2, 0, 2, 2) };
        Assert.Equal(0, SpatialQuery.DirectionalDistance(moving, stationary, PushDirection.Right));
    }

    private static double EntityDistance(bool cpu, List<Entity> moving, List<Entity> stationary, Vector direction) =>
        cpu ? new CpuDistanceComputer().ComputeDistances(stationary, moving,
            new[] { new SlideOffset(0, 0, direction.X, direction.Y) })[0]
            : SpatialQuery.DirectionalDistance(moving, stationary, direction);

    private static double Distance(string path, List<Line> moving, List<Line> stationary, PushDirection direction)
    {
        var unit = SpatialQuery.DirectionToOffset(direction, 1);
        // A nonzero template origin catches mixed local/world contact coordinates.
        var origin = new Vector(13, -7);
        var local = moving.Select(l => new Line(l.StartPoint - origin, l.EndPoint - origin)).ToList();
        return path switch
        {
            "axis" => SpatialQuery.DirectionalDistance(moving, stationary, direction),
            "offset" => SpatialQuery.DirectionalDistance(local, origin.X, origin.Y, stationary, direction),
            "edges" => SpatialQuery.DirectionalDistance(local.Select(l => (l.StartPoint, l.EndPoint)).ToArray(), origin,
                stationary.Select(l => (l.StartPoint, l.EndPoint)).ToArray(), Vector.Zero, direction),
            "vector" => SpatialQuery.DirectionalDistance(moving, stationary, unit),
            "entities" => SpatialQuery.DirectionalDistance(moving.Cast<Entity>().ToList(), stationary.Cast<Entity>().ToList(), unit),
            "cpu-lines" => new CpuDistanceComputer().ComputeDistances(stationary, local,
                new[] { new SlideOffset(origin.X, origin.Y, unit.X, unit.Y) })[0],
            "cpu-entities" => new CpuDistanceComputer().ComputeDistances(stationary.Cast<Entity>().ToList(), local.Cast<Entity>().ToList(),
                new[] { new SlideOffset(origin.X, origin.Y, unit.X, unit.Y) })[0],
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }

    private static void Reverse(List<Line> lines)
    {
        lines.Reverse();
        foreach (var line in lines)
            line.Reverse();
    }

    private static List<Line> Rect(double x, double y, double w, double h) =>
        Loop((x, y), (x + w, y), (x + w, y + h), (x, y + h));

    private static List<Line> Loop(params (double X, double Y)[] points) =>
        points.Select((p, i) => new Line(p.X, p.Y, points[(i + 1) % points.Length].X, points[(i + 1) % points.Length].Y)).ToList();
}

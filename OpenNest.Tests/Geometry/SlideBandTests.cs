using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// The axis band filter in <see cref="EntitySlideEvents"/> must skip only pairs whose kernels
/// cannot emit: the emitted event sequence (order, distances and contact points, bit for bit)
/// stays identical to the frozen pre-filter enumeration in <see cref="LegacyEntitySlideEvents"/>.
/// </summary>
public class SlideBandTests
{
    private static readonly (double X, double Y)[] AxisDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    [Theory]
    [InlineData(1.0, 3, 12)]
    [InlineData(250.0, 3, 12)]
    [InlineData(100000.0, 3, 12)]
    [InlineData(30000000.0, 3, 12)]
    [InlineData(1.0, 24, 160)]
    [InlineData(250.0, 24, 160)]
    [InlineData(100000.0, 24, 160)]
    [InlineData(30000000.0, 24, 160)]
    public void RandomShapes_EmitTheSameEventsAsTheUnfilteredEnumeration(double scale, int minSides, int maxSides)
    {
        // Boundaries of 24 or more entities go through the bucket index; smaller ones scan.
        var random = new Random(20261031 + (int)scale + maxSides);
        var compared = 0;
        var nonEmpty = 0;
        var trials = maxSides > SlideBandIndex.MinimumEntities ? 25 : 150;
        for (var trial = 0; trial < trials; trial++)
        {
            var stationary = RandomBoundary(random, scale, minSides, maxSides);
            var moving = RandomBoundary(random, scale, minSides, maxSides);
            foreach (var (dirX, dirY) in AxisDirections)
            {
                foreach (var (dx, dy) in Offsets(random, moving, stationary, dirX, dirY, scale))
                {
                    var events = CompareEvents(moving, stationary, dx, dy, dirX, dirY);
                    compared++;
                    if (events > 0)
                        nonEmpty++;
                }
            }
        }
        Assert.True(nonEmpty > compared / 4, $"only {nonEmpty} of {compared} slides emitted events");
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(-0.5, false)]
    [InlineData(0.99, false)]
    [InlineData(0.5, true)]
    [InlineData(-0.99, true)]
    public void VertexJustPastASegmentEnd_WithinTheRayTolerance_StillEmits(
        double fractionOfTolerance,
        bool indexed
    )
    {
        // RayLine accepts a segment parameter up to Eps past either end, so a ray that misses
        // the segment's extent by less than Eps of its span still reports a hit.
        var span = 1000.0;
        var stationary = new List<Entity> { new Line(new Vector(10, 0), new Vector(10, span)) };
        var y = fractionOfTolerance > 0
            ? span + fractionOfTolerance * Tolerance.Epsilon * span
            : fractionOfTolerance * Tolerance.Epsilon * span;
        var moving = new List<Entity> { new Line(new Vector(0, y), new Vector(-5, y)) };

        var events = CompareEvents(Pad(moving, indexed), Pad(stationary, indexed), 0, 0, 1, 0);

        Assert.True(events > 0, "the legacy kernel must report this tolerance hit");
    }

    [Theory]
    [InlineData(1e-9, false, false)]
    [InlineData(1e-3, false, false)]
    [InlineData(5.0, false, false)]
    [InlineData(1e-9, true, false)]
    [InlineData(5.0, true, false)]
    [InlineData(5.0, true, true)]
    [InlineData(1e-9, true, true)]
    public void RayGrazingAFarCircle_KeepsEveryRoundingHit(double radius, bool indexed, bool offset)
    {
        // Far along the ray the discriminant cancels catastrophically; sweep the band around
        // the circle's edge and require identical events, including any rounding hits. With
        // `offset` the distance comes from the moving translation rather than the geometry.
        var center = offset ? 0.0 : 90000.0;
        var dx = offset ? -90000.0 : 0.0;
        var stationary = Pad(new List<Entity> { new Circle(new Vector(center, 37), radius) }, indexed);
        var hits = 0;
        for (var k = -400; k <= 400; k++)
        {
            // Tiny radii widen the rounding band to about √(ulp(ox²)); sweep it coarsely.
            var step = radius < 1e-6 && offset ? 1e-6 : 1e-9 * System.Math.Max(1, radius);
            var y = 37 + radius + k * step;
            var moving = Pad(new List<Entity> { new Line(new Vector(0, y), new Vector(-1, y)) }, indexed);
            hits += CompareEvents(moving, stationary, dx, 0, 1, 0) > 0 ? 1 : 0;
        }
        Assert.True(hits > 0, "the sweep must include hits");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TangentCurvesAcrossTheBandEdge_EmitTheSameEvents(bool indexed)
    {
        var arc = new Arc(new Vector(0, 0), 3, 0, Angle.TwoPI * 0.75, false);
        var stationary = Pad(new List<Entity> { arc }, indexed);
        var emitted = 0;
        for (var k = -200; k <= 200; k++)
        {
            var dy = 3 + 2 + k * 1e-10;
            var moving = Pad(new List<Entity> { new Circle(new Vector(-50, 0), 2) }, indexed);
            emitted += CompareEvents(moving, stationary, 0, dy, 1, 0) > 0 ? 1 : 0;
        }
        Assert.True(emitted > 0, "the sweep must include tangent contacts");
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(2e-5, false)]
    [InlineData(0.0, true)]
    [InlineData(2e-5, true)]
    public void ArcAgainstANearlyParallelLine_AtTheBandEdge_EmitsTheSameEvents(double end, bool indexed)
    {
        // ArcToLine samples the circle where its tangent is parallel to the line. For a line
        // nearly parallel to the push that point is the circle's top or bottom, so sweeping
        // the circle across either end of the line's tiny perpendicular span walks the
        // arc-to-line band edge, including RayLine's Eps·span tolerance past the end.
        var stationary = Pad(new List<Entity> { new Line(new Vector(0, 0), new Vector(1000, 2e-5)) }, indexed);
        var radius = 5.0;
        var hits = 0;
        for (var k = -60; k <= 60; k++)
        {
            foreach (var side in new[] { -1.0, 1.0 })
            {
                // side -1 puts the circle above the line (bottom point), +1 below (top point).
                var cy = end - side * radius + k * 1e-11;
                var moving = Pad(new List<Entity> { new Circle(new Vector(-50, cy), radius) }, indexed);
                hits += CompareEvents(moving, stationary, 0, 0, 1, 0, vertices: false) > 0 ? 1 : 0;
            }
        }
        Assert.True(hits > 0, "the sweep must include arc-to-line hits");
    }

    public static IEnumerable<object[]> NonfiniteAndOverflowingSlides()
    {
        var unit = new Line(new Vector(0, 0), new Vector(0, 1));
        // vertex, stationary line, moving offset X; each emits in the unfiltered kernel.
        yield return new object[] { "infinite moving vertex", new Vector(double.NegativeInfinity, 5), unit, 0.0 };
        yield return new object[] {
            "infinite line end", new Vector(-10, 5),
            new Line(new Vector(double.PositiveInfinity, 0), new Vector(0, 1)), 0.0,
        };
        yield return new object[] {
            "overflowing separation", new Vector(-1.7e308, 5),
            new Line(new Vector(1.7e308, 0), new Vector(1.7e308, 1)), 0.0,
        };
        yield return new object[] { "infinite moving offset", new Vector(0, 5), unit, double.NegativeInfinity };
    }

    [Theory]
    [MemberData(nameof(NonfiniteAndOverflowingSlides))]
    public void NonfiniteOrOverflowingAlongThePush_KeepsTheKernelsEvents(
        string name,
        Vector vertex,
        Line line,
        double movingDx
    )
    {
        var events = CompareVertexEvents(new[] { vertex }, new List<Entity> { line }, movingDx, 0);
        Assert.True(events > 0, name);
    }

    [Fact]
    public void OverflowingDiscriminant_KeepsTheKernelsEvents()
    {
        var circle = new List<Entity> { new Circle(new Vector(0, 0), 1) };
        Assert.True(CompareVertexEvents(new[] { new Vector(-7e153, 1e150) }, circle, 0, 0) > 0);
    }

    [Theory]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-3e150)]
    public void LargeBoundary_WithAVertexBeyondTheLimitAlongThePush_KeepsTheKernelsEvents(double x)
    {
        // Enough entities to build an index; the far vertex must switch it off, because the
        // kernels can emit against lines whose band the vertex misses.
        var stationary = Enumerable.Range(0, 40)
            .Select(i => (Entity)new Line(new Vector(i, 0), new Vector(i + 0.5, 1)))
            .ToList();
        var events = CompareVertexEvents(new[] { new Vector(x, 0.5) }, stationary, 0, 0);
        Assert.True(events > 0);
    }

    [Fact]
    public void InfiniteLineEnd_KeepsArcToLineEvents()
    {
        var stationary = new List<Entity>
        {
            new Line(new Vector(double.PositiveInfinity, 0), new Vector(0, 1)),
        };
        var moving = new List<Entity> { new Circle(new Vector(0, 10), 1) };
        Assert.True(CompareEvents(moving, stationary, 0, 0, 1, 0, vertices: false, resolve: false) > 0);
    }

    [Fact]
    public void ObliqueDirection_IsNotFiltered()
    {
        Assert.Equal(SlideBand.Axis.None, SlideBand.For(System.Math.Sqrt(0.5), System.Math.Sqrt(0.5)));
        Assert.Equal(SlideBand.Axis.None, SlideBand.For(2, 0));
        Assert.Equal(SlideBand.Axis.PerpendicularY, SlideBand.For(-1, 0));
        Assert.Equal(SlideBand.Axis.PerpendicularX, SlideBand.For(0, 1));
    }

    [Fact]
    public void NonfiniteInputs_AreKept()
    {
        var line = new Line(new Vector(0, 0), new Vector(0, 1));
        Assert.True(SlideBand.RayMayHit(SlideBand.Axis.PerpendicularY, 5, double.NaN, line, 0, 0));
        Assert.True(SlideBand.RayMayHit(SlideBand.Axis.PerpendicularY, 5, 0.5, line, 0, double.PositiveInfinity));
        Assert.True(SlideBand.CirclesMayTouch(SlideBand.Axis.PerpendicularY, 0, double.NaN, 1, 0, 0, 1));
        Assert.True(SlideBand.CircleMayReachLine(SlideBand.Axis.PerpendicularX, double.NaN, 0, 1, 5, 5, 6, 6));
    }

    [Fact]
    public void SeparatedEntities_AreSkipped()
    {
        var line = new Line(new Vector(0, 0), new Vector(0, 1));
        Assert.False(SlideBand.RayMayHit(SlideBand.Axis.PerpendicularY, 5, 3, line, 0, 0));
        Assert.False(SlideBand.RayMayHit(SlideBand.Axis.PerpendicularY, 5, 3, new Circle(new Vector(0, 0), 1), 0, 0));
        Assert.False(SlideBand.CirclesMayTouch(SlideBand.Axis.PerpendicularX, 10, 0, 1, 0, 0, 1));
        Assert.False(SlideBand.CircleMayReachLine(SlideBand.Axis.PerpendicularY, 0, 10, 1, 5, 0, 6, 2));
    }

    [Fact]
    public void Index_ReturnsEveryAcceptedEntity_InAscendingOrder()
    {
        var random = new Random(7);
        foreach (var axis in new[] { SlideBand.Axis.PerpendicularY, SlideBand.Axis.PerpendicularX })
        {
            for (var trial = 0; trial < 40; trial++)
            {
                var scale = new[] { 1.0, 300.0, 1e6 }[trial % 3];
                var entities = RandomBoundary(random, scale, 24, 200);
                var vertices = SpatialQuery.ExtractEntityVertices(entities);
                double along = 0, perpendicular = 0;
                SlideBandIndex.Measure(axis, entities, vertices, 0, 0, ref along, ref perpendicular);
                var reach = SlideBandIndex.Reach(along, perpendicular);
                var index = SlideBandIndex.Build(axis, entities, 0, 0, reach, SlideBandIndex.Kind.All);
                Assert.NotNull(index);
                var range = new List<int>();
                for (var q = 0; q < 300; q++)
                {
                    var v = vertices[random.Next(vertices.Length)];
                    var nudge = (random.NextDouble() - 0.5) * 1e-4 * scale * (q % 2);
                    var vx = v.X + (axis == SlideBand.Axis.PerpendicularX ? nudge : 0);
                    var vy = v.Y + (axis == SlideBand.Axis.PerpendicularY ? nudge : 0);
                    var expected = Enumerable.Range(0, entities.Count)
                        .Where(j => entities[j] is Line or Arc or Circle && SlideBand.RayMayHit(axis, vx, vy, entities[j], 0, 0))
                        .ToList();
                    Assert.True(index!.TryPoint(SlideBand.Perp(axis, vx, vy), out var first, out var last));
                    var found = Enumerable.Range(first, last - first).Select(index.Item).ToList();
                    Assert.Equal(found.OrderBy(j => j), found);
                    Assert.Empty(expected.Except(found));

                    var p = SlideBand.Perp(axis, vx, vy);
                    Assert.True(index.TryRange(p - scale * 0.05, p + scale * 0.05, range));
                    Assert.Equal(range.Distinct().OrderBy(j => j), range);
                }
            }
        }
    }

    [Fact]
    public void Index_RefusesNonfiniteGeometry()
    {
        var entities = Enumerable.Range(0, 30)
            .Select(i => (Entity)new Line(new Vector(i, 0), new Vector(i, 1)))
            .Append(new Line(new Vector(0, double.NaN), new Vector(1, 1)))
            .ToList();
        Assert.Null(SlideBandIndex.Build(SlideBand.Axis.PerpendicularY, entities, 0, 0, 0, SlideBandIndex.Kind.All));
        Assert.Null(SlideBandIndex.Build(SlideBand.Axis.PerpendicularY, entities.Take(30).ToList(), 0, 0, double.NaN, SlideBandIndex.Kind.All));
        Assert.True(double.IsNaN(SlideBandIndex.Reach(2e150, 0)));
        Assert.True(double.IsNaN(SlideBandIndex.Reach(0, double.PositiveInfinity)));
        var index = SlideBandIndex.Build(SlideBand.Axis.PerpendicularY, entities.Take(30).ToList(), 0, 0, 0, SlideBandIndex.Kind.All);
        Assert.NotNull(index);
        Assert.False(index!.TryPoint(double.NaN, out _, out _));
        Assert.False(index.TryRange(0, double.PositiveInfinity, new List<int>()));
    }

    /// <summary>
    /// With <paramref name="indexed"/>, appends far-off decoy lines parallel to the push so the
    /// list reaches the size that builds a band index; they never emit.
    /// </summary>
    private static List<Entity> Pad(List<Entity> entities, bool indexed)
    {
        if (!indexed)
            return entities;
        var padded = new List<Entity>(entities);
        for (var i = 0; padded.Count < SlideBandIndex.MinimumEntities + 6; i++)
            padded.Add(new Line(new Vector(0, 1e4 + i), new Vector(1, 1e4 + i)));
        return padded;
    }

    /// <summary>Asserts identical event sequences and slide results; returns the event count.</summary>
    private static int CompareEvents(
        List<Entity> moving,
        List<Entity> stationary,
        double dx,
        double dy,
        double dirX,
        double dirY,
        bool vertices = true,
        bool resolve = true
    )
    {
        var movingVertices = vertices ? SpatialQuery.ExtractEntityVertices(moving) : Array.Empty<Vector>();
        var stationaryVertices = vertices
            ? SpatialQuery.ExtractEntityVertices(stationary)
            : Array.Empty<Vector>();
        return CompareEvents(moving, movingVertices, stationary, stationaryVertices, dx, dy, dirX, dirY, resolve);
    }

    /// <summary>One moving vertex set, no moving entities, a push along +X.</summary>
    private static int CompareVertexEvents(Vector[] movingVertices, List<Entity> stationary, double dx, double dy) =>
        CompareEvents(new(), movingVertices, stationary, Array.Empty<Vector>(), dx, dy, 1, 0, resolve: false);

    private static int CompareEvents(
        List<Entity> moving,
        Vector[] movingVertices,
        List<Entity> stationary,
        Vector[] stationaryVertices,
        double dx,
        double dy,
        double dirX,
        double dirY,
        bool resolve
    )
    {
        var current = new EntitySlideEvents(moving, movingVertices, dx, dy, stationary, stationaryVertices,
            dirX, dirY, arcToLine: true);
        var legacy = new LegacyEntitySlideEvents(moving, movingVertices, dx, dy, stationary, stationaryVertices,
            dirX, dirY, arcToLine: true);

        var expected = new SlideEventList { Events = new() };
        legacy.Enumerate(ref expected);
        var actual = new SlideEventList { Events = new() };
        current.Enumerate(ref actual);

        Assert.Equal(expected.Events.Count, actual.Events.Count);
        for (var i = 0; i < expected.Events.Count; i++)
        {
            Assert.Equal(Bits(expected.Events[i].Distance), Bits(actual.Events[i].Distance));
            AssertSameBits(expected.Events[i].MovingPoint, actual.Events[i].MovingPoint);
            AssertSameBits(expected.Events[i].StationaryPoint, actual.Events[i].StationaryPoint);
        }

        if (!resolve)
            return expected.Events.Count;

        var contacts = new SlideContactClassifier(moving, stationary).Prepare()
            .At(new Vector(dx, dy), Vector.Zero);
        Assert.Equal(
            Bits(SlideResolver.FirstBlocking(ref legacy, contacts, dirX, dirY)),
            Bits(SlideResolver.FirstBlocking(ref current, contacts, dirX, dirY)));
        return expected.Events.Count;
    }

    private static void AssertSameBits(Vector expected, Vector actual)
    {
        Assert.Equal(Bits(expected.X), Bits(actual.X));
        Assert.Equal(Bits(expected.Y), Bits(actual.Y));
    }

    /// <summary>IEEE bits, with every NaN mapped to one value: the JIT tier decides a NaN's
    /// sign, so the kernels' NaN contact points are equal but not bit-identical.</summary>
    private static long Bits(double value) => double.IsNaN(value) ? 0x7FF8000000000000 : BitConverter.DoubleToInt64Bits(value);

    /// <summary>A closed boundary of lines and arcs around a random star, sometimes with a hole circle.</summary>
    private static List<Entity> RandomBoundary(Random random, double scale, int minSides, int maxSides)
    {
        var entities = new List<Entity>();
        var count = random.Next(minSides, maxSides);
        var cx = (random.NextDouble() - 0.5) * scale;
        var cy = (random.NextDouble() - 0.5) * scale;
        var points = new List<Vector>();
        for (var i = 0; i < count; i++)
        {
            var angle = Angle.TwoPI * (i + random.NextDouble() * 0.8) / count;
            var radius = scale * (0.2 + random.NextDouble());
            points.Add(new Vector(cx + radius * System.Math.Cos(angle), cy + radius * System.Math.Sin(angle)));
        }
        for (var i = 0; i < count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % count];
            if (random.NextDouble() < 0.3)
            {
                // A bulging arc through both endpoints.
                var mid = new Vector((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var half = a.DistanceTo(b) / 2;
                var bulge = half * (0.2 + random.NextDouble());
                var nx = -(b.Y - a.Y) / (2 * half);
                var ny = (b.X - a.X) / (2 * half);
                var r = (half * half + bulge * bulge) / (2 * bulge);
                var center = new Vector(mid.X - nx * (r - bulge), mid.Y - ny * (r - bulge));
                var start = System.Math.Atan2(a.Y - center.Y, a.X - center.X);
                var end = System.Math.Atan2(b.Y - center.Y, b.X - center.X);
                entities.Add(new Arc(center, r, Angle.NormalizeRad(start), Angle.NormalizeRad(end), random.Next(2) == 0));
            }
            else
            {
                entities.Add(new Line(a, b));
            }
        }
        if (random.NextDouble() < 0.4)
            entities.Add(new Circle(new Vector(cx, cy), scale * 0.1 * random.NextDouble()));
        return entities;
    }

    /// <summary>
    /// Offsets that put the moving boundary well behind the stationary one along the push,
    /// with perpendicular shifts that land vertices exactly on, or a hair beside, stationary
    /// endpoints and curve extremes, plus plain random shifts.
    /// </summary>
    private static IEnumerable<(double Dx, double Dy)> Offsets(Random random, List<Entity> moving,
        List<Entity> stationary, double dirX, double dirY, double scale)
    {
        var back = 6 * scale;
        var alongX = -dirX * back;
        var alongY = -dirY * back;
        var perpIsY = dirY == 0;
        var movingVertices = SpatialQuery.ExtractEntityVertices(moving);
        var targets = new List<double>();
        foreach (var entity in stationary)
        {
            switch (entity)
            {
                case Line line:
                    targets.Add(perpIsY ? line.pt1.Y : line.pt1.X);
                    targets.Add(perpIsY ? line.pt2.Y : line.pt2.X);
                    break;
                case Arc arc:
                    targets.Add((perpIsY ? arc.Center.Y : arc.Center.X) + arc.Radius);
                    targets.Add((perpIsY ? arc.Center.Y : arc.Center.X) - arc.Radius);
                    break;
                case Circle circle:
                    targets.Add((perpIsY ? circle.Center.Y : circle.Center.X) + circle.Radius);
                    break;
            }
        }

        var nudges = new[] { 0, 1e-12, -1e-12, 1e-9, -1e-9, 4e-6, -4e-6, 1.2e-5, -1.2e-5 };
        for (var k = 0; k < 6; k++)
        {
            var target = targets[random.Next(targets.Count)];
            var vertex = movingVertices[random.Next(movingVertices.Length)];
            var shift = target - (perpIsY ? vertex.Y : vertex.X)
                + nudges[random.Next(nudges.Length)] * System.Math.Max(1, scale);
            yield return perpIsY ? (alongX, shift) : (shift, alongY);
        }
        for (var k = 0; k < 2; k++)
        {
            var shift = (random.NextDouble() - 0.5) * 3 * scale;
            yield return perpIsY ? (alongX, shift) : (shift, alongY);
        }
    }
}

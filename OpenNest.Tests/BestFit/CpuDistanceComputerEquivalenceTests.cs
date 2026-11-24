using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

/// <summary>
/// <see cref="CpuDistanceComputer"/> prepares its two boundaries once per batch
/// (<see cref="SlideBatch"/>) and passes over pairs that cannot change a slide. These tests
/// compare every distance it returns, bit for bit, with the frozen pre-change
/// <see cref="LegacyCpuDistanceComputer"/> (commit 6067d0c8), which runs Core's per-slide
/// event sources once per offset.
/// </summary>
public class CpuDistanceComputerEquivalenceTests
{
    public static IEnumerable<object[]> Pairs()
    {
        var names = Boundaries.Keys.ToList();
        for (var i = 0; i < names.Count; i++)
        {
            yield return new object[] { names[i], names[i], 0.0 };
            yield return new object[] { names[i], names[i], 180.0 };
            yield return new object[] { names[i], names[i], 33.5 };
            yield return new object[] { names[i], names[(i + 5) % names.Count], 90.0 };
        }
    }

    public static IEnumerable<object[]> LinePairs() =>
        Pairs().Where(pair =>
            Boundaries[(string)pair[0]]().All(e => e is Line)
            && Boundaries[(string)pair[1]]().All(e => e is Line));

    public static IEnumerable<object[]> Drawings() => Parts.Keys.Select(name => new object[] { name });

    private static readonly Dictionary<string, Func<List<Entity>>> Boundaries = new()
    {
        // Lines only.
        ["rectangle"] = () => Loop((0, 0), (6, 0), (6, 3), (0, 3)),
        ["notched"] = () => Loop((0, 0), (3, 0), (3, 1.5), (4.25, 1.5), (4.25, 0), (8, 0), (8, 5), (0, 5)),
        ["channel"] = () => Loop((0, 0), (7, 0), (7, 4), (6, 4), (6, 1), (1, 1), (1, 4), (0, 4)),
        ["triangle"] = () => Loop((0, 0), (5, 0.5), (1.5, 4)),

        // Enough lines for band indexes: teeth that interlock, and a curve as a polyline.
        ["gear"] = () => Loop(Gear(16, 5, 3.9)),
        ["polyline ellipse"] = () => Loop(Ellipse(64, 4, 1.5)),

        // Convex arcs, traversed both ways.
        ["rounded rectangle"] = () => RoundedRectangle(6, 3.5, 0.75),
        ["rounded rectangle clockwise"] = () => Reversed(RoundedRectangle(6, 3.5, 0.75)),
        ["slot"] = Slot,

        // A concave arc, and arcs only.
        ["filleted ell"] = FilletedEll,
        ["lens"] = Lens,

        // Lines, convex bumps and concave scallops, enough of them for band indexes.
        ["scalloped"] = () => Scalloped(30, 5),

        // Full circles and loops inside loops.
        ["disc"] = () => new List<Entity> { new Circle(new Vector(2.5, 2.5), 2.5) },
        ["full arc"] = () => new List<Entity> { new Arc(1, 2, 2.25, 0, Angle.TwoPI) },
        ["ring"] = () => new List<Entity> { new Circle(new Vector(3, 3), 3), new Circle(new Vector(3, 3), 1.75) },
        ["plate with holes"] = PlateWithHoles,

        // What offsetting by zero spacing leaves: a zero-radius arc at every corner, which
        // the contact topology cannot resolve, so that every contact blocks.
        ["zero radius corners"] = () => ZeroRadiusCorners(Loop((0, 0), (6, 0), (6, 3), (2, 3), (2, 5), (0, 5))),
        ["zero radius gear"] = () => ZeroRadiusCorners(Loop(Gear(14, 4, 3.2))),

        // A boundary that is not a closed loop.
        ["open chain"] = () => Chain((0, 0), (5, 0), (5, 3), (2, 4)),
    };

    private static readonly Dictionary<string, Func<Drawing>> Parts = new()
    {
        ["rounded rectangle"] = () => new RoundedRectangleShape { Length = 6, Width = 3.5, Radius = 0.75 }.GetDrawing(),
        ["tee"] = () => new TShape { Width = 10, Height = 8 }.GetDrawing(),
        ["ell"] = () => new LShape { Width = 7, Height = 9, LegWidth = 2, LegHeight = 2.5 }.GetDrawing(),
        ["disc"] = () => new CircleShape { Diameter = 5 }.GetDrawing(),
        ["hexagon"] = () => new NgonShape { Sides = 6, Width = 4 }.GetDrawing(),
        ["notched arcs"] = NotchedArcs,
        ["gear"] = () => Outline("gear", Gear(16, 5, 3.9)),
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ComputeDistances_MatchesLegacyBitwise(string stationaryName, string movingName, double degrees)
    {
        var stationary = Boundaries[stationaryName]();
        var moving = Turned(Boundaries[movingName](), degrees);
        var label = $"{stationaryName} / {movingName} at {degrees}";

        var legacy = new LegacyCpuDistanceComputer();
        var offsets = Offsets(stationary, moving);
        var expected = legacy.ComputeDistances(stationary, moving, offsets);
        AssertSameBits(expected, new CpuDistanceComputer().ComputeDistances(stationary, moving, offsets), offsets, label);

        // Slides that start in contact, as the pair search's settling slides do.
        var sideways = Sideways(offsets, expected);
        var expectedSideways = legacy.ComputeDistances(stationary, moving, sideways);
        AssertSameBits(expectedSideways, new CpuDistanceComputer().ComputeDistances(stationary, moving, sideways), sideways, label + ", sideways");

        // Offsets no slide can come from. They get a batch of their own: one of them in a
        // batch leaves its axis without an index.
        var degenerate = Degenerate();
        AssertSameBits(
            legacy.ComputeDistances(stationary, moving, degenerate),
            new CpuDistanceComputer().ComputeDistances(stationary, moving, degenerate),
            degenerate,
            label + ", degenerate");

        // One prepared pair for several batches, in either order.
        var prepared = new CpuDistanceComputer().Prepare(stationary, moving);
        AssertSameBits(expected, prepared.ComputeDistances(offsets), offsets, label + ", prepared");
        AssertSameBits(expectedSideways, prepared.ComputeDistances(sideways), sideways, label + ", prepared sideways");

        prepared = new CpuDistanceComputer().Prepare(stationary, moving);
        AssertSameBits(expectedSideways, prepared.ComputeDistances(sideways), sideways, label + ", prepared sideways first");
        AssertSameBits(expected, prepared.ComputeDistances(offsets), offsets, label + ", prepared second");

        // A prepared pair asked for slides one at a time: each batch is sized for its own
        // offset, so later ones start outside what earlier ones prepared.
        prepared = new CpuDistanceComputer().Prepare(stationary, moving);
        for (var i = 0; i < offsets.Length; i += 37)
        {
            var one = new[] { offsets[i] };
            AssertSameBits(new[] { expected[i] }, prepared.ComputeDistances(one), one, label + $", single {i}");
        }

        // The comparison is only worth something when it sees slides that stop and slides
        // that never touch.
        Assert.Contains(expected, d => d < double.MaxValue);
        Assert.Contains(expected, d => d >= double.MaxValue);
    }

    [Theory]
    [MemberData(nameof(LinePairs))]
    public void ComputeDistances_Lines_MatchesLegacyBitwise(string stationaryName, string movingName, double degrees)
    {
        var stationaryEntities = Boundaries[stationaryName]();
        var movingEntities = Turned(Boundaries[movingName](), degrees);
        var stationary = stationaryEntities.Cast<Line>().ToList();
        var moving = movingEntities.Cast<Line>().ToList();
        var label = $"{stationaryName} / {movingName} at {degrees}, lines";

        var legacy = new LegacyCpuDistanceComputer();
        var offsets = Offsets(stationaryEntities, movingEntities);
        var expected = legacy.ComputeDistances(stationary, moving, offsets);
        AssertSameBits(expected, new CpuDistanceComputer().ComputeDistances(stationary, moving, offsets), offsets, label);

        var sideways = Sideways(offsets, expected);
        AssertSameBits(
            legacy.ComputeDistances(stationary, moving, sideways),
            new CpuDistanceComputer().ComputeDistances(stationary, moving, sideways),
            sideways,
            label + ", sideways");

        var degenerate = Degenerate();
        AssertSameBits(
            legacy.ComputeDistances(stationary, moving, degenerate),
            new CpuDistanceComputer().ComputeDistances(stationary, moving, degenerate),
            degenerate,
            label + ", degenerate");
    }

    /// <summary>
    /// The batches the pair search itself asks for: its grid of first slides and the
    /// settling slides from where those came to rest, at no spacing (zero-radius offset
    /// corners) and at a spacing (rounded ones).
    /// </summary>
    [Theory]
    [MemberData(nameof(Drawings))]
    public void RotationSlideStrategy_MatchesLegacyBitwise(string name)
    {
        var drawing = CanonicalFrame.AsCanonicalCopy(Parts[name]());
        var batches = 0;

        foreach (var spacing in new[] { 0.0, 0.25 })
        {
            foreach (var degrees in new[] { 0.0, 90, 180, 37, 211.5 })
            {
                var rotation = Angle.ToRadians(degrees);
                var label = $"{name}, spacing {spacing}, {degrees} degrees";

                var recorder = new Recorder(new LegacyCpuDistanceComputer());
                var expected = new RotationSlideStrategy(rotation, 1, string.Empty, recorder)
                    .GenerateCandidates(drawing, spacing, 0.25);
                var actual = new RotationSlideStrategy(rotation, 1, string.Empty, new CpuDistanceComputer())
                    .GenerateCandidates(drawing, spacing, 0.25);

                Assert.Equal(expected.Count, actual.Count);
                for (var i = 0; i < expected.Count; i++)
                {
                    Assert.True(
                        Bits(expected[i].Part2Offset.X) == Bits(actual[i].Part2Offset.X)
                        && Bits(expected[i].Part2Offset.Y) == Bits(actual[i].Part2Offset.Y)
                        && Bits(expected[i].Part2Rotation) == Bits(actual[i].Part2Rotation)
                        && expected[i].TestNumber == actual[i].TestNumber,
                        $"{label}: candidate {i}");
                }

                foreach (var (stationary, moving, offsets, distances) in recorder.Calls)
                {
                    AssertSameBits(distances, new CpuDistanceComputer().ComputeDistances(stationary, moving, offsets), offsets, label);
                    batches++;
                }
            }
        }

        Assert.True(batches >= 10, $"{name}: {batches} batches");
    }

    /// <summary>
    /// A ray may hit a line up to <see cref="Tolerance.Epsilon"/> of its length past its
    /// end, which is nearer than the line's own extent. Here that grazing hit is the first
    /// contact, by less than the hit lies past the end, and another vertex has already
    /// found the contact just behind it: skipping the pair on its extent alone would
    /// return the later contact. Turned through every quarter, for each push axis and sign.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ComputeDistances_HitPastASegmentEnd_IsTheFirstContact(double degrees)
    {
        // The moving edge's top end meets the wall after 7.99997; its bottom end passes
        // 5e-5 below the sloping line's start and meets its extension after 7.99995.
        var stationary = Turned(
            new List<Entity>
            {
                new Line(new Vector(0, 0), new Vector(10, 10)),
                new Line(new Vector(-3e-5, 20), new Vector(-3e-5, 30)),
            },
            degrees);
        var moving = Turned(
            new List<Entity> { new Line(new Vector(-8, 25), new Vector(-8, -5e-5)) },
            degrees);

        var radians = Angle.ToRadians(degrees);
        var dirX = System.Math.Round(System.Math.Cos(radians));
        var dirY = System.Math.Round(System.Math.Sin(radians));
        var offsets = new[] { new SlideOffset(0, 0, dirX, dirY), new SlideOffset(-dirX, -dirY, dirX, dirY) };

        var expected = new LegacyCpuDistanceComputer().ComputeDistances(stationary, moving, offsets);
        Assert.Equal(7.99995, expected[0], 9);
        Assert.Equal(8.99995, expected[1], 9);

        AssertSameBits(expected, new CpuDistanceComputer().ComputeDistances(stationary, moving, offsets), offsets, $"grazing at {degrees}");
        AssertSameBits(expected, new CpuDistanceComputer().Prepare(stationary, moving).ComputeDistances(offsets), offsets, $"grazing at {degrees}, prepared");
    }

    [Fact]
    public void ComputeDistances_NoOffsets_IsEmpty()
    {
        var boundary = Boundaries["rounded rectangle"]();
        Assert.Empty(new CpuDistanceComputer().ComputeDistances(boundary, boundary, Array.Empty<SlideOffset>()));
        Assert.Empty(new CpuDistanceComputer().Prepare(boundary, boundary).ComputeDistances(Array.Empty<SlideOffset>()));
    }

    [Fact]
    public void ComputeDistances_NoEntities_NeverTouches()
    {
        var offsets = new[] { new SlideOffset(5, 0, -1, 0), new SlideOffset(0, 5, 0, -1), new SlideOffset(1, 1, 0.6, 0.8) };
        var empty = new List<Entity>();
        var boundary = Boundaries["rounded rectangle"]();
        var legacy = new LegacyCpuDistanceComputer();

        foreach (var (stationary, moving) in new[] { (empty, boundary), (boundary, empty), (empty, empty) })
        {
            AssertSameBits(
                legacy.ComputeDistances(stationary, moving, offsets),
                new CpuDistanceComputer().ComputeDistances(stationary, moving, offsets),
                offsets,
                "no entities");
        }
    }

    [Fact]
    public void Normalize_MatchesNormalizeRadBitwise()
    {
        var random = new Random(4711);
        var angles = new List<double>
        {
            0, -0.0, double.Epsilon, -double.Epsilon, Angle.TwoPI, -Angle.TwoPI, System.Math.PI, -System.Math.PI,
            System.Math.BitDecrement(Angle.TwoPI), System.Math.BitIncrement(Angle.TwoPI),
            System.Math.BitDecrement(-Angle.TwoPI), System.Math.BitIncrement(-Angle.TwoPI),
            2 * Angle.TwoPI, -2 * Angle.TwoPI, 1e300, -1e300,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity,
        };
        for (var i = 0; i < 2000; i++)
            angles.Add((random.NextDouble() - 0.5) * 40);

        foreach (var angle in angles)
            Assert.Equal(Bits(Angle.NormalizeRad(angle)), Bits(SlideBatch.Normalize(angle)));
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static void AssertSameBits(double[] expected, double[] actual, SlideOffset[] offsets, string label)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            if (Bits(expected[i]) == Bits(actual[i]))
                continue;

            var offset = offsets[i];
            Assert.Fail(
                $"{label}: slide {i} from ({offset.Dx:R}, {offset.Dy:R}) along ({offset.DirX:R}, {offset.DirY:R}): "
                + $"expected {expected[i]:R}, was {actual[i]:R}");
        }
    }

    /// <summary>
    /// Starts well clear of the stationary boundary, with the bounding boxes touching and
    /// overlapping, for each axis push; across the push, positions that miss, graze either
    /// end by amounts around every tolerance in play, sit level with an edge, and sweep the
    /// whole range. Then pushes that are not along an axis.
    /// </summary>
    private static SlideOffset[] Offsets(List<Entity> stationary, List<Entity> moving)
    {
        var s = Bounds(stationary);
        var m = Bounds(moving);
        var offsets = new List<SlideOffset>();
        var random = new Random(12345);
        double[] nudges =
        {
            0, 1e-9, -1e-9, 1e-7, -1e-7, 1e-6, -1e-6, 4e-6, -4e-6, 1e-5, -1e-5,
            3e-5, -3e-5, 1e-4, -1e-4, 1e-3, -1e-3, 0.02, -0.02,
        };

        foreach (var (dirX, dirY) in new[] { (-1.0, 0.0), (0.0, -1.0), (1.0, 0.0), (0.0, 1.0) })
        {
            var horizontal = dirY == 0;
            var sign = dirX + dirY;
            var (sLow, sHigh, mLow, mHigh) = horizontal ? (s.MinY, s.MaxY, m.MinY, m.MaxY) : (s.MinX, s.MaxX, m.MinX, m.MaxX);
            var (sNear, sFar, mNear, mFar) = horizontal ? (s.MinX, s.MaxX, m.MinX, m.MaxX) : (s.MinY, s.MaxY, m.MinY, m.MaxY);
            var span = sHigh - sLow + (mHigh - mLow);

            // Where the moving box rests against the stationary box on the side it comes from.
            var against = sign < 0 ? sFar - mNear : sNear - mFar;
            var starts = new[] { against - sign * 5, against, against + sign * 0.4 * (mFar - mNear) };

            var across = new List<double>();
            foreach (var nudge in nudges)
            {
                across.Add(sHigh - mLow + nudge);
                across.Add(sLow - mHigh + nudge);
                across.Add(sHigh - mLow + nudge * span);
                across.Add(sLow - mHigh + nudge * span);
                across.Add(sLow - mLow + nudge);
                across.Add(sHigh - mHigh + nudge);
            }
            for (var i = 0; i <= 24; i++)
                across.Add(sLow - mHigh - 0.5 + (span + 1) * i / 24);
            for (var i = 0; i < 24; i++)
                across.Add(sLow - mHigh + span * random.NextDouble());

            foreach (var start in starts)
            {
                foreach (var level in across)
                    offsets.Add(horizontal ? new SlideOffset(start, level, dirX, dirY) : new SlideOffset(level, start, dirX, dirY));
            }
        }

        var diagonal = System.Math.Sqrt(0.5);
        foreach (var (dirX, dirY) in new[] { (0.6, 0.8), (-0.8, 0.6), (diagonal, -diagonal), (-0.28, -0.96), (2.0, 0.0) })
        {
            var centerX = (s.MinX + s.MaxX - m.MinX - m.MaxX) / 2;
            var centerY = (s.MinY + s.MaxY - m.MinY - m.MaxY) / 2;
            var reach = s.MaxX - s.MinX + s.MaxY - s.MinY + m.MaxX - m.MinX + m.MaxY - m.MinY;
            for (var i = 0; i < 40; i++)
            {
                var back = reach * (0.2 + random.NextDouble());
                var side = reach * (random.NextDouble() - 0.5);
                offsets.Add(new SlideOffset(
                    centerX - dirX * back - dirY * side,
                    centerY - dirY * back + dirX * side,
                    dirX,
                    dirY));
            }
        }

        return offsets.ToArray();
    }

    /// <summary>Both sideways slides from where each slide came to rest, as the pair search settles.</summary>
    private static SlideOffset[] Sideways(SlideOffset[] offsets, double[] distances)
    {
        var sideways = new List<SlideOffset>();
        for (var i = 0; i < offsets.Length; i += 2)
        {
            var distance = distances[i];
            if (distance >= double.MaxValue || distance < 0)
                continue;

            var offset = offsets[i];
            var dx = offset.Dx + offset.DirX * distance;
            var dy = offset.Dy + offset.DirY * distance;
            sideways.Add(new SlideOffset(dx, dy, -offset.DirY, offset.DirX));
            sideways.Add(new SlideOffset(dx, dy, offset.DirY, -offset.DirX));
        }
        return sideways.ToArray();
    }

    private static SlideOffset[] Degenerate() =>
        new[]
        {
            new SlideOffset(double.NaN, 0, -1, 0),
            new SlideOffset(0, double.NaN, 0, 1),
            new SlideOffset(double.PositiveInfinity, 0, -1, 0),
            new SlideOffset(1, double.NegativeInfinity, 0, -1),
            new SlideOffset(1e200, 1, -1, 0),
            new SlideOffset(1e155, 0.5, 1, 0),
            new SlideOffset(0.5, -1e160, 0, 1),
            new SlideOffset(9, 1, -1, 0),
            new SlideOffset(1, 9, 0, -1),
            new SlideOffset(3, 3, 0, 0),
            new SlideOffset(3, 3, double.NaN, 0),
            new SlideOffset(9, 1, -1, double.NaN),
        };

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(List<Entity> entities)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var entity in entities)
        {
            var box = entity.BoundingBox;
            minX = System.Math.Min(minX, box.Left);
            minY = System.Math.Min(minY, box.Bottom);
            maxX = System.Math.Max(maxX, box.Right);
            maxY = System.Math.Max(maxY, box.Top);
        }
        return (minX, minY, maxX, maxY);
    }

    private static List<Entity> Turned(List<Entity> entities, double degrees)
    {
        if (degrees == 0)
            return entities;

        foreach (var entity in entities)
            entity.Rotate(Angle.ToRadians(degrees));
        return entities;
    }

    private static List<Entity> Loop(params (double X, double Y)[] points)
    {
        var entities = Chain(points);
        entities.Add(new Line(new Vector(points[^1].X, points[^1].Y), new Vector(points[0].X, points[0].Y)));
        return entities;
    }

    private static List<Entity> Chain(params (double X, double Y)[] points)
    {
        var entities = new List<Entity>();
        for (var i = 0; i + 1 < points.Length; i++)
        {
            entities.Add(new Line(
                new Vector(points[i].X, points[i].Y),
                new Vector(points[i + 1].X, points[i + 1].Y)));
        }
        return entities;
    }

    /// <summary>A loop traversed the other way round.</summary>
    private static List<Entity> Reversed(List<Entity> entities)
    {
        entities.Reverse();
        foreach (var entity in entities)
            entity.Reverse();
        return entities;
    }

    private static (double X, double Y)[] Gear(int teeth, double outer, double root)
    {
        var points = new List<(double, double)>();
        var step = Angle.TwoPI / teeth;
        for (var i = 0; i < teeth; i++)
        {
            var a = i * step;
            points.Add((outer * System.Math.Cos(a), outer * System.Math.Sin(a)));
            points.Add((outer * System.Math.Cos(a + step * 0.3), outer * System.Math.Sin(a + step * 0.3)));
            points.Add((root * System.Math.Cos(a + step * 0.45), root * System.Math.Sin(a + step * 0.45)));
            points.Add((root * System.Math.Cos(a + step * 0.85), root * System.Math.Sin(a + step * 0.85)));
        }
        return points.ToArray();
    }

    private static (double X, double Y)[] Ellipse(int segments, double a, double b)
    {
        var points = new (double, double)[segments];
        for (var i = 0; i < segments; i++)
        {
            var t = i * Angle.TwoPI / segments;
            points[i] = (a * System.Math.Cos(t), b * System.Math.Sin(t));
        }
        return points;
    }

    private static List<Entity> RoundedRectangle(double length, double width, double r) =>
        new()
        {
            new Line(new Vector(r, 0), new Vector(length - r, 0)),
            new Arc(length - r, r, r, 3 * Angle.HalfPI, 0),
            new Line(new Vector(length, r), new Vector(length, width - r)),
            new Arc(length - r, width - r, r, 0, Angle.HalfPI),
            new Line(new Vector(length - r, width), new Vector(r, width)),
            new Arc(r, width - r, r, Angle.HalfPI, System.Math.PI),
            new Line(new Vector(0, width - r), new Vector(0, r)),
            new Arc(r, r, r, System.Math.PI, 3 * Angle.HalfPI),
        };

    private static List<Entity> Slot() =>
        new()
        {
            new Line(new Vector(1, 0), new Vector(6, 0)),
            new Arc(6, 1, 1, 3 * Angle.HalfPI, Angle.HalfPI),
            new Line(new Vector(6, 2), new Vector(1, 2)),
            new Arc(1, 1, 1, Angle.HalfPI, 3 * Angle.HalfPI),
        };

    /// <summary>An ell whose inside corner is a concave fillet and whose toe is rounded.</summary>
    private static List<Entity> FilletedEll() =>
        new()
        {
            new Line(new Vector(0, 0), new Vector(6, 0)),
            new Line(new Vector(6, 0), new Vector(6, 1.5)),
            new Arc(5.5, 1.5, 0.5, 0, Angle.HalfPI),
            new Line(new Vector(5.5, 2), new Vector(3, 2)),
            new Arc(3, 3, 1, 3 * Angle.HalfPI, System.Math.PI, true),
            new Line(new Vector(2, 3), new Vector(2, 6)),
            new Line(new Vector(2, 6), new Vector(0, 6)),
            new Line(new Vector(0, 6), new Vector(0, 0)),
        };

    /// <summary>The overlap of two radius-5 discs 6 apart: two arcs meeting at sharp tips.</summary>
    private static List<Entity> Lens() =>
        new()
        {
            new Arc(0, 0, 5, Angle.NormalizeRad(System.Math.Atan2(-4, 3)), System.Math.Atan2(4, 3)),
            new Arc(6, 0, 5, System.Math.Atan2(4, -3), Angle.NormalizeRad(System.Math.Atan2(-4, -3))),
        };

    /// <summary>
    /// Points round a circle joined in turn by a line, a bump outward and a scallop inward.
    /// </summary>
    private static List<Entity> Scalloped(int sides, double radius)
    {
        var entities = new List<Entity>();
        for (var i = 0; i < sides; i++)
        {
            var a = i * Angle.TwoPI / sides;
            var b = (i + 1) * Angle.TwoPI / sides;
            var from = new Vector(radius * System.Math.Cos(a), radius * System.Math.Sin(a));
            var to = new Vector(radius * System.Math.Cos(b), radius * System.Math.Sin(b));

            if (i % 3 == 0)
            {
                entities.Add(new Line(from, to));
                continue;
            }

            var center = new Vector((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var r = from.DistanceTo(to) / 2;
            var start = Angle.NormalizeRad(System.Math.Atan2(from.Y - center.Y, from.X - center.X));
            var end = Angle.NormalizeRad(System.Math.Atan2(to.Y - center.Y, to.X - center.X));
            entities.Add(new Arc(center.X, center.Y, r, start, end, i % 3 == 2));
        }
        return entities;
    }

    private static List<Entity> PlateWithHoles()
    {
        var entities = Loop((0, 0), (9, 0), (9, 6), (0, 6));
        entities.Add(new Circle(new Vector(2.5, 3), 1.25));
        entities.AddRange(Loop((5, 2), (5, 4.5), (7.5, 4.5), (7.5, 2)));
        return entities;
    }

    /// <summary>The loop with a zero-radius arc turning each corner, as a zero offset leaves it.</summary>
    private static List<Entity> ZeroRadiusCorners(List<Entity> loop)
    {
        var entities = new List<Entity>();
        for (var i = 0; i < loop.Count; i++)
        {
            var line = (Line)loop[i];
            var next = (Line)loop[(i + 1) % loop.Count];
            entities.Add(line);

            var leaving = System.Math.Atan2(line.EndPoint.Y - line.StartPoint.Y, line.EndPoint.X - line.StartPoint.X);
            var entering = System.Math.Atan2(next.EndPoint.Y - next.StartPoint.Y, next.EndPoint.X - next.StartPoint.X);
            entities.Add(new Arc(
                line.EndPoint.X,
                line.EndPoint.Y,
                0,
                Angle.NormalizeRad(leaving - Angle.HalfPI),
                Angle.NormalizeRad(entering - Angle.HalfPI)));
        }
        return entities;
    }

    /// <summary>A plate with a square notch, a round notch cut clockwise and two rounded corners.</summary>
    private static Drawing NotchedArcs()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 0);
        program.LineTo(3, 1.5);
        program.LineTo(4.25, 1.5);
        program.LineTo(4.25, 0);
        program.LineTo(8, 0);
        program.LineTo(8, 4);
        program.ArcTo(7, 5, 7, 4, RotationType.CCW);
        program.LineTo(5.5, 5);
        program.ArcTo(3.5, 5, 4.5, 5, RotationType.CW);
        program.LineTo(1, 5);
        program.ArcTo(0, 4, 1, 4, RotationType.CCW);
        program.LineTo(0, 0);
        return new Drawing("notched arcs", program);
    }

    private static Drawing Outline(string name, (double X, double Y)[] points)
    {
        var program = new Program();
        program.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
            program.LineTo(points[i].X, points[i].Y);
        program.LineTo(points[0].X, points[0].Y);
        return new Drawing(name, program);
    }

    private sealed class Recorder : IDistanceComputer
    {
        private readonly IDistanceComputer inner;

        public Recorder(IDistanceComputer inner)
        {
            this.inner = inner;
        }

        public List<(List<Entity> Stationary, List<Entity> Moving, SlideOffset[] Offsets, double[] Distances)> Calls { get; } = new();

        public double[] ComputeDistances(List<Line> stationaryLines, List<Line> movingTemplateLines, SlideOffset[] offsets) =>
            inner.ComputeDistances(stationaryLines, movingTemplateLines, offsets);

        public double[] ComputeDistances(List<Entity> stationaryEntities, List<Entity> movingEntities, SlideOffset[] offsets)
        {
            var distances = inner.ComputeDistances(stationaryEntities, movingEntities, offsets);
            Calls.Add((stationaryEntities, movingEntities, offsets, distances));
            return distances;
        }
    }
}

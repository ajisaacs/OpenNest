using System.Reflection;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class RayContactPrimitiveCharacterizationTests
{
    private delegate bool RaySolver(double vx, double vy, double cx, double cy, double radius,
        double dx, double dy, out double near, out double far);

    private static readonly RaySolver QuerySolver = Bind<RaySolver>(typeof(SpatialQuery), "SolveRayCircle");
    private static readonly RaySolver EventSolver = Bind<RaySolver>(typeof(SlideEvents), "SolveRayCircle");
    private static readonly Func<Arc?, double, double, double, bool> QueryAngle =
        Bind<Func<Arc?, double, double, double, bool>>(typeof(SpatialQuery), "ContainsContactAngle");
    private static readonly Func<Arc?, double, double, double, bool> EventAngle =
        Bind<Func<Arc?, double, double, double, bool>>(typeof(SlideEvents), "ContainsContactAngle");

    public static IEnumerable<object[]> Rays()
    {
        var epsilon = Tolerance.Epsilon;
        foreach (var ray in new[]
        {
            new[] { 0d, 0, 5, 0, 1, 1, 0 }, // both roots
            new[] { 0d, 0, 5, 1, 1, 1, 0 }, // tangent: two equal roots
            new[] { 0d, 0, 5, 2, 1, 1, 0 }, // miss and MaxValue outputs
            new[] { 0d, 0, -5, 0, 1, 1, 0 }, // both roots behind
            new[] { 0.5, 0, 0, 0, 1, 1, 0 }, // negative near root, forward far root
            new[] { 17d, -23, 22, -23, 1, 1, 0 }, // translated frame
            new[] { 17d, -23, 20, -19, 1, 0.6, 0.8 }, // non-cardinal direction
            new[] { 0d, 0, epsilon / 2, 0, 0, 1, 0 },
            new[] { 0d, 0, epsilon, 0, 0, 1, 0 },
            new[] { 0d, 0, epsilon * 2, 0, 0, 1, 0 },
            new[] { 0d, 0, -epsilon / 2, 0, 0, 1, 0 },
            new[] { 0d, 0, -epsilon, 0, 0, 1, 0 }, // arc strict / circle inclusive boundary
            new[] { 0d, 0, -epsilon * 2, 0, 0, 1, 0 },
            new[] { -2d, 3, 7, 5, 2.5, 0.6, 0.8 },
        })
            yield return new object[] { ray };
    }

    [Theory]
    [MemberData(nameof(Rays))]
    public void RayRoots_MatchFrozenQuadraticBitwise(double[] ray)
    {
        var expected = FrozenRoots(ray);
        foreach (var solver in new[] { QuerySolver, EventSolver })
        {
            var found = solver(ray[0], ray[1], ray[2], ray[3], ray[4], ray[5], ray[6], out var near, out var far);
            Assert.Equal(expected.Found, found);
            EqualBits(expected.Near, near);
            EqualBits(expected.Far, far);
        }
    }

    [Theory]
    [MemberData(nameof(Rays))]
    public void PublicRayQueriesAndTranslatedEvents_PreserveRootsSpansAndWitnessOrder(double[] ray)
    {
        var spans = new[]
        {
            (0d, 0d, false), // historical equal-angle full-circle membership
            (0d, Angle.TwoPI, false), // explicit full turn retains the historical seam rule
            (Angle.HalfPI, 3 * Angle.HalfPI, false),
            (3 * Angle.HalfPI, Angle.HalfPI, false),
            (Angle.HalfPI, 3 * Angle.HalfPI, true),
        };
        var circle = new Circle(ray[2], ray[3], ray[4]);
        var curves = new List<Entity> { circle };
        curves.AddRange(spans.Select(span => new Arc(ray[2], ray[3], ray[4], span.Item1, span.Item2, span.Item3)));
        foreach (var curve in curves)
        {
            var arc = curve as Arc;
            var expected = FrozenRayEvents(ray, arc);
            var distance = arc == null
                ? SpatialQuery.RayCircleDistance(ray[0], ray[1], ray[2], ray[3], ray[4], ray[5], ray[6])
                : SpatialQuery.RayArcDistance(ray[0], ray[1], ray[2], ray[3], ray[4], arc.StartAngle, arc.EndAngle,
                    arc.IsReversed, ray[5], ray[6]);
            EqualBits(expected.Count == 0 ? double.MaxValue : expected.Min(hit => hit.Distance), distance);

            // Reach both vertex phases through the public source, including nonzero local origins.
            var offset = new Vector(ray[2], ray[3]);
            var source = new EntitySlideEvents(new(), new[] { new Vector(ray[0], ray[1]) - offset },
                offset.X, offset.Y, new() { curve }, Array.Empty<Vector>(), ray[5], ray[6], false);
            var sink = new CaptureSink { Hits = new() };
            source.Enumerate(ref sink);
            EqualHits(expected, sink.Hits);

            var localCurve = curve.Clone();
            localCurve.Offset(-offset.X, -offset.Y);
            source = new EntitySlideEvents(new() { localCurve }, Array.Empty<Vector>(), offset.X, offset.Y,
                new(), new[] { new Vector(ray[0], ray[1]) }, -ray[5], -ray[6], false);
            sink = new CaptureSink { Hits = new() };
            source.Enumerate(ref sink);
            EqualHits(expected.Select(hit => hit with { Moving = hit.Stationary, Stationary = hit.Moving }).ToList(), sink.Hits);
        }
    }

    public static IEnumerable<object[]> ContactAngles()
    {
        var epsilon = Tolerance.Epsilon;
        yield return new object[] { 0d, Angle.HalfPI, false, 1d, 0d, 1d, true }; // start
        yield return new object[] { 0d, Angle.HalfPI, false, 0d, 1d, 1d, true }; // end
        yield return new object[] { 0d, Angle.HalfPI, false, -1d, 0d, 1d, false };
        yield return new object[] { 0d, Angle.HalfPI, false, 1d, -epsilon / 2, 1d, true };
        yield return new object[] { 0d, Angle.HalfPI, false, 1d, -epsilon * 2, 1d, false };
        yield return new object[] { 3 * Angle.HalfPI, Angle.HalfPI, false, 1d, 0d, 1d, true }; // wrapping
        yield return new object[] { 3 * Angle.HalfPI, Angle.HalfPI, false, -1d, 0d, 1d, false };
        yield return new object[] { Angle.HalfPI, 3 * Angle.HalfPI, true, 1d, 0d, 1d, true }; // reversed wrapping
        yield return new object[] { Angle.HalfPI, 3 * Angle.HalfPI, true, -1d, 0d, 1d, false };
        yield return new object[] { 0d, 0d, false, -1d, 0d, 1d, true }; // equal-angle full circle
        yield return new object[] { 0d, Angle.TwoPI, false, 1d, 0d, 1d, true }; // explicit full-turn seam
        yield return new object[] { 0d, Angle.TwoPI, false, -1d, 0d, 1d, false }; // freeze, do not repair
        yield return new object[] { 0d, Angle.HalfPI, false, -1d, 0d, 0d, true }; // point ignores angles
    }

    [Theory]
    [MemberData(nameof(ContactAngles))]
    public void ContactAngleWrappers_PreserveEndpointsWrappingAndHistoricalFullCircleRules(
        double start, double end, bool reversed, double x, double y, double radius, bool expected)
    {
        var arc = new Arc(17, -23, radius, start, end, reversed);
        Assert.Equal(expected, QueryAngle(arc, radius, x, y));
        Assert.Equal(expected, EventAngle(arc, radius, x, y));
    }

    [Fact]
    public void NullArc_IsAnUnrestrictedCircleRegardlessOfRadiusOrDirection()
    {
        foreach (var radius in new[] { 0d, 1d })
        {
            Assert.True(QueryAngle(null, radius, -1, 0));
            Assert.True(EventAngle(null, radius, -1, 0));
        }
    }

    public static IEnumerable<object[]> TangencySpans()
    {
        foreach (var spans in new[]
        {
            new[] { 0d, 0, 0, 0, 0, 0 },
            new[] { 0d, Angle.HalfPI, 0, Angle.HalfPI, System.Math.PI, 0 }, // endpoints
            new[] { 3 * Angle.HalfPI, Angle.HalfPI, 0, Angle.HalfPI, 3 * Angle.HalfPI, 0 },
            new[] { Angle.HalfPI, 3 * Angle.HalfPI, 1, 3 * Angle.HalfPI, Angle.HalfPI, 1 },
            new[] { Angle.HalfPI, 3 * Angle.HalfPI, 0, 0d, Angle.HalfPI, 0 }, // near root rejected
            new[] { 0d, Angle.TwoPI, 0, 0, Angle.TwoPI, 0 },
            new[] { 0.2, 0.3, 0, 0.2, 0.3, 0 }, // neither root in span
        })
            foreach (var swapped in new[] { false, true })
                yield return new object[] { spans, swapped };
    }

    [Theory]
    [MemberData(nameof(TangencySpans))]
    public void PublicCurveTangencyAndNativeArcEvents_PreserveKindRootAndSpanOrder(double[] spans, bool swapped)
    {
        var moving = new Arc(17, -23, swapped ? 3 : 1, spans[0], spans[1], spans[2] != 0);
        var stationary = new Arc(27, -23, swapped ? 1 : 3, spans[3], spans[4], spans[5] != 0);
        var expected = FrozenTangencies(moving, stationary);
        var distance = SpatialQuery.CurveTangencyDistance(moving.Center.X, moving.Center.Y, moving.Radius, moving,
            stationary.Center.X, stationary.Center.Y, stationary.Radius, stationary, 1, 0);
        EqualBits(expected.Count == 0 ? double.MaxValue : expected.Min(hit => hit.Distance), distance);
        // Empty vertex arrays isolate the public source's native curve phase, without flattening arcs.
        var source = new EntitySlideEvents(new() { moving }, Array.Empty<Vector>(), 0, 0,
            new() { stationary }, Array.Empty<Vector>(), 1, 0, false);
        var sink = new CaptureSink { Hits = new() };
        source.Enumerate(ref sink);
        EqualHits(expected, sink.Hits);

        // Early completion must keep the first emission, not a sorted/reduced event sequence.
        sink = new CaptureSink { Hits = new(), Limit = 1 };
        source.Enumerate(ref sink);
        EqualHits(expected.Take(1).ToList(), sink.Hits);
    }

    // Frozen from the pre-extraction quadratic. Do not route the oracle through production helpers.
    private static (bool Found, double Near, double Far) FrozenRoots(double[] input)
    {
        var relativeX = input[0] - input[2];
        var relativeY = input[1] - input[3];
        var quadratic = input[5] * input[5] + input[6] * input[6];
        var linear = 2.0 * (relativeX * input[5] + relativeY * input[6]);
        var constant = relativeX * relativeX + relativeY * relativeY - input[4] * input[4];
        var delta = linear * linear - 4.0 * quadratic * constant;
        if (delta < 0)
            return (false, double.MaxValue, double.MaxValue);
        var rootDelta = System.Math.Sqrt(delta);
        var reciprocal = 1.0 / (2.0 * quadratic);
        return (true, (-linear - rootDelta) * reciprocal, (-linear + rootDelta) * reciprocal);
    }

    private static List<Hit> FrozenRayEvents(double[] input, Arc? span)
    {
        var roots = FrozenRoots(input);
        var result = new List<Hit>();
        if (!roots.Found)
            return result;
        foreach (var parameter in new[] { roots.Near, roots.Far })
        {
            if (span == null ? parameter < -Tolerance.Epsilon : parameter <= -Tolerance.Epsilon)
                continue;
            var witness = new Vector(input[0] + parameter * input[5], input[1] + parameter * input[6]);
            // Ray arcs retain their angular test even at radius zero (unlike tangencies).
            if (span != null && !Angle.IsBetweenRad(Angle.NormalizeRad(System.Math.Atan2(
                    witness.Y - input[3], witness.X - input[2])), span.StartAngle, span.EndAngle, span.IsReversed))
                continue;
            result.Add(new Hit(parameter > Tolerance.Epsilon ? parameter : 0, new Vector(input[0], input[1]), witness));
        }
        return result;
    }

    private static List<Hit> FrozenTangencies(Arc mover, Arc obstacle)
    {
        var result = new List<Hit>();
        for (var contactKind = 0; contactKind < 2; contactKind++)
        {
            var inside = contactKind == 1;
            var effectiveRadius = inside ? System.Math.Abs(mover.Radius - obstacle.Radius) : mover.Radius + obstacle.Radius;
            if (effectiveRadius == 0)
                continue;
            var roots = FrozenRoots(new[] { mover.Center.X, mover.Center.Y, obstacle.Center.X, obstacle.Center.Y,
                effectiveRadius, 1, 0 });
            if (!roots.Found)
                continue;
            foreach (var parameter in new[] { roots.Near, roots.Far })
            {
                if (parameter < -Tolerance.Epsilon)
                    continue;
                var relativeX = obstacle.Center.X - (mover.Center.X + parameter * 1);
                var relativeY = obstacle.Center.Y - (mover.Center.Y + parameter * 0);
                var moverSign = inside && mover.Radius < obstacle.Radius ? -1 : 1;
                var obstacleSign = inside ? moverSign : -1;
                if (!FrozenContains(mover, moverSign * relativeX, moverSign * relativeY)
                    || !FrozenContains(obstacle, obstacleSign * relativeX, obstacleSign * relativeY))
                    continue;
                var magnitude = System.Math.Sqrt(relativeX * relativeX + relativeY * relativeY);
                var unitX = magnitude > 0 ? relativeX / magnitude : 0;
                var unitY = magnitude > 0 ? relativeY / magnitude : 0;
                result.Add(new Hit(parameter > Tolerance.Epsilon ? parameter : 0,
                    new Vector(mover.Center.X + moverSign * mover.Radius * unitX, mover.Center.Y + moverSign * mover.Radius * unitY),
                    new Vector(obstacle.Center.X + obstacleSign * obstacle.Radius * unitX, obstacle.Center.Y + obstacleSign * obstacle.Radius * unitY)));
            }
        }
        return result;
    }

    private static bool FrozenContains(Arc span, double horizontal, double vertical) =>
        span.Radius == 0 || Angle.IsBetweenRad(Angle.NormalizeRad(System.Math.Atan2(vertical, horizontal)),
            span.StartAngle, span.EndAngle, span.IsReversed);

    private static T Bind<T>(Type owner, string method) where T : Delegate =>
        owner.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();

    private static void EqualBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static void EqualHits(List<Hit> expected, List<Hit> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            EqualBits(expected[index].Distance, actual[index].Distance);
            EqualBits(expected[index].Moving.X, actual[index].Moving.X);
            EqualBits(expected[index].Moving.Y, actual[index].Moving.Y);
            EqualBits(expected[index].Stationary.X, actual[index].Stationary.X);
            EqualBits(expected[index].Stationary.Y, actual[index].Stationary.Y);
        }
    }

    private readonly record struct Hit(double Distance, Vector Moving, Vector Stationary);

    private struct CaptureSink : ISlideEventSink
    {
        public List<Hit> Hits;
        public int Limit;
        public readonly bool IsDone => Limit > 0 && Hits.Count >= Limit;
        public void Add(double distance, Vector movingPoint, Vector stationaryPoint) =>
            Hits.Add(new Hit(distance, movingPoint, stationaryPoint));
    }
}

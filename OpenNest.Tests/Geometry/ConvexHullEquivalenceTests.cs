using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// <see cref="ConvexHull.Compute"/> sorts its points in a reused buffer instead of through a
/// stable two-key LINQ sort, and <see cref="Shape.ToPolygonWithTolerance"/> flattens arcs
/// straight into the polygon. These tests compare both bit for bit with the earlier code,
/// written out here.
/// </summary>
public class ConvexHullEquivalenceTests
{
    [Fact]
    public void SeededPoints_MatchStableSortHullBitwise()
    {
        var random = new Random(24112026);

        for (var i = 0; i < 4000; i++)
        {
            var points = MakePoints(random, i);

            Assert.True(
                Bits(LegacyHull(points)).SequenceEqual(Bits(ConvexHull.Compute(points).Vertices)),
                $"point set {i} (kind {i % 5}, {points.Count} points)"
            );
        }
    }

    [Fact]
    public void Compute_DoesNotReorderInput()
    {
        var random = new Random(3);
        var points = MakePoints(random, 0);
        var before = Bits(points);

        ConvexHull.Compute(points);

        Assert.True(before.SequenceEqual(Bits(points)));
    }

    [Fact]
    public void FlattenedShape_MatchesPerEntityPointsBitwise()
    {
        var random = new Random(11);

        for (var i = 0; i < 500; i++)
        {
            var shape = new Shape();
            var count = 1 + random.Next(6);

            for (var j = 0; j < count; j++)
            {
                if (random.Next(3) == 0)
                {
                    shape.Entities.Add(new Line(Point(random), Point(random)));
                    continue;
                }

                shape.Entities.Add(new Arc(
                    Point(random),
                    0.0005 + random.NextDouble() * 6,
                    random.NextDouble() * Angle.TwoPI,
                    random.NextDouble() * Angle.TwoPI,
                    random.Next(2) == 0
                ));
            }

            foreach (var tolerance in new[] { 0.01, 0.001 })
            {
                foreach (var circumscribe in new[] { false, true })
                {
                    var expected = new Polygon();

                    foreach (var entity in shape.Entities)
                    {
                        if (entity is Line line)
                            expected.Vertices.AddRange(new[] { line.StartPoint, line.EndPoint });
                        else if (entity is Arc arc)
                            expected.Vertices.AddRange(LegacyArcPoints(arc, LegacySegments(arc, tolerance), circumscribe));
                    }

                    expected.Close();
                    expected.Cleanup();

                    var actual = shape.ToPolygonWithTolerance(tolerance, circumscribe);

                    Assert.True(Bits(expected.Vertices).SequenceEqual(Bits(actual.Vertices)), $"shape {i}");
                }
            }
        }
    }

    /// <summary>The hull as computed before: a stable sort by X then Y, then the monotone chain.</summary>
    private static List<Vector> LegacyHull(IList<Vector> points)
    {
        if (points.Count < 3)
            return points.ToList();

        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var lower = new List<Vector>();

        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);

            lower.Add(p);
        }

        var upper = new List<Vector>();

        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];

            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);

            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);

        var hull = new Polygon();
        hull.Vertices.AddRange(lower);
        hull.Close();
        return hull.Vertices;
    }

    private static double Cross(Vector o, Vector a, Vector b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    /// <summary>Arc.SegmentsForTolerance as it was before its arithmetic moved to a shared helper.</summary>
    private static int LegacySegments(Arc arc, double tolerance)
    {
        if (tolerance >= arc.Radius)
            return 1;

        var maxAngle = 2.0 * PortableMath.Acos(1.0 - tolerance / arc.Radius);
        return System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(LegacySweep(arc)) / maxAngle));
    }

    private static double LegacySweep(Arc arc)
    {
        var startAngle = arc.StartAngle;
        var endAngle = arc.EndAngle;

        if (arc.IsReversed)
            (startAngle, endAngle) = (endAngle, startAngle);

        if (startAngle > endAngle)
            startAngle -= Angle.TwoPI;

        return endAngle - startAngle;
    }

    /// <summary>Arc.ToPoints as it was before its arithmetic moved to a shared helper.</summary>
    private static List<Vector> LegacyArcPoints(Arc arc, int segments, bool circumscribe)
    {
        var points = new List<Vector>();
        var stepAngle = arc.IsReversed ? -LegacySweep(arc) / segments : LegacySweep(arc) / segments;

        var r =
            circumscribe && segments > 0
                ? arc.Radius / PortableMath.Cos(System.Math.Abs(stepAngle) / 2.0)
                : arc.Radius;

        for (var i = 0; i <= segments; ++i)
        {
            var angle = stepAngle * i + arc.StartAngle;
            points.Add(new Vector(PortableMath.Cos(angle) * r + arc.Center.X, PortableMath.Sin(angle) * r + arc.Center.Y));
        }

        return points;
    }

    private static List<Vector> MakePoints(Random random, int index)
    {
        var count = index % 7 == 0 ? random.Next(4) : 3 + random.Next(300);
        var points = new List<Vector>(count);

        for (var i = 0; i < count; i++)
        {
            switch (index % 5)
            {
                // Scattered.
                case 0:
                    points.Add(Point(random));
                    break;

                // A coarse grid: many equal X values, equal points and collinear runs.
                case 1:
                    points.Add(new Vector(random.Next(6), random.Next(6)));
                    break;

                // Two columns, so the sort is decided by Y almost everywhere.
                case 2:
                    points.Add(new Vector(random.Next(2) * 3.5, System.Math.Round(random.NextDouble() * 4, 1)));
                    break;

                // On a circle, where nearly every point is on the hull.
                case 3:
                    var angle = random.NextDouble() * Angle.TwoPI;
                    points.Add(new Vector(5 * System.Math.Cos(angle), 5 * System.Math.Sin(angle)));
                    break;

                // Signed zeros and repeats of one point.
                default:
                    points.Add(random.Next(3) == 0
                        ? new Vector(random.Next(2) == 0 ? 0.0 : -0.0, random.Next(2) == 0 ? 0.0 : -0.0)
                        : new Vector(random.Next(3) - 1, random.Next(3) - 1));
                    break;
            }
        }

        return points;
    }

    private static Vector Point(Random random) =>
        new(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10);

    private static long[] Bits(IEnumerable<Vector> points) =>
        points.SelectMany(p => new[] { p.X, p.Y }).Select(BitConverter.DoubleToInt64Bits).ToArray();
}

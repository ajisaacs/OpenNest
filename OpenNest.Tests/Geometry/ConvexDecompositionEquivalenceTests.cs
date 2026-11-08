using OpenNest.Geometry;
using static OpenNest.Tests.Geometry.NoFitPolygonTests;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// <see cref="ConvexDecomposition.Triangulate"/> skips repeating ear tests whose verdict cannot
/// have changed. These tests compare it bit for bit against the frozen pre-change
/// <see cref="LegacyConvexDecomposition"/> (commit a9fc9f0a): same triangles, same order.
/// </summary>
public class ConvexDecompositionEquivalenceTests
{
    [Fact]
    public void SeededPolygons_MatchLegacyBitwise()
    {
        var random = new Random(7112026);
        var triangles = 0;

        for (var i = 0; i < 3000; i++)
        {
            var polygon = MakePolygon(random, i);
            var expected = LegacyConvexDecomposition.Triangulate(polygon);
            var actual = ConvexDecomposition.Triangulate(polygon);

            Assert.True(
                TriangleBits(expected).SequenceEqual(TriangleBits(actual)),
                $"polygon {i} (kind {i % 8}, {polygon.Vertices.Count} vertices)"
            );
            triangles += expected.Count;
        }

        Assert.True(triangles > 50000);
    }

    /// <summary>
    /// Small stars on a coarse grid. Here a vertex's triangle often holds a vertex that is cut
    /// as an ear later while the vertex keeps its neighbors, the case the remembered verdict
    /// must notice.
    /// </summary>
    [Fact]
    public void SmallGridStars_MatchLegacyBitwise()
    {
        var random = new Random(1);

        for (var i = 0; i < 20000; i++)
        {
            var count = 5 + random.Next(8);
            var angles = Enumerable.Range(0, count)
                .Select(_ => random.NextDouble() * 2 * System.Math.PI)
                .OrderBy(a => a)
                .ToArray();
            var points = angles
                .Select(a =>
                {
                    var r = 0.2 + random.NextDouble() * 3;
                    return (System.Math.Round(r * System.Math.Cos(a), 1), System.Math.Round(r * System.Math.Sin(a), 1));
                })
                .ToArray();
            var polygon = Ring(points);

            Assert.True(
                TriangleBits(LegacyConvexDecomposition.Triangulate(polygon))
                    .SequenceEqual(TriangleBits(ConvexDecomposition.Triangulate(polygon))),
                $"star {i}"
            );
        }
    }

    [Fact]
    public void DegeneratePolygons_MatchLegacyBitwise()
    {
        var cases = new List<Polygon>
        {
            new Polygon(),
            Ring((0, 0), (1, 0)),
            Ring((0, 0), (1, 0), (2, 0)),
            Ring((0, 0), (1, 0), (2, 0), (3, 0), (1, 1e-12)),
            // Repeated vertices and a spike back along an edge.
            Ring((0, 0), (2, 0), (2, 0), (2, 2), (1, 1), (2, 2), (0, 2)),
            // Self-intersecting bow tie: the scan stops at its safety limit.
            Ring((0, 0), (2, 2), (2, 0), (0, 2)),
            // The vertex at (0.1, -0.6) is blocked by (-0.3, -0.3) until that vertex is cut.
            Ring((1.7, 0.8), (-1.6, -1.3), (-0.3, -0.3), (0.1, -0.6), (1.4, -1.2)),
            // Clockwise input is reversed first.
            Ring((0, 0), (0, 3), (1, 3), (1, 1), (3, 1), (3, 0)),
        };

        foreach (var polygon in cases)
        {
            Assert.Equal(
                TriangleBits(LegacyConvexDecomposition.Triangulate(polygon)),
                TriangleBits(ConvexDecomposition.Triangulate(polygon))
            );
        }
    }

    [Fact]
    public void Triangulate_DoesNotMutateInput()
    {
        var polygon = MakePolygon(new Random(3), 1);
        var before = polygon.Vertices.ToList();

        ConvexDecomposition.Triangulate(polygon);

        Assert.Equal(before, polygon.Vertices);
    }

    /// <summary>
    /// Simple polygons of several families: radial stars with many reflex vertices, combs,
    /// flattened circles (long runs of nearly collinear vertices), collinear edge points,
    /// clockwise copies and far-from-origin offsets.
    /// </summary>
    private static Polygon MakePolygon(Random random, int index)
    {
        var polygon = (index % 8) switch
        {
            0 => Star(random),
            1 => Radial(random, 6 + random.Next(120), 0.2),
            2 => Radial(random, 6 + random.Next(60), 0.9),
            3 => Comb(random),
            4 => Circle(random),
            5 => WithEdgePoints(random, Radial(random, 5 + random.Next(20), 0.6)),
            6 => Reverse(Radial(random, 8 + random.Next(80), 0.5)),
            _ => Square(1 + random.NextDouble()),
        };

        return index % 3 == 0
            ? Move(polygon, random.NextDouble() * 400 - 200, random.NextDouble() * 400 - 200)
            : polygon;
    }

    private static Polygon Radial(Random random, int count, double jitter)
    {
        var points = new (double X, double Y)[count];
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * System.Math.PI * i / count;
            var radius = 1 + random.NextDouble() * jitter * 3;
            points[i] = (radius * System.Math.Cos(angle), radius * System.Math.Sin(angle));
        }

        return Ring(points);
    }

    private static Polygon Comb(Random random)
    {
        var teeth = 2 + random.Next(12);
        var width = 0.2 + random.NextDouble();
        var points = new List<(double X, double Y)> { (0, 0), (teeth * width * 2, 0) };
        for (var t = teeth - 1; t >= 0; t--)
        {
            var x = t * width * 2;
            points.Add((x + width * 2, 1 + random.NextDouble()));
            points.Add((x + width, 1 + random.NextDouble()));
            points.Add((x + width, 0.3));
            points.Add((x, 0.3));
        }

        return Ring(points.ToArray());
    }

    private static Polygon Circle(Random random)
    {
        var shape = new Shape();
        var radius = 0.5 + random.NextDouble() * 3;
        shape.Entities.Add(new Arc(0, 0, radius, 0, System.Math.PI));
        shape.Entities.Add(new Arc(0, 0, radius, System.Math.PI, 2 * System.Math.PI));
        return ClipperBridge.Flatten(shape, 0.001 + random.NextDouble() * 0.05, circumscribe: false);
    }

    private static Polygon WithEdgePoints(Random random, Polygon polygon)
    {
        var points = new List<(double X, double Y)>();
        var vertices = polygon.Vertices;
        var count = vertices.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = vertices[i];
            var b = vertices[i + 1];
            points.Add((a.X, a.Y));
            var extra = random.Next(3);
            for (var k = 1; k <= extra; k++)
            {
                var t = k / (extra + 1.0);
                points.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
        }

        return Ring(points.ToArray());
    }

    private static Polygon Reverse(Polygon polygon)
    {
        var points = polygon.Vertices.Take(polygon.Vertices.Count - 1).Reverse().Select(v => (v.X, v.Y));
        return Ring(points.ToArray());
    }

    private static long[] TriangleBits(List<Polygon> triangles) =>
        triangles
            .SelectMany(t =>
                t.Vertices.SelectMany(v => new[]
                    {
                        BitConverter.DoubleToInt64Bits(v.X),
                        BitConverter.DoubleToInt64Bits(v.Y),
                    })
                    .Append(t.Vertices.Count)
            )
            .Append(triangles.Count)
            .ToArray();
}

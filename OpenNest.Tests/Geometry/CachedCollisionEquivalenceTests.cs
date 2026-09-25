using System.Diagnostics;
using OpenNest.Geometry;
using Xunit.Abstractions;
using static OpenNest.Tests.Geometry.NoFitPolygonTests;

namespace OpenNest.Tests.Geometry;

public class CachedCollisionEquivalenceTests
{
    private readonly ITestOutputHelper output;

    public CachedCollisionEquivalenceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SeededPairsMatchReference()
    {
        var random = new Random(73862026);
        var decisions = 0;
        var mismatches = 0;
        var fallback = 0;
        var falseClear = 0;
        var clear = 0;
        var overlaps = 0;
        var watch = Stopwatch.StartNew();
        for (var pair = 0; pair < 200; pair++)
        {
            var a = Make(random, pair % 5);
            var b = Make(random, (pair / 5) % 5);
            var holesA = pair % 4 == 0 ? new List<Polygon> { Move(Square(0.7), 0.2, 0.2) } : null;
            var holesB = pair % 7 == 0 ? new List<Polygon> { Move(Square(0.6), 0.3, 0.3) } : null;
            var ta = TriangulatedRegion.Build(a, holesA);
            var tb = TriangulatedRegion.Build(b, holesB);
            var ga = EdgeGridPolygon.From(a);
            var gb = EdgeGridPolygon.From(b);
            Assert.NotNull(ta);
            Assert.NotNull(tb);
            Assert.NotNull(ga);
            Assert.NotNull(gb);
            for (var sample = 0; sample < 500; sample++)
            {
                var ax = random.NextDouble() * 200 - 100;
                var ay = random.NextDouble() * 200 - 100;
                var bx = ax + random.NextDouble() * 12 - 6;
                var by = ay + random.NextDouble() * 12 - 6;
                if (sample % 4 == 0)
                {
                    // Exact, near-touching, and thin positive-area contacts at both box edges.
                    var gap = new[] { 0, -1e-7, 1e-7, -1e-5, 1e-5, -1e-4, 1e-4 }[(sample / 4) % 7];
                    bx = ax + a.BoundingBox.Right - b.BoundingBox.Left + gap;
                    by = ay + a.BoundingBox.Bottom - b.BoundingBox.Bottom;
                }
                var reference = Collision.HasOverlap(Move(a, ax, ay), Move(b, bx, by),
                    holesA?.Select(h => Move(h, ax, ay)).ToList(),
                    holesB?.Select(h => Move(h, bx, by)).ToList());
                var cached = ta.Overlaps(tb, ax, ay, bx, by);
                var relation = EdgeGridPolygon.Relate(ga.Translated(ax, ay), gb.Translated(bx, by));
                if (!cached.HasValue)
                    fallback++;
                else if (cached.Value != reference)
                {
                    if (mismatches < 5)
                        output.WriteLine($"Mismatch pair={pair} sample={sample} a=({ax:R},{ay:R}) b=({bx:R},{by:R}) expected={reference}");
                    mismatches++;
                }
                if (relation == ShellRelation.Clear)
                {
                    clear++;
                    if (reference)
                        falseClear++;
                }
                if (reference)
                    overlaps++;
                decisions++;
            }
        }
        output.WriteLine($"Decisions={decisions}; mismatches={mismatches}; false Clear={falseClear}; "
            + $"fallback={fallback} ({100.0 * fallback / decisions:F4}%); Clear={clear}; overlaps={overlaps}; elapsed={watch.Elapsed.TotalSeconds:F3}s");
        Assert.Equal(100000, decisions);
        Assert.Equal(0, mismatches);
        Assert.Equal(0, falseClear);
        Assert.True(overlaps > 10000);
    }

    [Fact]
    public void HoleContainmentAndSharedGridQueries()
    {
        var a = Square(10);
        var b = Square(1);
        var ta = TriangulatedRegion.Build(a, new[] { Move(Square(6), 2, 2) });
        var tb = TriangulatedRegion.Build(b);
        Assert.NotNull(ta);
        Assert.NotNull(tb);
        Assert.False(ta.Overlaps(tb, 0, 0, 4, 4));
        Assert.True(ta.Overlaps(tb, 0, 0, 1.5, 4));
        var ga = EdgeGridPolygon.From(a)!;
        var gb = EdgeGridPolygon.From(b)!;
        Parallel.For(0, 1000, i =>
        {
            var dx = i % 2 == 0 ? 4 : 12;
            Assert.Equal(dx == 4 ? ShellRelation.Unknown : ShellRelation.Clear,
                EdgeGridPolygon.Relate(ga, gb.Translated(dx, 4)));
        });
    }

    [Fact]
    public void EmptyRingsRequestFallback()
    {
        Assert.Null(TriangulatedRegion.Build(new Polygon()));
        Assert.Null(EdgeGridPolygon.From(new Polygon()));
    }

    private static Polygon Make(Random random, int kind)
    {
        var size = 2 + random.NextDouble() * 2;
        switch (kind)
        {
            case 0:
                return Square(size);
            case 1:
                return Star(random);
            case 2:
                return Ring((0, 0), (size, 0), (size, 1), (1, 1), (1, size), (0, size));
            case 3:
                return Ring((0, 0), (size, 0), (0, size));
            default:
                var shape = new Shape();
                shape.Entities.Add(new Arc(0, 0, size / 2, 0, System.Math.PI));
                shape.Entities.Add(new Arc(0, 0, size / 2, System.Math.PI, 2 * System.Math.PI));
                return ClipperBridge.Flatten(shape, 0.08, circumscribe: false);
        }
    }
}

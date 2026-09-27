using OpenNest.Geometry;
using Xunit.Abstractions;
using static OpenNest.Tests.Geometry.NoFitPolygonTests;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// The overlap-only <see cref="Collision.HasOverlap"/> path skips crossing points. These tests
/// compare it and <see cref="Collision.Check"/> against the frozen pre-change
/// <see cref="LegacyCollision"/> (commit 82feb78).
/// </summary>
[Collection(nameof(OpenNest.Tests.BestFit.FillCacheCollection))]
public class CollisionOverlapOnlyTests
{
    private readonly ITestOutputHelper output;

    public CollisionOverlapOnlyTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SeededHasOverlap_MatchesLegacyVerdicts()
    {
        var random = new Random(27092026);
        var decisions = 0;
        var overlaps = 0;
        var boxOverlapClear = 0;
        var centerAligned = 0;
        for (var pair = 0; pair < 200; pair++)
        {
            var a = Make(random, pair % 6);
            var b = Make(random, (pair / 6) % 6);
            var holesA = pair % 4 == 0 ? new List<Polygon> { Move(Square(0.7), 0.2, 0.2) } : null;
            var holesB = pair % 7 == 0 ? new List<Polygon> { Move(Square(0.6), 0.3, 0.3) } : null;
            for (var sample = 0; sample < 250; sample++)
            {
                var (ax, ay, bx, by) = Offsets(random, a, b, sample);
                var ma = Move(a, ax, ay);
                var mb = Move(b, bx, by);
                var ha = holesA?.Select(h => Move(h, ax, ay)).ToList();
                var hb = holesB?.Select(h => Move(h, bx, by)).ToList();
                var legacyCheck = LegacyCollision.Check(ma, mb, ha, hb).Overlaps;
                var legacyHas = LegacyCollision.HasOverlap(ma, mb, ha, hb);
                var actual = Collision.HasOverlap(ma, mb, ha, hb);
                Assert.True(legacyCheck == legacyHas, $"legacy self-disagreement pair={pair} sample={sample}");
                Assert.True(actual == legacyHas,
                    $"pair={pair} sample={sample} a=({ax:R},{ay:R}) b=({bx:R},{by:R}) expected={legacyHas}");
                Assert.Equal(legacyHas, Collision.Check(ma, mb, ha, hb).Overlaps);
                if (legacyHas)
                    overlaps++;
                else if (BoxesOverlap(ma.BoundingBox, mb.BoundingBox))
                    boxOverlapClear++;
                if (sample % 10 == 9)
                    centerAligned++;
                decisions++;
            }
        }
        output.WriteLine($"decisions={decisions}; overlaps={overlaps}; bbox-overlap but clear={boxOverlapClear}; center-aligned samples={centerAligned}");
        Assert.Equal(50000, decisions);
        Assert.True(overlaps > 5000);
        Assert.True(boxOverlapClear > 5000);
    }

    /// <summary>
    /// Supplied triangulations, each prepared once and reused across many pairs as
    /// PartOverlapChecker does, give the same verdicts as the legacy per-call path and are
    /// never mutated by clipping or hole subtraction.
    /// </summary>
    [Fact]
    public void SeededPreparedTriangles_ReusedAcrossPairs_MatchLegacyVerdicts()
    {
        var random = new Random(28092026);
        var decisions = 0;
        var overlaps = 0;
        var boxOverlapClear = 0;
        for (var group = 0; group < 40; group++)
        {
            var polygons = new List<Polygon>();
            var shapeA = Make(random, group % 6);
            var shapeB = Make(random, (group / 6) % 6);
            for (var sample = 0; sample < 25; sample++)
            {
                // Offsets gives contact/containment placements within each (A, B) pair. Pulling
                // every pair into one ~8-unit window makes most cross-pair tests reach clipping
                // too, so each cached triangulation is reused against many near neighbours.
                var (ax, ay, bx, by) = Offsets(random, shapeA, shapeB, sample);
                var dx = random.NextDouble() * 4 - ax;
                var dy = random.NextDouble() * 4 - ay;
                polygons.Add(Move(shapeA, ax + dx, ay + dy));
                polygons.Add(Move(shapeB, bx + dx, by + dy));
            }
            var holes = group % 4 == 0
                ? polygons.Select(p => new List<Polygon> { Move(Square(0.5), p.BoundingBox.Left + 0.2, p.BoundingBox.Bottom + 0.2) }).ToList()
                : null;
            var triangles = polygons.Select(p => new Lazy<List<Polygon>>(() => Collision.Triangulate(p))).ToArray();
            for (var i = 0; i < polygons.Count; i++)
                for (var j = i + 1; j < polygons.Count; j++)
                {
                    var expected = LegacyCollision.HasOverlap(polygons[i], polygons[j], holes?[i], holes?[j]);
                    var ti = triangles[i];
                    var tj = triangles[j];
                    var actual = Collision.HasOverlap(polygons[i], () => ti.Value, polygons[j], () => tj.Value, holes?[i], holes?[j]);
                    Assert.True(expected == actual, $"group={group} i={i} j={j} expected={expected}");
                    decisions++;
                    if (expected)
                        overlaps++;
                    else if (BoxesOverlap(polygons[i].BoundingBox, polygons[j].BoundingBox))
                        boxOverlapClear++;
                }
            for (var i = 0; i < polygons.Count; i++)
            {
                if (!triangles[i].IsValueCreated)
                    continue;
                // Reused triangles must still equal a fresh triangulation, bit for bit.
                Assert.Equal(TriangleBits(Collision.Triangulate(polygons[i])), TriangleBits(triangles[i].Value));
            }
        }
        output.WriteLine($"prepared-triangle decisions={decisions}; overlaps={overlaps}; bbox-overlap but clear={boxOverlapClear}");
        Assert.Equal(40 * 50 * 49 / 2, decisions);
        Assert.True(overlaps > 10000);
        Assert.True(boxOverlapClear > 2000);
    }

    [Fact]
    public void PreparedTriangles_AreNotResolvedWhenBoundingBoxesMiss()
    {
        var a = Square(2);
        var b = Move(Square(2), 10, 10);
        Assert.False(Collision.HasOverlap(a, () => throw new InvalidOperationException("A"), b,
            () => throw new InvalidOperationException("B")));
        var c = Move(Square(2), 1, 1);
        var resolved = new List<string>();
        Assert.True(Collision.HasOverlap(a, () => { resolved.Add("a"); return Collision.Triangulate(a); }, c,
            () => { resolved.Add("c"); return Collision.Triangulate(c); }));
        Assert.Equal(new[] { "a", "c" }, resolved);
    }

    private static long[] TriangleBits(List<Polygon> triangles) => triangles
        .SelectMany(t => new[] { t.BoundingBox.X, t.BoundingBox.Y, t.BoundingBox.Length, t.BoundingBox.Width }
            .Concat(t.Vertices.SelectMany(v => new[] { v.X, v.Y })))
        .Select(BitConverter.DoubleToInt64Bits)
        .Prepend(triangles.Count)
        .ToArray();

    [Fact]
    public void SeededCheck_MatchesLegacyBitwise()
    {
        var random = new Random(9272026);
        var compared = 0;
        for (var pair = 0; pair < 60; pair++)
        {
            var a = Make(random, pair % 6);
            var b = Make(random, (pair / 6) % 6);
            var holesA = pair % 3 == 0 ? new List<Polygon> { Move(Square(0.7), 0.2, 0.2) } : null;
            for (var sample = 0; sample < 40; sample++)
            {
                var (ax, ay, bx, by) = Offsets(random, a, b, sample);
                var ma = Move(a, ax, ay);
                var mb = Move(b, bx, by);
                var ha = holesA?.Select(h => Move(h, ax, ay)).ToList();
                var expected = LegacyCollision.Check(ma, mb, ha);
                var actual = Collision.Check(ma, mb, ha);
                Assert.Equal(expected.Overlaps, actual.Overlaps);
                Assert.Equal(Bits(expected.OverlapArea), Bits(actual.OverlapArea));
                Assert.Equal(expected.IntersectionPoints.SelectMany(VectorBits), actual.IntersectionPoints.SelectMany(VectorBits));
                Assert.Equal(expected.OverlapRegions.Count, actual.OverlapRegions.Count);
                for (var r = 0; r < expected.OverlapRegions.Count; r++)
                {
                    Assert.Equal(expected.OverlapRegions[r].Vertices.SelectMany(VectorBits),
                        actual.OverlapRegions[r].Vertices.SelectMany(VectorBits));
                    Assert.Equal(BoxBits(expected.OverlapRegions[r].BoundingBox), BoxBits(actual.OverlapRegions[r].BoundingBox));
                }
                compared++;
            }
        }
        Assert.Equal(2400, compared);
    }

    [Theory]
    [InlineData(0.0, 0.0, true)] // identical squares
    [InlineData(4.0, 4.0, true)] // b entirely inside a: no edge crossings
    [InlineData(10.0, 0.0, false)] // exact edge contact
    [InlineData(10.0000001, 0.0, false)] // separated by less than Epsilon
    [InlineData(9.99999, 0.0, false)] // overlap below the area floor
    [InlineData(9.9, 0.0, true)] // thin positive overlap
    public void Containment_Contact_AndThinOverlap_MatchLegacy(double bx, double by, bool expected)
    {
        var a = Square(10);
        var b = Move(bx == 0 && by == 0 ? Square(10) : Square(bx == 4 ? 1 : 10), bx, by);
        Assert.Equal(expected, LegacyCollision.HasOverlap(a, b));
        Assert.Equal(expected, Collision.HasOverlap(a, b));
        Assert.Equal(expected, Collision.Check(a, b).Overlaps);
    }

    [Fact]
    public void HoleContainment_MatchesLegacy()
    {
        var outer = Square(10);
        var holes = new List<Polygon> { Move(Square(6), 2, 2) };
        foreach (var (x, y, expected) in new[] { (4.0, 4.0, false), (1.5, 4.0, true), (2.0, 2.0, false), (1.9, 2.0, true) })
        {
            var inner = Move(Square(1), x, y);
            Assert.Equal(expected, LegacyCollision.HasOverlap(outer, inner, holes));
            Assert.Equal(expected, Collision.HasOverlap(outer, inner, holes));
            Assert.Equal(expected, Collision.HasOverlap(inner, outer, null, holes));
        }
    }

    [Fact]
    public void HasOverlap_DoesNotMutateInputs()
    {
        var a = Make(new Random(5), 1);
        var b = Move(Make(new Random(6), 2), 0.5, 0.5);
        var holes = new List<Polygon> { Move(Square(0.7), 0.2, 0.2) };
        var before = Snapshot(a, b, holes[0]);
        Collision.HasOverlap(a, b, holes, null);
        Collision.HasOverlap(b, a, null, holes);
        Assert.Equal(before, Snapshot(a, b, holes[0]));
    }

    /// <summary>
    /// Malformed input (a null outer vertex list after bounds were cached) must still fail loudly
    /// once the bounding boxes overlap, rather than reporting "no overlap". The exception type
    /// is intentionally not pinned: skipping crossing points moves the first dereference from
    /// Polygon.ToLines (NullReferenceException) to triangulation (ArgumentNullException).
    /// Bounding-box rejection still returns false without touching the vertices.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullOuterVertices_StillFailLoudly_UnlessBoxesAreSeparate(bool nullFirst)
    {
        var random = new Random(5);
        var a = Make(random, 0);
        var b = Move(Make(random, 0), 0.5, 0.5);
        (nullFirst ? a : b).Vertices = null;

        Assert.ThrowsAny<Exception>(() => LegacyCollision.Check(a, b));
        Assert.ThrowsAny<Exception>(() => Collision.HasOverlap(a, b));
        Assert.ThrowsAny<Exception>(() => Collision.Check(a, b));
        Assert.ThrowsAny<Exception>(() => Collision.HasOverlap(a, () => Collision.Triangulate(a), b,
            () => Collision.Triangulate(b)));

        var far = Move(Make(new Random(5), 0), 100, 100);
        var broken = nullFirst ? a : b;
        Assert.False(LegacyCollision.Check(broken, far).Overlaps);
        Assert.False(Collision.HasOverlap(broken, far));
        var resolved = 0;
        Assert.False(Collision.HasOverlap(broken, () => { resolved++; return Collision.Triangulate(broken); }, far,
            () => { resolved++; return Collision.Triangulate(far); }));
        Assert.Equal(0, resolved);
    }

#if DEBUG
    [Fact]
    public void Work_HasOverlapSkipsCrossingPointScans()
    {
        var random = new Random(11);
        var a = Make(random, 1);
        var b = Move(Make(random, 2), 0.25, 0.25);
        PerfCounters.Reset();
        try
        {
            for (var i = 0; i < 5; i++)
                Collision.HasOverlap(a, b);
            var overlapOnly = PerfCounters.CrossingPointScans;
            PerfCounters.Reset();
            for (var i = 0; i < 5; i++)
                Collision.Check(a, b);
            var full = PerfCounters.CrossingPointScans;
            output.WriteLine($"crossing-point scans: HasOverlap={overlapOnly}; Check={full}");
            Assert.Equal(0, overlapOnly);
            Assert.Equal(5, full);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    internal static Polygon Make(Random random, int kind)
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
            case 4:
                // Concave comb: several notches that interlock with a translated copy.
                return Ring((0, 0), (size, 0), (size, 1), (size * 0.75, 1), (size * 0.75, 0.4),
                    (size * 0.5, 0.4), (size * 0.5, 1), (size * 0.25, 1), (size * 0.25, 0.4), (0, 0.4));
            default:
                var shape = new Shape();
                shape.Entities.Add(new Arc(0, 0, size / 2, 0, System.Math.PI));
                shape.Entities.Add(new Arc(0, 0, size / 2, System.Math.PI, 2 * System.Math.PI));
                return ClipperBridge.Flatten(shape, 0.08, circumscribe: false);
        }
    }

    private static (double, double, double, double) Offsets(Random random, Polygon a, Polygon b, int sample)
    {
        var ax = random.NextDouble() * 200 - 100;
        var ay = random.NextDouble() * 200 - 100;
        var bx = ax + random.NextDouble() * 8 - 4;
        var by = ay + random.NextDouble() * 8 - 4;
        if (sample % 5 == 0)
        {
            // Exact, near-touching and thin positive-area contacts at a box edge.
            var gap = new[] { 0, -1e-7, 1e-7, -1e-5, 1e-5, -1e-4, 1e-4 }[(sample / 5) % 7];
            bx = ax + a.BoundingBox.Right - b.BoundingBox.Left + gap;
            by = ay + a.BoundingBox.Bottom - b.BoundingBox.Bottom;
        }
        else if (sample % 10 == 9)
        {
            // Box centers coincide: probes containment and deep overlap (not verified per sample).
            bx = ax + a.BoundingBox.Center.X - b.BoundingBox.Center.X;
            by = ay + a.BoundingBox.Center.Y - b.BoundingBox.Center.Y;
        }
        return (ax, ay, bx, by);
    }

    private static bool BoxesOverlap(Box a, Box b) =>
        System.Math.Min(a.Right, b.Right) - System.Math.Max(a.Left, b.Left) > OpenNest.Math.Tolerance.Epsilon
        && System.Math.Min(a.Top, b.Top) - System.Math.Max(a.Bottom, b.Bottom) > OpenNest.Math.Tolerance.Epsilon;

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static long[] VectorBits(Vector v) => new[] { Bits(v.X), Bits(v.Y) };

    private static long[] BoxBits(Box box) => new[] { Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width) };

    private static long[] Snapshot(params Polygon[] polygons) =>
        polygons.SelectMany(p => p.Vertices.SelectMany(VectorBits).Concat(BoxBits(p.BoundingBox))
            .Append(p.Vertices.Count)).ToArray();
}

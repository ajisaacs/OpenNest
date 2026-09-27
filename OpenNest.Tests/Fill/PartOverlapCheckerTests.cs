using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

/// <summary>
/// PartOverlapChecker, and both in-fill HasOverlappingParts checks built on it, must reproduce
/// the frozen pre-change nested loop exactly: the same verdict and the same first overlapping
/// (a, b) indices. The only change is that each distinct Program is prepared once per check.
/// </summary>
[Collection(nameof(FillCacheCollection))]
public class PartOverlapCheckerTests
{
    private static readonly string[] Shapes = { "rectangle", "concave", "arc", "circle", "ring" };

    private readonly ITestOutputHelper output;

    public PartOverlapCheckerTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> GridCases()
    {
        foreach (var shape in Shapes)
            foreach (var spacing in new[] { 0.0, 0.5 })
                foreach (var angle in new[] { 0.0, System.Math.PI / 2, 0.37 })
                    yield return new object[] { shape, spacing, angle };
    }

    [Theory]
    [MemberData(nameof(GridCases))]
    public void FillLinearGrids_MatchFrozenLoops(string shape, double spacing, double angle)
    {
        var area = new Box(3.1, 5.3, 61, 37);
        var filler = new FillLinear(area, spacing);
        var drawing = Fixture(shape);
        foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
        {
            var grid = filler.Fill(drawing, angle, direction);
            Assert.NotEmpty(grid);
            AssertMatchesLegacy(grid);
            // Nudge copies onto their neighbours so overlapping verdicts are exercised too.
            foreach (var shift in new[] { 0.05, 1e-9, -1e-9 })
                AssertMatchesLegacy(Shifted(grid, shift));
        }
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("rotated")]
    [InlineData("built-pair")]
    public void PatternGrids_MatchFrozenLoops(string kind)
    {
        foreach (var shape in Shapes)
        {
            var pattern = MakePattern(kind, Fixture(shape));
            var filler = new FillLinear(new Box(3.1, 5.3, 61, 37), 0.5);
            foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
            {
                var grid = filler.Fill(pattern, direction);
                AssertMatchesLegacy(grid);
                AssertMatchesLegacy(Shifted(grid, 0.05));
            }
        }
    }

    [Fact]
    public void OverlappingSeedTiling_MatchesFrozenLoops()
    {
        var first = new Part(Fixture("rectangle"));
        var pattern = new Pattern();
        pattern.Parts.AddRange(new[] { first, first.CloneAtOffset(new Vector(0.25, 0.25)) });
        pattern.UpdateBounds();
        var legacy = new LegacyFillLinear(new Box(0, 0, 35, 8.25), 0.5);
        var raw = new List<Part>(pattern.Parts);
        raw.AddRange((List<Part>)FillExtentsTests.Invoke(legacy, "TilePattern", pattern, NestDirection.Horizontal));
        Assert.True(LegacyPartOverlap.FillHelpersHasOverlappingParts(raw));
        AssertMatchesLegacy(raw);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1e-12)]
    [InlineData(1e-9)]
    [InlineData(-1e-9)]
    [InlineData(1e-7)]
    [InlineData(-1e-7)]
    public void TouchingAndEpsilonEdgeOffsets_MatchFrozenLoops(double gap)
    {
        foreach (var shape in Shapes)
        {
            var drawing = Fixture(shape);
            var a = Part.CreateAtOrigin(drawing, 0);
            var right = a.CloneAtOffset(new Vector(a.BoundingBox.Length + gap, 0));
            var above = a.CloneAtOffset(new Vector(0, a.BoundingBox.Width + gap));
            var diagonal = a.CloneAtOffset(new Vector(a.BoundingBox.Length + gap, a.BoundingBox.Width + gap));
            AssertMatchesLegacy(new List<Part> { a, right, above, diagonal });
        }
    }

    [Fact]
    public void NonMaterialAndEmptyPrograms_MatchFrozenLoops()
    {
        var square = Part.CreateAtOrigin(Fixture("rectangle"), 0);
        var scribeOnly = new Program();
        scribeOnly.Codes.Add(new RapidMove(new Vector(0, 0)));
        scribeOnly.Codes.Add(new LinearMove(new Vector(4, 0)) { Layer = LayerType.Scribe });
        scribeOnly.Codes.Add(new LinearMove(new Vector(4, 4)) { Layer = LayerType.Scribe });
        scribeOnly.Codes.Add(new LinearMove(new Vector(0, 0)) { Layer = LayerType.Scribe });
        var rapidOnly = new Program();
        rapidOnly.Codes.Add(new RapidMove(new Vector(0, 0)));
        rapidOnly.Codes.Add(new RapidMove(new Vector(5, 5)));
        foreach (var program in new[] { scribeOnly, rapidOnly })
        {
            var odd = new Part(new Drawing("odd", program));
            Assert.Empty(Part.MaterialEntities(odd.Program));
            var parts = new List<Part> { square, odd, square.CloneAtOffset(new Vector(2, 2)) };
            AssertMatchesLegacy(parts);
            Assert.False(new PartOverlapChecker().Overlaps(square, odd));
            Assert.False(new PartOverlapChecker().Overlaps(odd, odd));
        }

        // An empty program has no material entities, so both paths return false before ShapeProfile.
        var empty = new Part(new Drawing("empty", new Program()));
        Assert.Empty(Part.MaterialEntities(empty.Program));
        Assert.Equal(Capture(() => LegacyPartOverlap.Intersects(square, empty, out _)),
            Capture(() => new PartOverlapChecker().Overlaps(square, empty)));
        Assert.Equal(Capture(() => LegacyPartOverlap.Intersects(empty, square, out _)),
            Capture(() => new PartOverlapChecker().Overlaps(empty, square)));
        AssertMatchesLegacy(new List<Part> { square, empty, square.CloneAtOffset(new Vector(20, 0)) });
    }

    [Fact]
    public void SharedProgram_PartsTestedAgainstEachOtherAndItself()
    {
        var a = Part.CreateAtOrigin(Fixture("concave"), 0);
        var b = a.CloneAtOffset(new Vector(1, 1));
        var c = a.CloneAtOffset(new Vector(30, 0));
        Assert.Same(a.Program, b.Program);
        var checker = new PartOverlapChecker();
        Assert.Equal(LegacyPartOverlap.Intersects(a, b, out _), checker.Overlaps(a, b));
        Assert.Equal(LegacyPartOverlap.Intersects(a, c, out _), checker.Overlaps(a, c));
        Assert.Equal(LegacyPartOverlap.Intersects(b, a, out _), checker.Overlaps(b, a));
        Assert.Equal(LegacyPartOverlap.Intersects(a, a, out _), checker.Overlaps(a, a));
        Assert.True(checker.Overlaps(a, b));
        Assert.False(checker.Overlaps(a, c));
    }

    [Fact]
    public void WorldPolygons_MatchPreChangeBitwise()
    {
        foreach (var shape in Shapes)
        {
            var part = Part.CreateAtOrigin(Fixture(shape), 0.37);
            part.Offset(new Vector(11.1, -3.3));
            var clone = part.CloneAtOffset(new Vector(7.77, 1.234567));
            var checker = new PartOverlapChecker();
            // Prepare the shared Program through the clone first, then build the original.
            checker.Overlaps(clone, part);
            foreach (var p in new[] { part, clone })
            {
                var expected = LegacyPartOverlap.WorldPolygon(p);
                var actual = WorldPolygon(checker, p);
                Assert.Equal(PolygonBits(expected), PolygonBits(actual));
            }
        }
    }

    [Fact]
    public void Check_DoesNotMutateParts()
    {
        var pattern = MakePattern("rotated", Fixture("arc"));
        var grid = new FillLinear(new Box(3.1, 5.3, 61, 37), 0.5).Fill(pattern, NestDirection.Horizontal);
        var before = grid.Select(Snapshot).ToArray();
        FillHelpers.HasOverlappingParts(grid);
        Invoke(grid, out _, out _);
        var checker = new PartOverlapChecker();
        for (var i = 0; i + 1 < grid.Count; i++)
            checker.Overlaps(grid[i], grid[i + 1]);
        Assert.Equal(before, grid.Select(Snapshot).ToArray());
    }

    [Fact]
    public void ConcurrentChecks_MatchSequential()
    {
        var grids = Shapes.SelectMany(shape => new[]
        {
            Shifted(new FillLinear(new Box(3.1, 5.3, 61, 37), 0.5).Fill(Fixture(shape), 0.37, NestDirection.Horizontal), 0.05),
            new FillLinear(new Box(3.1, 5.3, 61, 37), 0.5).Fill(MakePattern("built-pair", Fixture(shape)), NestDirection.Vertical),
        }).ToArray();
        var expected = grids.Select(g => (Helpers: FillHelpers.HasOverlappingParts(g), Linear: Indices(g))).ToArray();
        Parallel.For(0, grids.Length * 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var grid = grids[i % grids.Length];
            Assert.Equal(expected[i % grids.Length].Helpers, FillHelpers.HasOverlappingParts(grid));
            Assert.Equal(expected[i % grids.Length].Linear, Indices(grid));
        });
    }

#if DEBUG
    [Theory]
    [InlineData("single", 1)]
    [InlineData("pair", 2)]
    public void Work_PreparesEachDistinctProgramOncePerCheck(string kind, long distinctPrograms)
    {
        // Rotated grids: neighbouring boxes overlap but parts do not, so every box-overlapping
        // pair reaches an exact test and there is no early exit.
        var filler = new FillLinear(new Box(3.1, 5.3, 96, 48), 0.5);
        List<Part> grid;
        if (kind == "single")
            grid = filler.Fill(Fixture("arc"), 0.37, NestDirection.Horizontal);
        else
        {
            var first = Part.CreateAtOrigin(Fixture("concave"), 0);
            var second = Part.CreateAtOrigin(Fixture("concave"), System.Math.PI);
            second.Offset(new Vector(first.Right + 0.5, first.Bottom));
            grid = filler.Fill(FillHelpers.BuildRotatedPattern(new List<Part> { first, second }, 0.37),
                NestDirection.Horizontal);
        }
        Assert.Equal(distinctPrograms, grid.Select(g => g.Program).Distinct(ReferenceEqualityComparer.Instance).Count());
        PerfCounters.Reset();
        try
        {
            var legacyVerdict = LegacyPartOverlap.FillHelpersHasOverlappingParts(grid);
            Assert.Equal(0, PerfCounters.OverlapPolygonPreparations);
            // Old path: two preparations per exact test, through Part.Intersects.
            var exactTests = 0;
            var legacyPreparations = 0L;
            PerfCounters.Reset();
            ForEachBoxOverlappingPair(grid, (a, b) =>
            {
                exactTests++;
                a.Intersects(b, out _);
            });
            legacyPreparations = PerfCounters.OverlapPolygonPreparations;
            PerfCounters.Reset();
            var verdict = FillHelpers.HasOverlappingParts(grid);
            var preparations = PerfCounters.OverlapPolygonPreparations;
            output.WriteLine($"work {kind}: parts={grid.Count}; exact tests={exactTests}; old preparations={legacyPreparations}; new={preparations}; verdict={verdict}");
            Assert.False(legacyVerdict);
            Assert.False(verdict);
            Assert.True(exactTests > 2);
            Assert.Equal(2L * exactTests, legacyPreparations);
            Assert.Equal(distinctPrograms, preparations);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    private static void AssertMatchesLegacy(List<Part> parts)
    {
        var legacy = Capture(() => LegacyPartOverlap.FillHelpersHasOverlappingParts(parts));
        var actual = Capture(() => FillHelpers.HasOverlappingParts(parts));
        Assert.Equal(legacy, actual);

        var legacyIndices = Capture(() =>
        {
            var hit = LegacyPartOverlap.FillLinearHasOverlappingParts(parts, out var a, out var b);
            return (hit, a, b);
        });
        var actualIndices = Capture(() => Indices(parts));
        Assert.Equal(legacyIndices, actualIndices);
    }

    private static (T Value, Type? Exception) Capture<T>(Func<T> run)
    {
        try
        {
            return (run(), null);
        }
        catch (Exception exception)
        {
            return (default!, (exception as System.Reflection.TargetInvocationException)?.InnerException?.GetType()
                ?? exception.GetType());
        }
    }

    private static (bool Hit, int A, int B) Indices(List<Part> parts)
    {
        var hit = Invoke(parts, out var a, out var b);
        return (hit, a, b);
    }

    private static bool Invoke(List<Part> parts, out int a, out int b)
    {
        var method = typeof(FillLinear).GetMethod("HasOverlappingParts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var args = new object[] { parts, -1, -1 };
        var hit = (bool)method.Invoke(null, args)!;
        a = (int)args[1];
        b = (int)args[2];
        return hit;
    }

    private static void ForEachBoxOverlappingPair(List<Part> parts, Action<Part, Part> action)
    {
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var b1 = parts[i].BoundingBox;
                var b2 = parts[j].BoundingBox;
                var overlapX = System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                var overlapY = System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);
                if (overlapX > OpenNest.Math.Tolerance.Epsilon && overlapY > OpenNest.Math.Tolerance.Epsilon)
                    action(parts[i], parts[j]);
            }
    }

    private static Polygon WorldPolygon(PartOverlapChecker checker, Part part)
    {
        var field = typeof(PartOverlapChecker).GetField("worldPolygons",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((Dictionary<Part, Polygon>)field.GetValue(checker)!)[part];
    }

    /// <summary>Every other part replaced by a copy moved by (-shift, -shift).</summary>
    private static List<Part> Shifted(List<Part> grid, double shift)
    {
        var result = new List<Part>(grid.Count);
        for (var i = 0; i < grid.Count; i++)
            result.Add(i % 2 == 0 ? grid[i] : grid[i].CloneAtOffset(new Vector(-shift, -shift)));
        return result;
    }

    private static Drawing Fixture(string shape) => shape switch
    {
        "circle" => new RingShape { OuterDiameter = 8, InnerDiameter = 0 }.GetDrawing(),
        "ring" => new RingShape { OuterDiameter = 8, InnerDiameter = 3 }.GetDrawing(),
        _ => FillExtentsTests.MakeFixture(shape),
    };

    private static Pattern MakePattern(string kind, Drawing drawing)
    {
        var first = Part.CreateAtOrigin(drawing, 0);
        first.Offset(new Vector(11.1, -3.3));
        var second = kind == "shared" ? first.CloneAtOffset(new Vector(first.BoundingBox.Length + 0.5, 0))
            : Part.CreateAtOrigin(drawing, System.Math.PI / 2);
        if (kind != "shared")
            second.Offset(new Vector(first.Right + 0.5, first.Bottom));
        if (kind == "built-pair")
            return FillHelpers.BuildRotatedPattern(new List<Part> { first, second }, 0.37);
        var pattern = new Pattern();
        pattern.Parts.AddRange(new[] { first, second });
        pattern.UpdateBounds();
        Assert.Equal(kind == "shared", ReferenceEquals(first.Program, second.Program));
        return pattern;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static long[] PolygonBits(Polygon polygon) => new[]
        {
            Bits(polygon.BoundingBox.X), Bits(polygon.BoundingBox.Y),
            Bits(polygon.BoundingBox.Length), Bits(polygon.BoundingBox.Width),
        }
        .Concat(polygon.Vertices.SelectMany(v => new[] { Bits(v.X), Bits(v.Y) }))
        .ToArray();

    private static object[] Snapshot(Part part) => new object[]
        {
            part, part.Program, part.Program.Codes.Count, Bits(part.Location.X), Bits(part.Location.Y),
            Bits(part.Rotation), Bits(part.BoundingBox.X), Bits(part.BoundingBox.Y),
            Bits(part.BoundingBox.Length), Bits(part.BoundingBox.Width),
        }
        .Concat(part.Program.Codes.Cast<object>())
        .Concat(ConvertProgram.ToGeometry(part.Program).SelectMany(e => new object[] { e.GetType(), e.Layer,
            Bits(e.BoundingBox.X), Bits(e.BoundingBox.Y), Bits(e.BoundingBox.Length), Bits(e.BoundingBox.Width) }))
        .ToArray();
}

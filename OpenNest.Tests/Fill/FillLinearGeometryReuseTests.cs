using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

[Collection(nameof(FillCacheCollection))]
public class FillLinearGeometryReuseTests
{
    private readonly ITestOutputHelper output;

    public FillLinearGeometryReuseTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var shape in new[] { "rectangle", "concave", "arc", "circle", "ring" })
            foreach (var spacing in new[] { 0.0, 0.5 })
                foreach (var angle in new[] { 0.0, System.Math.PI / 2, System.Math.PI, 0.37 })
                    foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                        yield return new object[] { shape, spacing, angle, direction };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DrawingAndRow_MatchFrozenLegacy(string shape, double spacing, double angle, NestDirection direction)
    {
        var drawing = Fixture(shape);
        var before = ProgramValues(drawing.Program);
        var area = new Box(3.1, -5.3, 42, 29);
        var filler = new FillLinear(area, spacing);
        var legacy = new LegacyFillLinear(area, spacing);
        var inputs = new[] { drawing.Program };
        var expected = legacy.Fill(drawing, angle, direction);
        Assert.NotEmpty(expected);
        AssertLayout(expected, filler.Fill(drawing, angle, direction), inputs);
        AssertLayout(expected, filler.Fill(drawing, angle, direction), inputs);
        var expectedRow = legacy.FillRow(drawing, angle, direction);
        var actualRow = filler.FillRow(drawing, angle, direction);
        AssertLayout(expectedRow.Parts, actualRow.Parts, inputs);
        Assert.Equal(BoxBits(expectedRow.BoundingBox), BoxBits(actualRow.BoundingBox));
        Assert.Equal(before, ProgramValues(drawing.Program));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("rotated")]
    [InlineData("built-pair")]
    public void Pattern_MatchesFrozenLegacy_IdentityAndInputOwnership(string kind)
    {
        foreach (var spacing in new[] { 0.0, 0.5 })
            foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
            {
                var pattern = MakePattern(kind);
                var before = Snapshot(pattern);
                var filler = new FillLinear(new Box(-7.1, 11.3, 52, 39), spacing);
                var legacy = new LegacyFillLinear(filler.WorkArea, spacing);
                var inputs = pattern.Parts.Select(p => p.Program).ToArray();
                var expected = legacy.Fill(pattern, direction);
                Assert.NotEmpty(expected);
                AssertLayout(expected, filler.Fill(pattern, direction), inputs);
                AssertLayout(expected, filler.Fill(pattern, direction), inputs);
                Assert.Equal(before, Snapshot(pattern));
            }
    }

    [Theory]
    [InlineData(NestDirection.Horizontal)]
    [InlineData(NestDirection.Vertical)]
    public void BoundaryFit_MatchesBothSidesOfLastCopyThreshold(NestDirection direction)
    {
        var drawing = Fixture("rectangle");
        var spacing = 0.5;
        var dim = direction == NestDirection.Horizontal ? 10.0 : 8.0;
        // Locate the adjacent-double threshold with the frozen path: the legacy
        // slide can leave an epsilon in the pitch, so nominal rectangle arithmetic is not an oracle.
        var rejected = 2 * dim + spacing;
        var accepted = 4 * dim + 3 * spacing;
        for (var i = 0; i < 64; i++)
        {
            var middle = (rejected + accepted) / 2;
            var probeArea = new Box(3.1, 5.3, direction == NestDirection.Horizontal ? middle : 10,
                direction == NestDirection.Vertical ? middle : 8);
            if (new LegacyFillLinear(probeArea, spacing).FillRow(drawing, 0, direction).Parts.Count < 3)
                rejected = middle;
            else
                accepted = middle;
        }
        Assert.Equal(System.Math.BitIncrement(rejected), accepted);
        var counts = new HashSet<int>();
        foreach (var size in new[] { rejected, accepted })
        {
            var area = new Box(3.1, 5.3, direction == NestDirection.Horizontal ? size : 10,
                direction == NestDirection.Vertical ? size : 8);
            var legacy = new LegacyFillLinear(area, spacing);
            var filler = new FillLinear(area, spacing);
            var expected = legacy.FillRow(drawing, 0, direction).Parts;
            counts.Add(expected.Count);
            AssertLayout(expected, filler.FillRow(drawing, 0, direction).Parts, new[] { drawing.Program });
            AssertLayout(legacy.Fill(drawing, 0, direction), filler.Fill(drawing, 0, direction), new[] { drawing.Program });
        }
        Assert.Equal(new[] { 2, 3 }, counts.OrderBy(n => n));
    }

    [Fact]
    public void OverlappingSeeds_ExerciseLegacyBoundingBoxFallback()
    {
        // The legacy fallback does not repair an already-overlapping input pair.
        // This deliberately invalid seed deterministically exercises that branch.
        var first = new Part(Fixture("rectangle"));
        var pattern = new Pattern();
        pattern.Parts.AddRange(new[] { first, first.CloneAtOffset(new Vector(0.25, 0.25)) });
        pattern.UpdateBounds();
        var area = new Box(0, 0, 35, 8.25);
        var legacy = new LegacyFillLinear(area, 0.5);
        var raw = new List<Part>(pattern.Parts);
        raw.AddRange((List<Part>)FillExtentsTests.Invoke(legacy, "TilePattern", pattern, NestDirection.Horizontal));
        Assert.True(FillHelpers.HasOverlappingParts(raw));
        var fallback = new List<Part>(pattern.Parts);
        fallback.AddRange((List<Part>)FillExtentsTests.Invoke(legacy, "TilePatternBbox", pattern, NestDirection.Horizontal));
        var inputs = pattern.Parts.Select(p => p.Program).ToArray();
        AssertLayout(fallback, legacy.Fill(pattern, NestDirection.Horizontal), inputs);
        AssertLayout(fallback, new FillLinear(area, 0.5).Fill(pattern, NestDirection.Horizontal), inputs);
    }

    [Fact]
    public void EmptyAndNoFit_MatchFrozenLegacy()
    {
        var area = new Box(3, 5, 1, 1);
        var filler = new FillLinear(area, 0.5);
        var legacy = new LegacyFillLinear(area, 0.5);
        var drawing = Fixture("rectangle");
        var pattern = MakePattern("rotated");
        Assert.Empty(filler.Fill(new Pattern(), NestDirection.Horizontal));
        Assert.Empty(legacy.Fill(new Pattern(), NestDirection.Horizontal));
        Assert.Empty(filler.Fill(pattern, NestDirection.Vertical));
        Assert.Empty(legacy.Fill(pattern, NestDirection.Vertical));
        Assert.Empty(filler.Fill(drawing, 0, NestDirection.Horizontal));
        Assert.Empty(legacy.Fill(drawing, 0, NestDirection.Horizontal));
        Assert.Empty(filler.FillRow(drawing, 0, NestDirection.Vertical).Parts);
        Assert.Empty(legacy.FillRow(drawing, 0, NestDirection.Vertical).Parts);
    }

    [Fact]
    public void ConcurrentCalls_OneFiller_MatchSequentialAndPreserveInputs()
    {
        var patterns = new[] { MakePattern("shared"), MakePattern("rotated"), MakePattern("built-pair") };
        var before = patterns.Select(Snapshot).ToArray();
        var filler = new FillLinear(new Box(3.1, 5.3, 52, 39), 0.5);
        var expected = patterns.Select(p => new[] { filler.Fill(p, NestDirection.Horizontal),
            filler.Fill(p, NestDirection.Vertical) }).ToArray();
        Parallel.For(0, 48, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var index = i % patterns.Length;
            var direction = i % 2 == 0 ? NestDirection.Horizontal : NestDirection.Vertical;
            AssertLayout(expected[index][i % 2], filler.Fill(patterns[index], direction),
                patterns[index].Parts.Select(p => p.Program).ToArray());
        });
        for (var i = 0; i < patterns.Length; i++)
            Assert.Equal(before[i], Snapshot(patterns[i]));
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("concave")]
    [InlineData("arc")]
    [InlineData("circle")]
    [InlineData("ring")]
    public void LocalEntities_CloneThenTranslate_MatchIndependentPreChangeOracle(string shape)
    {
        var boxesDifferent = 0;
        var entitiesCompared = 0;
        foreach (var spacing in new[] { 0.0, 0.125, 0.5 })
            foreach (var angle in new[] { 0.0, 0.37, System.Math.PI / 2 })
                foreach (var location in new[] { new Vector(0, 0), new Vector(3.1, -5.3), new Vector(100000.1, -0.001) })
                {
                    var part = new Part(Fixture(shape));
                    part.Rotate(angle);
                    part.Offset(location);
                    var expected = PreChangeEntities(part, spacing);
                    var wrapper = PartGeometry.GetOffsetPerimeterEntities(part, spacing);
                    var local = PartGeometry.GetOffsetPerimeterEntities(part.Program, spacing);
                    var before = local.SelectMany(EntityValues).ToArray();
                    var beforeBoxes = local.SelectMany(e => BoxBits(e.BoundingBox)).ToArray();
                    var translated = local.Select(e =>
                    {
                        var clone = e.Clone();
                        clone.Offset(part.Location);
                        return clone;
                    }).ToList();
                    Assert.Equal(expected.Count, translated.Count);
                    Assert.Equal(expected.SelectMany(EntityValues), wrapper.SelectMany(EntityValues));
                    Assert.Equal(expected.SelectMany(e => BoxBits(e.BoundingBox)), wrapper.SelectMany(e => BoxBits(e.BoundingBox)));
                    for (var i = 0; i < expected.Count; i++)
                    {
                        Assert.Equal(EntityValues(expected[i]), EntityValues(translated[i]));
                        Assert.NotSame(local[i], translated[i]);
                        entitiesCompared++;
                        if (!BoxBits(expected[i].BoundingBox).SequenceEqual(BoxBits(translated[i].BoundingBox)))
                        {
                            boxesDifferent++;
                            if (boxesDifferent <= 3)
                                output.WriteLine($"bbox divergence: shape={shape}; spacing={spacing:R}; angle={angle:R}; location={location}; entity={i} {expected[i].GetType().Name}; old bits={string.Join(',', BoxBits(expected[i].BoundingBox))}; clone bits={string.Join(',', BoxBits(translated[i].BoundingBox))}");
                        }
                    }
                    Assert.Equal(before, local.SelectMany(EntityValues));
                    Assert.Equal(beforeBoxes, local.SelectMany(e => BoxBits(e.BoundingBox)));
                    // Verify the consumer on both axes even when clone boxes differ.
                    foreach (var direction in new[] { PushDirection.Left, PushDirection.Down })
                    {
                        var other = part.CloneAtOffset(new Vector(20.1, 17.3));
                        var stationary = PreChangeEntities(other, spacing);
                        Assert.Equal(Bits(SpatialQuery.DirectionalDistance(expected, stationary, direction)),
                            Bits(SpatialQuery.DirectionalDistance(translated, stationary, direction)));
                    }
                }
        output.WriteLine($"{shape}: entities={entitiesCompared}; bitwise different bounding boxes={boxesDifferent}");
    }

    [Fact]
    public void EmptyProgram_PreservesPreChangeException()
    {
        // ShapeProfile indexes shapes[0]; an empty program throws, rather than returning an empty perimeter.
        var part = new Part(new Drawing("empty", new Program()));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreChangeEntities(part, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PartGeometry.GetOffsetPerimeterEntities(part, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PartGeometry.GetOffsetPerimeterEntities(part.Program, 0.5));
    }

#if DEBUG
    [Theory]
    [InlineData("row", 2, 1)]
    [InlineData("drawing", 8, 1)]
    [InlineData("shared", 12, 1)]
    [InlineData("rotated", 12, 2)]
    public void Work_PerCallPreparesEachDistinctProgramOnce(string mode, long oldCount, long newCount)
    {
        var drawing = Fixture("rectangle");
        var area = mode is "row" or "drawing" ? new Box(3, 5, 31.5, 17) : new Box(3, 5, 42.5, 25);
        var pattern = MakePattern(mode == "shared" ? "shared" : "rotated", drawing);
        var legacy = new LegacyFillLinear(area, 0.5);
        var filler = new FillLinear(area, 0.5);
        PerfCounters.Reset();
        try
        {
            var expected = mode == "row" ? legacy.FillRow(drawing, 0, NestDirection.Horizontal).Parts
                : mode == "drawing" ? legacy.Fill(drawing, 0, NestDirection.Horizontal)
                : legacy.Fill(pattern, NestDirection.Horizontal);
            var before = PerfCounters.OffsetPerimeterEntities;
            PerfCounters.Reset();
            var actual = mode == "row" ? filler.FillRow(drawing, 0, NestDirection.Horizontal).Parts
                : mode == "drawing" ? filler.Fill(drawing, 0, NestDirection.Horizontal)
                : filler.Fill(pattern, NestDirection.Horizontal);
            var after = PerfCounters.OffsetPerimeterEntities;
            output.WriteLine($"work {mode}: legacy={before}; actual={after}; parts={actual.Count}");
            AssertLayout(expected, actual, pattern.Parts.Select(p => p.Program).Append(drawing.Program).ToArray());
            Assert.Equal(oldCount, before);
            Assert.Equal(newCount, after);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    private static Drawing Fixture(string shape) => shape switch
    {
        "circle" => new RingShape { OuterDiameter = 8, InnerDiameter = 0 }.GetDrawing(),
        "ring" => new RingShape { OuterDiameter = 8, InnerDiameter = 3 }.GetDrawing(),
        _ => FillExtentsTests.MakeFixture(shape),
    };

    private static Pattern MakePattern(string kind, Drawing? drawing = null)
    {
        drawing ??= Fixture("concave");
        var first = Part.CreateAtOrigin(drawing, 0);
        first.Offset(new Vector(11.1, -3.3));
        var second = kind == "shared" ? first.CloneAtOffset(new Vector(10.5, 0))
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

    // Independent 094c4c1 Part overload, not the new overload or delegating wrapper.
    private static List<Entity> PreChangeEntities(Part part, double spacing)
    {
        var geoEntities = ConvertProgram.ToGeometry(part.Program);
        var profile = new ShapeProfile(geoEntities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());
        var offsetShape = profile.Perimeter.OffsetOutward(spacing);
        if (offsetShape == null)
            return new List<Entity>();
        foreach (var entity in offsetShape.Entities)
            entity.Offset(part.Location);
        return offsetShape.Entities;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static long[] BoxBits(Box box) => new[] { Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width) };

    private static object[] EntityValues(Entity entity)
    {
        var values = new List<object> { entity.GetType(), entity.Layer };
        if (entity is Line line)
            values.AddRange(new object[] { Bits(line.pt1.X), Bits(line.pt1.Y), Bits(line.pt2.X), Bits(line.pt2.Y) });
        else if (entity is Arc arc)
            values.AddRange(new object[] { Bits(arc.Center.X), Bits(arc.Center.Y), Bits(arc.Radius),
                Bits(arc.StartAngle), Bits(arc.EndAngle), arc.IsReversed });
        else if (entity is Circle circle)
            values.AddRange(new object[] { Bits(circle.Center.X), Bits(circle.Center.Y), Bits(circle.Radius), circle.Rotation });
        else
            Assert.Fail($"Unexpected entity {entity.GetType()}");
        return values.ToArray();
    }

    private static object[] ProgramValues(Program program) =>
        new object[] { program, program.Codes.Count, Bits(program.Rotation), program.Mode }
            .Concat(program.Codes.Cast<object>()).Concat(ConvertProgram.ToGeometry(program).SelectMany(EntityValues)).ToArray();

    private static object[] Snapshot(Pattern pattern) => BoxBits(pattern.BoundingBox).Cast<object>()
        .Concat(pattern.Parts.SelectMany(p => new object[] { p, p.BaseDrawing, Bits(p.Rotation), Bits(p.Location.X), Bits(p.Location.Y) }
            .Concat(BoxBits(p.BoundingBox).Cast<object>()).Concat(ProgramValues(p.Program)))).ToArray();

    private static void AssertLayout(List<Part> expected, List<Part> actual, Program[] inputs)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i].BaseDrawing, actual[i].BaseDrawing);
            Assert.Equal(Bits(expected[i].Location.X), Bits(actual[i].Location.X));
            Assert.Equal(Bits(expected[i].Location.Y), Bits(actual[i].Location.Y));
            Assert.Equal(Bits(expected[i].Rotation), Bits(actual[i].Rotation));
            Assert.Equal(BoxBits(expected[i].BoundingBox), BoxBits(actual[i].BoundingBox));
            Assert.Equal(expected[i].Program.Codes.Count, actual[i].Program.Codes.Count);
            Assert.Equal(ConvertProgram.ToGeometry(expected[i].Program).SelectMany(EntityValues),
                ConvertProgram.ToGeometry(actual[i].Program).SelectMany(EntityValues));
            foreach (var input in inputs)
                Assert.Equal(ReferenceEquals(expected[i].Program, input), ReferenceEquals(actual[i].Program, input));
            for (var j = 0; j < expected.Count; j++)
                Assert.Equal(ReferenceEquals(expected[i].Program, expected[j].Program),
                    ReferenceEquals(actual[i].Program, actual[j].Program));
        }
    }
}

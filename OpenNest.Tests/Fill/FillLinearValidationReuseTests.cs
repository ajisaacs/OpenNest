using System.Diagnostics;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Engine.Tests.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;

namespace OpenNest.Tests.Fill;

[Collection(nameof(FillCacheCollection))]
public class FillLinearValidationReuseTests
{
    private readonly ITestOutputHelper output;

    public FillLinearValidationReuseTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> DrawingCases()
    {
        foreach (var shape in new[] { "rectangle", "concave", "arc", "circle", "ring" })
            foreach (var spacing in new[] { 0.0, 0.5 })
                foreach (var angle in new[] { 0.0, 0.37, System.Math.PI / 2 })
                    foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                        yield return new object[] { shape, spacing, angle, direction };
    }

    [Theory]
    [MemberData(nameof(DrawingCases))]
    public void DrawingFill_MatchesPreStep3_OrderedBitsProgramsAndImmutableInput(
        string shape, double spacing, double angle, NestDirection direction)
    {
        var drawing = MakeDrawing(shape);
        var before = DrawingSnapshot(drawing);
        var area = new Box(3.1, -5.3, 42, 29);
        var areaBefore = BoxBits(area);
        var expected = new PreStep3FillLinear(area, spacing).Fill(drawing, angle, direction);
        var filler = new FillLinear(area, spacing);
        AssertLayout(expected, filler.Fill(drawing, angle, direction), new[] { drawing.Program });
        Assert.Equal(before, DrawingSnapshot(drawing));
        AssertLayout(expected, filler.Fill(drawing, angle, direction), new[] { drawing.Program });
        AssertValid(expected, area);
        Assert.Equal(before, DrawingSnapshot(drawing));
        Assert.Equal(areaBefore, BoxBits(area));
        Assert.Equal(areaBefore, BoxBits(filler.WorkArea));
    }

    public static IEnumerable<object[]> PatternCases()
    {
        foreach (var shape in new[] { "concave", "arc", "circle", "ring" })
            foreach (var kind in new[] { "single", "shared", "rotated" })
                foreach (var spacing in new[] { 0.0, 0.5 })
                    foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                        yield return new object[] { shape, kind, spacing, direction };
    }

    [Theory]
    [MemberData(nameof(PatternCases))]
    public void PatternFill_MatchesPreStep3_SharingAndImmutableInput(
        string shape, string kind, double spacing, NestDirection direction)
    {
        var pattern = MakePattern(shape, kind);
        CheckPattern(pattern, new Box(-7.1, 11.3, 52, 39), spacing, direction);
    }

    [Theory]
    [InlineData("horizontal-stripe", 8)]
    [InlineData("vertical-stripe", 19)]
    [InlineData("full-grid", 36)]
    [InlineData("partial-only", 29)]
    [InlineData("single-seed-stripe", 8)]
    public void CharacterizedControls_MatchPreStep3(string mode, int count)
    {
        var pattern = MakePattern("concave", mode == "single-seed-stripe" ? "single" : "rotated");
        var vertical = mode is "vertical-stripe" or "partial-only";
        var direction = vertical ? NestDirection.Vertical : NestDirection.Horizontal;
        var area = mode switch
        {
            "full-grid" => new Box(0, 0, 96, 48),
            "partial-only" => new Box(0, 0, 1.8 * pattern.BoundingBox.Length, 96),
            "vertical-stripe" => new Box(0, 0, pattern.BoundingBox.Length, 96),
            _ => new Box(0, 0, 96, pattern.BoundingBox.Width),
        };
        var parts = CheckPattern(pattern, area, 0.5, direction);
        Assert.Equal(count, parts.Count);
        var stripe = vertical ? new Box(0, 0, pattern.BoundingBox.Length, 96)
            : new Box(0, 0, 96, pattern.BoundingBox.Width);
        var row = new PreStep3FillLinear(stripe, 0.5).Fill(pattern, direction);
        if (mode == "full-grid")
        {
            Assert.Equal(8, row.Count);
            Assert.Equal(28, parts.Count - row.Count);
        }
        if (mode == "partial-only")
        {
            Assert.Equal(19, row.Count);
            Assert.Equal(10, parts.Count - row.Count);
            // Fewer appended parts than the row: TilePattern's incomplete-copy path,
            // not a full row. The ordered prefix must be unchanged.
            AssertLayout(row, parts.Take(row.Count).ToList(), pattern.Parts.Select(p => p.Program).ToArray());
        }
        output.WriteLine($"{mode}: row={row.Count}; total={parts.Count}; appended={parts.Count - row.Count}");
    }

    [Theory]
    [InlineData(NestDirection.Horizontal, 9)]
    [InlineData(NestDirection.Vertical, 4)]
    public void PerpendicularOnly_MatchesPreStep3(NestDirection direction, int count)
    {
        var pattern = MakePattern("concave", "rotated");
        var area = direction == NestDirection.Horizontal
            ? new Box(0, 0, pattern.BoundingBox.Length, 48)
            : new Box(0, 0, 48, pattern.BoundingBox.Width);
        var seedOnlyArea = new Box(0, 0, pattern.BoundingBox.Length, pattern.BoundingBox.Width);
        Assert.Equal(pattern.Parts.Count, new PreStep3FillLinear(seedOnlyArea, 0.5).Fill(pattern, direction).Count);
        var parts = CheckPattern(pattern, area, 0.5, direction);
        Assert.True(parts.Count > pattern.Parts.Count);
        Assert.Equal(count, parts.Count);
        output.WriteLine($"PerpOnly {direction}: total={parts.Count}");
    }

    [Theory]
    [InlineData(NestDirection.Horizontal)]
    [InlineData(NestDirection.Vertical)]
    public void LastCopy_AdjacentDoubleThreshold_MatchesBothPublicFillOverloads(NestDirection direction)
    {
        var drawing = MakeDrawing("rectangle");
        var pattern = new Pattern();
        pattern.Parts.Add(Part.CreateAtOrigin(drawing));
        pattern.UpdateBounds();
        var before = PatternSnapshot(pattern);
        var dim = direction == NestDirection.Horizontal ? 10.0 : 8.0;
        Box Area(double size) => new(3.1, 5.3, direction == NestDirection.Horizontal ? size : 10,
            direction == NestDirection.Vertical ? size : 8);
        var rejected = 2 * dim + 0.5;
        var accepted = 4 * dim + 1.5;
        // Find the actual adjacent-double boundary through the frozen public Fill;
        // nominal pitch arithmetic is not an oracle for the accumulated FP endpoint.
        for (var i = 0; i < 64; i++)
        {
            var middle = (rejected + accepted) / 2;
            if (new PreStep3FillLinear(Area(middle), 0.5).Fill(pattern, direction).Count < 3)
                rejected = middle;
            else
                accepted = middle;
        }
        Assert.Equal(Bits(System.Math.BitIncrement(rejected)), Bits(accepted));
        foreach (var size in new[] { rejected, accepted })
        {
            var area = Area(size);
            var frozen = new PreStep3FillLinear(area, 0.5);
            var filler = new FillLinear(area, 0.5);
            var expectedPattern = frozen.Fill(pattern, direction);
            var expectedDrawing = frozen.Fill(drawing, 0, direction);
            Assert.Equal(size == rejected ? 2 : 3, expectedPattern.Count);
            Assert.Equal(expectedPattern.Count, expectedDrawing.Count);
            AssertLayout(expectedPattern, filler.Fill(pattern, direction), new[] { pattern.Parts[0].Program });
            AssertLayout(expectedDrawing, filler.Fill(drawing, 0, direction), new[] { drawing.Program });
        }
        Assert.Equal(before, PatternSnapshot(pattern));
        output.WriteLine($"{direction}: rejected={rejected:R} accepted={accepted:R}");
    }

    [Fact]
    public void InvalidOverlappingSeeds_PreserveBothFallbacks_EvenWithoutPerpendicularAdditions()
    {
        // Deliberately invalid: bbox fallback does not repair an overlapping seed.
        // Keep separate from assertions that valid fixtures never overlap.
        var first = Part.CreateAtOrigin(MakeDrawing("rectangle"));
        var pattern = new Pattern();
        pattern.Parts.AddRange(new[] { first, first.CloneAtOffset(new Vector(0.25, 0.25)) });
        pattern.UpdateBounds();
        Assert.True(FillHelpers.HasOverlappingParts(pattern.Parts));
        var before = PatternSnapshot(pattern);
        var area = new Box(0, 0, 35, 8.25);
        var frozen = new PreStep3FillLinear(area, 0.5) { Label = "invalid-frozen" };
        var filler = new FillLinear(area, 0.5) { Label = "invalid-production" };
#if DEBUG
        using var listener = new FallbackListener();
        Trace.Listeners.Add(listener);
        try
        {
#endif
            var expected = frozen.Fill(pattern, NestDirection.Horizontal);
            var actual = filler.Fill(pattern, NestDirection.Horizontal);
            Assert.Equal(6, expected.Count);
            AssertLayout(expected, actual, pattern.Parts.Select(p => p.Program).ToArray());
            Assert.True(FillHelpers.HasOverlappingParts(actual));
            Assert.Equal(before, PatternSnapshot(pattern));
#if DEBUG
            foreach (var label in new[] { "invalid-frozen", "invalid-production" })
            {
                var records = listener.Records.Where(r => r.Label == label).ToArray();
                Assert.Equal(new[] { "Step1-Primary", "Step2-Perp" }, records.Select(r => r.Step));
                Assert.All(records, r =>
                {
                    Assert.Equal(6, r.Count);
                    Assert.Equal("Overlapping pair [0] vs [1]:", r.Pair);
                });
                output.WriteLine($"{label}: Step1-Primary and Step2-Perp, both total=6 pair=(0,1); zero perpendicular additions");
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
#endif
    }

    [Theory]
    [InlineData(NestDirection.Horizontal)]
    [InlineData(NestDirection.Vertical)]
    public void EmptyNoFitAndMalformedInputs_PreserveResultsAndExceptionTypes(NestDirection direction)
    {
        var area = new Box(3, 5, 1, 1);
        var filler = new FillLinear(area, 0.5);
        var frozen = new PreStep3FillLinear(area, 0.5);
        var drawing = MakeDrawing("rectangle");
        var pattern = MakePattern("concave", "rotated");
        var before = PatternSnapshot(pattern);
        Assert.Empty(frozen.Fill(new Pattern(), direction));
        Assert.Empty(filler.Fill(new Pattern(), direction));
        Assert.Empty(frozen.Fill(pattern, direction));
        Assert.Empty(filler.Fill(pattern, direction));
        Assert.Empty(frozen.Fill(drawing, 0.37, direction));
        Assert.Empty(filler.Fill(drawing, 0.37, direction));
        Assert.Equal(before, PatternSnapshot(pattern));
        Assert.Throws<NullReferenceException>(() => frozen.Fill((Pattern)null!, direction));
        Assert.Throws<NullReferenceException>(() => filler.Fill((Pattern)null!, direction));
        Assert.Throws<NullReferenceException>(() => frozen.Fill((Drawing)null!, 0, direction));
        Assert.Throws<NullReferenceException>(() => filler.Fill((Drawing)null!, 0, direction));
        Assert.Throws<NullReferenceException>(() => new PreStep3FillLinear(null!, 0.5));
        Assert.Throws<NullReferenceException>(() => new FillLinear(null!, 0.5));
        foreach (var rapidOnly in new[] { false, true })
        {
            var program = new Program();
            if (rapidOnly)
                program.Codes.Add(new RapidMove(new Vector(0, 0)));
            var empty = new Drawing("empty-material", program);
            var emptyBefore = DrawingSnapshot(empty);
            var emptyPattern = new Pattern();
            emptyPattern.Parts.Add(Part.CreateAtOrigin(empty));
            emptyPattern.UpdateBounds();
            var patternBefore = PatternSnapshot(emptyPattern);
            Assert.Throws<ArgumentOutOfRangeException>(() => frozen.Fill(empty, 0, direction));
            Assert.Throws<ArgumentOutOfRangeException>(() => filler.Fill(empty, 0, direction));
            Assert.Throws<ArgumentOutOfRangeException>(() => frozen.Fill(emptyPattern, direction));
            Assert.Throws<ArgumentOutOfRangeException>(() => filler.Fill(emptyPattern, direction));
            Assert.Equal(emptyBefore, DrawingSnapshot(empty));
            Assert.Equal(patternBefore, PatternSnapshot(emptyPattern));
        }
    }

    [Fact]
    public void ConcurrentIndependentCalls_OneFiller_MatchFrozenAndPreserveInputs()
    {
        var filler = new FillLinear(new Box(3.1, -5.3, 52, 39), 0.5);
        var frozen = new PreStep3FillLinear(filler.WorkArea, 0.5);
        var patterns = Enumerable.Range(0, 24).Select(i => MakePattern(i % 2 == 0 ? "concave" : "arc",
            i % 3 == 0 ? "shared" : i % 3 == 1 ? "rotated" : "single")).ToArray();
        var drawings = patterns.Select(p => p.Parts[0].BaseDrawing).ToArray();
        var before = patterns.Select(PatternSnapshot).ToArray();
        var expectedPatterns = patterns.Select((p, i) => frozen.Fill(p,
            i % 2 == 0 ? NestDirection.Horizontal : NestDirection.Vertical)).ToArray();
        var expectedDrawings = drawings.Select((d, i) => frozen.Fill(d, 0.37,
            i % 2 == 0 ? NestDirection.Horizontal : NestDirection.Vertical)).ToArray();
        Parallel.For(0, patterns.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var direction = i % 2 == 0 ? NestDirection.Horizontal : NestDirection.Vertical;
            AssertLayout(expectedPatterns[i], filler.Fill(patterns[i], direction), patterns[i].Parts.Select(p => p.Program).ToArray());
            AssertLayout(expectedDrawings[i], filler.Fill(drawings[i], 0.37, direction), new[] { drawings[i].Program });
            Assert.Equal(before[i], PatternSnapshot(patterns[i]));
        });
    }

    private static Drawing MakeDrawing(string shape)
    {
        var drawing = shape switch
        {
            "circle" => new CircleShape { Diameter = 8 }.GetDrawing(),
            "ring" => new RingShape { OuterDiameter = 8, InnerDiameter = 3 }.GetDrawing(),
            _ => FillExtentsTests.MakeFixture(shape),
        };
        var profile = new ShapeProfile(ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());
        Assert.True(profile.Perimeter.IsClosed());
        Assert.All(profile.Cutouts, cutout => Assert.True(cutout.IsClosed()));
        Assert.True(profile.Perimeter.Area() - profile.Cutouts.Sum(c => c.Area()) > 0);
        Assert.True(drawing.Area > 0);
        if (shape == "concave")
            Assert.Equal(50.0, drawing.Area);
        if (shape is "arc" or "circle" or "ring")
            Assert.Contains(drawing.Program.Codes, code => code is ArcMove);
        return drawing;
    }

    private static Pattern MakePattern(string shape, string kind)
    {
        var drawing = MakeDrawing(shape);
        var first = Part.CreateAtOrigin(drawing, 0);
        if (kind == "single")
            return FillHelpers.BuildRotatedPattern(new List<Part> { first }, 0.37);
        var second = kind == "shared" ? first.CloneAtOffset(new Vector(10.5, 0))
            : Part.CreateAtOrigin(drawing, System.Math.PI);
        if (kind != "shared")
            second.Offset(new Vector(10.5, 0));
        if (kind == "rotated")
            return FillHelpers.BuildRotatedPattern(new List<Part> { first, second }, 0.37);
        var pattern = new Pattern();
        pattern.Parts.AddRange(new[] { first, second });
        pattern.UpdateBounds();
        Assert.Same(first.Program, second.Program);
        return pattern;
    }

    private static List<Part> CheckPattern(Pattern pattern, Box area, double spacing, NestDirection direction)
    {
        var before = PatternSnapshot(pattern);
        var areaBefore = BoxBits(area);
        var inputs = pattern.Parts.Select(p => p.Program).ToArray();
        Assert.False(FillHelpers.HasOverlappingParts(pattern.Parts));
        var expected = new PreStep3FillLinear(area, spacing).Fill(pattern, direction);
        Assert.Equal(before, PatternSnapshot(pattern));
        var filler = new FillLinear(area, spacing);
        var actual = filler.Fill(pattern, direction);
        AssertLayout(expected, actual, inputs);
        Assert.Equal(before, PatternSnapshot(pattern));
        AssertLayout(expected, filler.Fill(pattern, direction), inputs);
        Assert.Equal(before, PatternSnapshot(pattern));
        AssertValid(actual, area);
        Assert.Equal(areaBefore, BoxBits(area));
        Assert.Equal(areaBefore, BoxBits(filler.WorkArea));
        return actual;
    }

    private static void AssertValid(List<Part> parts, Box area)
    {
        Assert.NotEmpty(parts);
        Assert.All(parts, p =>
        {
            Assert.True(p.Left >= area.Left - Tolerance.Epsilon && p.Right <= area.Right + Tolerance.Epsilon);
            Assert.True(p.Bottom >= area.Bottom - Tolerance.Epsilon && p.Top <= area.Top + Tolerance.Epsilon);
        });
        Assert.False(FillHelpers.HasOverlappingParts(parts));
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static long[] BoxBits(Box box) => new[] { Bits(box.X), Bits(box.Y), Bits(box.Length), Bits(box.Width) };

    private static object[] ProgramValues(Program program)
    {
        var values = new List<object> { program.Mode, Bits(program.Rotation), program.Codes.Count };
        values.AddRange(BoxBits(program.BoundingBox()).Cast<object>());
        foreach (var code in program.Codes)
        {
            values.Add(code.GetType());
            var motion = Assert.IsAssignableFrom<Motion>(code);
            values.AddRange(new object[] { code.Type, Bits(motion.EndPoint.X), Bits(motion.EndPoint.Y),
                motion.Feedrate, motion.UseExactStop, motion.Suppressed, motion.VariableRefs?.Count ?? -1 });
            if (motion.VariableRefs != null)
                foreach (var entry in motion.VariableRefs.OrderBy(e => e.Key))
                    values.AddRange(new object[] { entry.Key, entry.Value });
            if (code is LinearMove line)
                values.Add(line.Layer);
            else if (code is ArcMove arc)
                values.AddRange(new object[] { Bits(arc.CenterPoint.X), Bits(arc.CenterPoint.Y), arc.Rotation, arc.Layer });
            else
                Assert.IsType<RapidMove>(code);
        }
        return values.ToArray();
    }

    private static object[] DrawingSnapshot(Drawing drawing) => new object[] { drawing, drawing.Program, Bits(drawing.Area) }
        .Concat(drawing.Program.Codes.Cast<object>()).Concat(ProgramValues(drawing.Program)).ToArray();

    private static object[] PatternSnapshot(Pattern pattern) => BoxBits(pattern.BoundingBox).Cast<object>()
        .Concat(pattern.Parts.SelectMany(p => new object[] { p, p.Program, Bits(p.Location.X), Bits(p.Location.Y), Bits(p.Rotation) }
            .Concat(BoxBits(p.BoundingBox).Cast<object>()).Concat(p.Program.Codes.Cast<object>())
            .Concat(ProgramValues(p.Program)).Concat(DrawingSnapshot(p.BaseDrawing)))).ToArray();

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
            Assert.Equal(ProgramValues(expected[i].Program), ProgramValues(actual[i].Program));
            foreach (var input in inputs)
                Assert.Equal(ReferenceEquals(expected[i].Program, input), ReferenceEquals(actual[i].Program, input));
            for (var j = 0; j < expected.Count; j++)
                Assert.Equal(ReferenceEquals(expected[i].Program, expected[j].Program),
                    ReferenceEquals(actual[i].Program, actual[j].Program));
        }
    }

#if DEBUG
    private sealed class FallbackListener : TraceListener
    {
        internal sealed class Entry
        {
            public string Label = "";
            public string Step = "";
            public int Count;
            public string Pair = "";
        }

        public List<Entry> Records { get; } = new();
        private Entry? current;

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            var line = message?.Trim() ?? "";
            const string prefix = "[FillLinear] OVERLAP FALLBACK (";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                current = new Entry { Label = line[prefix.Length..^1] };
                Records.Add(current);
            }
            else if (current != null && line.StartsWith("Step: ", StringComparison.Ordinal))
                current.Step = line[6..].Split(',')[0];
            else if (current != null && line.StartsWith("Total parts after tiling: ", StringComparison.Ordinal))
                current.Count = int.Parse(line[26..], System.Globalization.CultureInfo.InvariantCulture);
            else if (current != null && line.StartsWith("Overlapping pair ", StringComparison.Ordinal))
                current.Pair = line;
        }
    }
#endif
}

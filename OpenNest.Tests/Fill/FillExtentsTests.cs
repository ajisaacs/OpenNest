using System.Reflection;
using OpenNest.CNC;
using OpenNest.Engine.Strategies;
using OpenNest.Tests.BestFit;
using Xunit.Abstractions;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests.Fill;

[Collection(nameof(FillCacheCollection))]
public class FillExtentsTests
{
    private readonly ITestOutputHelper output;

    public FillExtentsTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> DifferentialCases()
    {
        foreach (var shape in new[] { "rectangle", "triangle", "concave", "arc" })
            foreach (var spacing in new[] { 0.0, 0.5 })
                foreach (var rotated in new[] { false, true })
                    yield return new object[] { shape, spacing, rotated };
    }

    [Theory]
    [MemberData(nameof(DifferentialCases))]
    public void Fill_MatchesFrozenLegacy_OrderedLayoutAndInputOwnership(string shape, double spacing, bool rotated)
    {
        var drawing = MakeFixture(shape);
        if (rotated)
            drawing.Program.Rotate(System.Math.PI / 2);
        var before = Snapshot(drawing);
        var workArea = new Box(3, 5, 45, 27);
        var areaBefore = Bounds(workArea);
        var angle = rotated ? System.Math.PI / 2 : 0;
        var expectedProgress = new List<(List<Part> Parts, string Message)>();
        var actualProgress = new List<(List<Part> Parts, string Message)>();
        var expected = new LegacyFillExtents(workArea, spacing).Fill(drawing, angle,
            reportProgress: (parts, message) => expectedProgress.Add((parts, message)));
        var actual = new FillExtents(workArea, spacing).Fill(drawing, angle,
            reportProgress: (parts, message) => actualProgress.Add((parts, message)));

        AssertSameLayout(expected, actual);
        Assert.Equal(expectedProgress.Count, actualProgress.Count);
        for (var i = 0; i < expectedProgress.Count; i++)
        {
            Assert.Equal(expectedProgress[i].Message, actualProgress[i].Message);
            AssertSameLayout(expectedProgress[i].Parts, actualProgress[i].Parts);
        }
        AssertValidLayout(actual, workArea);
        Assert.Equal(before, Snapshot(drawing));
        Assert.Equal(areaBefore, Bounds(workArea));
        Assert.All(actual, part => Assert.NotSame(drawing.Program, part.Program));
        // Offset clones intentionally share programs until rotation takes ownership.
        for (var i = 0; i < actual.Count; i++)
            for (var j = 0; j < actual.Count; j++)
                Assert.Equal(ReferenceEquals(expected[i].Program, expected[j].Program),
                    ReferenceEquals(actual[i].Program, actual[j].Program));
        var siblingPrograms = actual.Skip(1).Select(part => ProgramValues(part.Program)).ToArray();
        actual[0].Rotate(0.125);
        for (var i = 1; i < actual.Count; i++)
            Assert.Equal(siblingPrograms[i - 1], ProgramValues(actual[i].Program));
        Assert.Equal(before, Snapshot(drawing));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(15, 10)] // One rectangle fits, the pair does not.
    public void Fill_NoFit_MatchesFrozenLegacy(double length, double width)
    {
        var drawing = MakeRect(10, 8);
        var before = Snapshot(drawing);
        var area = new Box(3, 5, length, width);
        var expected = new LegacyFillExtents(area, 0.5).Fill(drawing);
        var actual = new FillExtents(area, 0.5).Fill(drawing);
        Assert.Empty(expected);
        AssertSameLayout(expected, actual);
        Assert.Equal(before, Snapshot(drawing));
    }

    [Fact]
    public void Fill_PreCancelled_MatchesFrozenLegacyRatherThanThrowing()
    {
        var drawing = MakeFixture("triangle");
        var before = Snapshot(drawing);
        var area = new Box(3, 5, 45, 27);
        var token = new CancellationToken(true);
        var expected = new LegacyFillExtents(area, 0.5).Fill(drawing, token: token);
        var actual = new FillExtents(area, 0.5).Fill(drawing, token: token);
        Assert.NotEmpty(expected);
        AssertSameLayout(expected, actual);
        AssertValidLayout(actual, area);
        Assert.Equal(before, Snapshot(drawing));
    }

    [Theory]
    [InlineData("rectangle", 0.0, false)]
    [InlineData("triangle", 0.5, false)]
    [InlineData("concave", 0.5, false)]
    [InlineData("arc", 0.5, false)]
    [InlineData("triangle", 0.0, true)]
    [InlineData("triangle", 0.5, true)]
    public void ColumnAdjustment_UsesAdjustedOrOverlapFallbackWithinBounds(string shape, double spacing, bool fallback)
    {
        var drawing = MakeFixture(shape);
        var area = new Box(3, 5, 45, 27);
        var legacy = new LegacyFillExtents(area, spacing);
        var angle = fallback ? System.Math.PI / 6 : 0;
        var pair = Invoke(legacy, "BuildPair", drawing, angle);
        var initial = (List<Part>)Invoke(legacy, "BuildColumn", pair);
        var adjusted = (List<Part>)Invoke(legacy, "AdjustColumn", pair, initial, CancellationToken.None);
        var overlap = FillHelpers.HasOverlappingParts(adjusted);
        Assert.NotSame(initial, adjusted);
        Assert.Equal(fallback, overlap);
        output.WriteLine($"{shape}, spacing={spacing}: initial={initial.Count}, adjusted={adjusted.Count}, "
            + $"same={ReferenceEquals(initial, adjusted)}, overlap={overlap}");
        var progress = new List<List<Part>>();
        var actual = new FillExtents(area, spacing).Fill(drawing, angle,
            reportProgress: (parts, _) => progress.Add(parts));
        AssertSameLayout(overlap ? initial : adjusted, progress[1]);
        AssertValidLayout(progress[1], area);
        AssertValidLayout(actual, area);
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("arc")]
    public void Fill_KnownLegacyTightWidthOverrun_RemainsDifferentialOnly(string shape)
    {
        // Baseline already extends ~1e-5 beyond this exactly tiled width at zero spacing.
        // Record that limitation separately; this optimization does not fix geometry.
        var area = new Box(3, 5, 40, 27);
        var drawing = MakeFixture(shape);
        var expected = new LegacyFillExtents(area, 0).Fill(drawing);
        var actual = new FillExtents(area, 0).Fill(drawing);
        AssertSameLayout(expected, actual);
        Assert.Contains(expected, part => part.Right > area.Right + 1e-6);
        Assert.InRange(expected.Max(part => part.Right) - area.Right, 9e-6, 11e-6);
    }

    [Theory]
    [InlineData(-0.25)]
    [InlineData(-0.5)]
    public void Fill_NegativeSpacing_PreservesLegacyResultsAndInputs(double spacing)
    {
        var area = new Box(3, 5, 45, 27);
        var drawing = MakeFixture("rectangle");
        var before = Snapshot(drawing);
        var expected = new LegacyFillExtents(area, spacing).Fill(drawing);
        var actual = new FillExtents(area, spacing).Fill(drawing);
        AssertSameLayout(expected, actual);
        Assert.Equal(before, Snapshot(drawing));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Fill_NonfiniteSpacing_NoFitStillReturnsEmptyWithoutValidation(double spacing)
    {
        var area = new Box(3, 5, 1, 1);
        var drawing = MakeFixture("rectangle");
        Assert.Empty(new LegacyFillExtents(area, spacing).Fill(drawing));
        Assert.Empty(new FillExtents(area, spacing).Fill(drawing));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-10.0)]
    [InlineData(double.NegativeInfinity)]
    public void BuildColumn_UnsupportedNegativeSpacing_RetainsLegacyPitch(double spacing)
    {
        var area = new Box(3, 5, 45, 27);
        var drawing = MakeFixture("rectangle");
        // Prepare finite valid pairs independently of unsupported spacing. This isolates
        // the private column calculation without risking legacy nonfinite public tiling.
        var legacyPair = Invoke(new LegacyFillExtents(area, 0), "BuildPair", drawing, 0.0);
        var pair = Invoke(new FillExtents(area, 0), "BuildPair", drawing, 0.0);
        var expected = (List<Part>)Invoke(new LegacyFillExtents(area, spacing), "BuildColumn", legacyPair);
        var actual = (List<Part>)Invoke(new FillExtents(area, spacing), "BuildColumn", pair);
        Assert.Equal(6, expected.Count);
        AssertSameLayout(expected, actual);
        Assert.Equal(8.0, actual[2].Bottom - actual[0].Bottom, 10);
    }

#if DEBUG
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void BuildColumn_RepeatedCalls_DoNotPrepareBoundaries(double spacing)
    {
        var area = new Box(3, 5, 45, 27);
        var drawing = MakeFixture("triangle");
        var legacy = new LegacyFillExtents(area, spacing);
        var filler = new FillExtents(area, spacing);
        PerfCounters.Reset();
        try
        {
            var legacyPair = Invoke(legacy, "BuildPair", drawing, 0.0);
            PerfCounters.Reset();
            var pair = Invoke(filler, "BuildPair", drawing, 0.0);
            Assert.Equal(2, PerfCounters.PartBoundaryPreparations); // BuildPair must retain geometry.
            PerfCounters.Reset();
            var expected = new List<List<Part>>();
            for (var i = 0; i < 4; i++)
                expected.Add((List<Part>)Invoke(legacy, "BuildColumn", legacyPair));
            Assert.Equal(8, PerfCounters.PartBoundaryPreparations);
            PerfCounters.Reset();
            for (var i = 0; i < 4; i++)
                AssertSameLayout(expected[i], (List<Part>)Invoke(filler, "BuildColumn", pair));
            Assert.Equal(0, PerfCounters.PartBoundaryPreparations);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void Fill_RebuildsColumns_OnlyBuildPairPreparesBoundaries(double spacing)
    {
        var area = new Box(3, 5, 45, 27);
        var drawing = MakeFixture("triangle");
        PerfCounters.Reset();
        try
        {
            var expected = new LegacyFillExtents(area, spacing).Fill(drawing);
            var legacyPreparations = PerfCounters.PartBoundaryPreparations;
            // Only BuildPair and BuildColumn construct PartBoundary in this pipeline.
            // More than four proves AdjustColumn called BuildColumn again.
            Assert.True(legacyPreparations > 4);
            PerfCounters.Reset();
            var actual = new FillExtents(area, spacing).Fill(drawing);
            output.WriteLine($"spacing={spacing}: legacy boundaries={legacyPreparations}, "
                + $"BuildColumn calls={(legacyPreparations - 2) / 2}, actual boundaries={PerfCounters.PartBoundaryPreparations}, parts={actual.Count}");
            AssertSameLayout(expected, actual);
            AssertValidLayout(actual, area);
            Assert.Equal(2, PerfCounters.PartBoundaryPreparations);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    internal static Drawing MakeFixture(string shape)
    {
        if (shape == "rectangle")
            return MakeRect(10, 8);
        if (shape == "triangle")
            return MakeRightTriangle(10, 8);
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        if (shape == "concave")
        {
            foreach (var point in new[] { new Vector(10, 0), new Vector(10, 3),
                new Vector(4, 3), new Vector(4, 8), new Vector(0, 8), new Vector(0, 0) })
                program.Codes.Add(new LinearMove(point));
        }
        else
        {
            // Native CCW quarter arcs, not a polygonized or self-crossing approximation.
            program.Codes.Add(new LinearMove(new Vector(9, 0)));
            program.Codes.Add(new ArcMove(new Vector(10, 1), new Vector(9, 1), RotationType.CCW));
            program.Codes.Add(new LinearMove(new Vector(10, 7)));
            program.Codes.Add(new ArcMove(new Vector(9, 8), new Vector(9, 7), RotationType.CCW));
            program.Codes.Add(new LinearMove(new Vector(0, 8)));
            program.Codes.Add(new LinearMove(new Vector(0, 0)));
        }
        return new Drawing(shape, program);
    }

    internal static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(target, args)!;

    internal static void AssertSameLayout(List<Part> expected, List<Part> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i].BaseDrawing, actual[i].BaseDrawing);
            Assert.Equal(expected[i].Location, actual[i].Location);
            Assert.Equal(expected[i].Rotation, actual[i].Rotation);
            Assert.Equal(Bounds(expected[i].BoundingBox), Bounds(actual[i].BoundingBox));
            Assert.Equal(ProgramValues(expected[i].Program), ProgramValues(actual[i].Program));
        }
    }

    internal static void AssertValidLayout(List<Part> parts, Box area)
    {
        Assert.NotEmpty(parts);
        foreach (var part in parts)
        {
            Assert.True(part.BaseDrawing.Area > 0);
            Assert.True(part.Left >= area.Left - 1e-6 && part.Right <= area.Right + 1e-6
                && part.Bottom >= area.Bottom - 1e-6 && part.Top <= area.Top + 1e-6);
            Assert.All(new[] { part.Left, part.Right, part.Bottom, part.Top, part.Rotation },
                value => Assert.True(double.IsFinite(value)));
        }
        Assert.False(FillHelpers.HasOverlappingParts(parts));
    }

    private static (double X, double Y, double Length, double Width) Bounds(Box box) =>
        (box.X, box.Y, box.Length, box.Width);

    private static object[] ProgramValues(Program program)
    {
        var values = new List<object> { program.Mode, program.Rotation, Bounds(program.BoundingBox()) };
        foreach (var code in program.Codes)
        {
            values.Add(code.GetType());
            if (code is Motion motion)
                values.Add(motion.EndPoint);
            if (code is LinearMove line)
                values.Add(line.Layer);
            if (code is ArcMove arc)
            {
                values.Add(arc.CenterPoint);
                values.Add(arc.Rotation);
                values.Add(arc.Layer);
            }
        }
        return values.ToArray();
    }

    private static object[] Snapshot(Drawing drawing) => new object[] { drawing, drawing.Area, drawing.Program }
        .Concat(drawing.Program.Codes.Cast<object>()).Concat(ProgramValues(drawing.Program)).ToArray();

    private static Drawing MakeRightTriangle(double w, double h)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("triangle", pgm);
    }

    private static Drawing MakeRect(double w, double h)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    [Fact]
    public void Fill_Triangle_ReturnsPartsWithinWorkArea()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.True(parts.Count > 0, "Should place at least one part");

        foreach (var part in parts)
        {
            Assert.True(
                part.BoundingBox.Right <= workArea.Right + 0.01,
                $"Part right edge {part.BoundingBox.Right} exceeds work area {workArea.Right}"
            );
            Assert.True(
                part.BoundingBox.Top <= workArea.Top + 0.01,
                $"Part top edge {part.BoundingBox.Top} exceeds work area {workArea.Top}"
            );
        }
    }

    [Fact]
    public void Fill_PartTooLarge_ReturnsEmpty()
    {
        var workArea = new Box(0, 0, 5, 5);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRect(10, 10);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.Empty(parts);
    }

    [Fact]
    public void Fill_Triangle_ColumnFillsHeight()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.True(parts.Count > 0);

        // The topmost part should be close to the work area top edge.
        var topEdge = 0.0;
        foreach (var part in parts)
        {
            if (part.BoundingBox.Top > topEdge)
                topEdge = part.BoundingBox.Top;
        }

        // After adjustment, the gap should be small (within one part spacing).
        var gap = workArea.Top - topEdge;
        Assert.True(
            gap < 1.0,
            $"Gap of {gap:F2} is too large — adjustment should fill close to the top"
        );
    }

    [Fact]
    public void Fill_Triangle_FillsWidthWithMultipleColumns()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        // With a 120-wide sheet and ~10-wide parts, we should get multiple columns.
        Assert.True(
            parts.Count >= 8,
            $"Expected multiple columns but got only {parts.Count} parts"
        );

        // Verify all parts are within bounds.
        foreach (var part in parts)
        {
            Assert.True(part.BoundingBox.Right <= workArea.Right + 0.01);
            Assert.True(part.BoundingBox.Top <= workArea.Top + 0.01);
            Assert.True(part.BoundingBox.Left >= workArea.Left - 0.01);
            Assert.True(part.BoundingBox.Bottom >= workArea.Bottom - 0.01);
        }
    }

    [Fact]
    public void Fill_Rect_ReturnsNonEmpty()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRect(15, 10);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.True(parts.Count > 0, "Rectangle should produce results");
    }

    [Fact]
    public void Fill_NonZeroOriginWorkArea_PartsWithinBounds()
    {
        // Simulate a remnant sub-region with non-zero origin.
        var workArea = new Box(30, 10, 80, 40);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.True(parts.Count > 0);

        foreach (var part in parts)
        {
            Assert.True(
                part.BoundingBox.Left >= workArea.Left - 0.01,
                $"Part left {part.BoundingBox.Left} below work area left {workArea.Left}"
            );
            Assert.True(
                part.BoundingBox.Bottom >= workArea.Bottom - 0.01,
                $"Part bottom {part.BoundingBox.Bottom} below work area bottom {workArea.Bottom}"
            );
            Assert.True(part.BoundingBox.Right <= workArea.Right + 0.01);
            Assert.True(part.BoundingBox.Top <= workArea.Top + 0.01);
        }
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing, token: cts.Token);

        Assert.NotNull(parts);
    }
}

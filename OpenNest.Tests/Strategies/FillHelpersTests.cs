using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Shapes;
using OpenNest.Tests.BestFit;

namespace OpenNest.Tests.Strategies;

[Collection(nameof(FillCacheCollection))]
public class FillHelpersTests
{
    [Theory]
    [InlineData("accumulated-translation", 0.0)]
    [InlineData("accumulated-translation", 0.37)]
    [InlineData("translated", 0.0)]
    [InlineData("translated", 0.37)]
    [InlineData("pre-rotated", 0.0)]
    [InlineData("pre-rotated", 0.37)]
    [InlineData("canonical", 0.0)]
    [InlineData("canonical", 0.37)]
    [InlineData("arc", 0.0)]
    [InlineData("arc", 0.37)]
    [InlineData("nonzero-program-origin", 0.0)]
    [InlineData("nonzero-program-origin", 0.37)]
    public void BuildRotatedPattern_MatchesPreChangePosesBoundsAndOwnership(string scenario, double angle)
    {
        var drawing = OpenNest.Tests.Fill.FillExtentsTests.MakeFixture(scenario == "arc" ? "arc" : "concave");
        if (scenario is "pre-rotated" or "canonical")
            drawing.Program.Rotate(0.6);
        double canonicalAngle = 0;
        if (scenario == "canonical")
        {
            // Program.Rotate mutates in place; refresh the canonical angle the same way
            // a rotated CAD import carries it, or AsCanonicalCopy takes the zero-angle path.
            drawing.RecomputeCanonicalAngle();
            canonicalAngle = drawing.Source.Angle;
            Assert.False(OpenNest.Math.Tolerance.IsEqualTo(canonicalAngle, 0));
            drawing = CanonicalFrame.AsCanonicalCopy(drawing);
            Assert.Equal(0.0, drawing.Source.Angle);
        }
        if (scenario == "accumulated-translation")
            drawing.Program.Offset(new Vector(0.1, 0.1));
        if (scenario == "nonzero-program-origin")
            drawing.Program.Offset(new Vector(0.125, -0.375));
        var group = new List<Part>
        {
            new(drawing, new Vector(11.25, 13.5)),
            new(drawing, new Vector(31.75, 27.25)),
        };
        if (scenario == "translated")
            foreach (var part in group)
                part.Offset(3.5, -2.25);
        if (scenario == "accumulated-translation")
        {
            group = new List<Part> { new(drawing, new Vector(0.1, 0.1)) };
            group[0].Offset(1.1, 1.1);
        }
        var before = Snapshot(group);
        var expected = PreChangeRotatedPattern(group, angle);
        var actual = FillHelpers.BuildRotatedPattern(group, angle);
        OpenNest.Tests.Fill.FillExtentsTests.AssertSameLayout(expected.Parts, actual.Parts);
        Assert.Equal(Bounds(expected.BoundingBox), Bounds(actual.BoundingBox));
        AssertNoInputPartsReturned(group, actual.Parts);
        Assert.Equal(before, Snapshot(group));
        for (var i = 0; i < group.Count; i++)
        {
            Assert.NotSame(group[i].Program, actual.Parts[i].Program);
            // Clone must not reapply the baked drawing rotation before adding the angle.
            // Normalize both sides: a baked canonical rotation can already sit at exactly 2pi.
            Assert.Equal(OpenNest.Math.Angle.NormalizeRad(group[i].Rotation + angle),
                OpenNest.Math.Angle.NormalizeRad(actual.Parts[i].Rotation));
        }
    }

    // The exact pre-Task-3 helper, kept local to characterization, never used for timings.
    internal static Pattern PreChangeRotatedPattern(List<Part> group, double angle)
    {
        var pattern = new Pattern();
        var center = ((IEnumerable<IBoundable>)group).GetBoundingBox().Center;
        foreach (var part in group)
        {
            var clone = (Part)part.Clone();
            clone.UpdateBounds();
            if (!OpenNest.Math.Tolerance.IsEqualTo(angle, 0))
                clone.Rotate(angle, center);
            pattern.Parts.Add(clone);
        }
        pattern.UpdateBounds();
        return pattern;
    }

    [Theory]
    [InlineData(5, 9, 8, 7)]
    [InlineData(10, 4, 7, 8)]
    [InlineData(5, 4, 3, 3)]
    [InlineData(7, 6, 8, 8)]
    public void FillPattern_DefaultScoring_PreservesWinningLayoutAndInputs(
        double length, double width, int horizontalCount, int verticalCount)
    {
        var group = MakeGroup();
        var before = Snapshot(group);
        var workArea = new Box(3, 5, length, width);
        var areaBefore = Bounds(workArea);
        var engine = new FillLinear(workArea, 0.25);
        var angles = new List<double> { 0 };
        var pattern = FillHelpers.BuildRotatedPattern(group, 0);
        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);
        Assert.Equal(horizontalCount, h.Count);
        Assert.Equal(verticalCount, v.Count);
        AssertValidLayout(h, workArea);
        AssertValidLayout(v, workArea);
        var hScore = FillScore.Compute(h, workArea);
        var vScore = FillScore.Compute(v, workArea);
        Assert.NotEqual(hScore, vScore);
        var expected = hScore > vScore ? h : v;

        var actual = FillHelpers.FillPattern(engine, group, angles, workArea);

        AssertSameLayout(expected, actual);
        AssertValidLayout(actual, workArea);
        AssertNoInputPartsReturned(group, actual);
        Assert.Equal(before, Snapshot(group));
        Assert.Equal(new[] { 0.0 }, angles);
        Assert.Equal(areaBefore, Bounds(workArea));
        Assert.Equal(areaBefore, Bounds(engine.WorkArea));
        Assert.Equal(0.25, engine.PartSpacing);
    }

    [Theory]
    [InlineData(5, 9)]
    [InlineData(10, 4)]
    [InlineData(5, 4)]
    [InlineData(7, 6)]
    public void FillPattern_CustomComparer_IsAuthoritativeAgainstCountAndDensity(
        double length, double width)
    {
        var group = MakeGroup();
        var before = Snapshot(group);
        var workArea = new Box(3, 5, length, width);
        var engine = new FillLinear(workArea, 0.25);
        var angles = new List<double> { 0 };
        var pattern = FillHelpers.BuildRotatedPattern(group, 0);
        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);
        var hScore = FillScore.Compute(h, workArea);
        var vScore = FillScore.Compute(v, workArea);
        Assert.NotEqual(hScore, vScore);
        var expected = hScore < vScore ? h : v;
        var comparer = new RecordingComparer((candidate, current, area) =>
            FillScore.Compute(candidate, area) < FillScore.Compute(current, area));

        var actual = FillHelpers.FillPattern(engine, group, angles, workArea, comparer);

        // Within one angle V is considered before H.
        // Pin both arguments and the call count, not just the eventual winning score.
        var call = Assert.Single(comparer.Calls);
        AssertSameLayout(h, call.Candidate);
        AssertSameLayout(v, call.Current);
        Assert.Same(workArea, call.WorkArea);
        Assert.Same(hScore < vScore ? call.Candidate : call.Current, actual);
        AssertSameLayout(expected, actual);
        Assert.True(FillScore.Compute(actual, workArea) < (hScore > vScore ? hScore : vScore));
        AssertValidLayout(actual, workArea);
        AssertNoInputPartsReturned(group, actual);
        Assert.Equal(before, Snapshot(group));
        Assert.Equal(new[] { 0.0 }, angles);
        Assert.Equal((3.0, 5.0, length, width), Bounds(workArea));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillPattern_TiedScores_KeepsVerticalBeforeHorizontalAndStillCallsCustomComparer(bool acceptCandidate)
    {
        var drawing = new RectangleShape { Length = 2, Width = 1 }.GetDrawing();
        var group = new List<Part> { new(drawing, new Vector(11, 13)) };
        var workArea = new Box(3, 5, 5, 4);
        var engine = new FillLinear(workArea, 0.25);
        var angles = new List<double> { 0 };
        var pattern = FillHelpers.BuildRotatedPattern(group, 0);
        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);
        Assert.Equal(6, h.Count);
        Assert.Equal(FillScore.Compute(h, workArea), FillScore.Compute(v, workArea));
        Assert.NotEqual(h[1].Location, v[1].Location);
        var comparer = new RecordingComparer((_, _, _) => acceptCandidate);

        var byScore = FillHelpers.FillPattern(engine, group, angles, workArea);
        var byComparer = FillHelpers.FillPattern(engine, group, angles, workArea, comparer);

        AssertSameLayout(v, byScore); // Strict > retains the earlier (V) result on a tie.
        var call = Assert.Single(comparer.Calls);
        AssertSameLayout(h, call.Candidate);
        AssertSameLayout(v, call.Current);
        Assert.Same(workArea, call.WorkArea);
        Assert.Same(acceptCandidate ? call.Candidate : call.Current, byComparer);
        AssertSameLayout(acceptCandidate ? h : v, byComparer);
        AssertValidLayout(byScore, workArea);
        AssertValidLayout(byComparer, workArea);
    }

    [Fact]
    public void FillPattern_ConsidersAnglesInInputOrderWhateverTheThreadOrder()
    {
        var drawing = new RectangleShape { Length = 2, Width = 1 }.GetDrawing();
        var group = new List<Part> { new(drawing, new Vector(11, 13)) };
        var workArea = new Box(3, 5, 5, 4);
        var engine = new FillLinear(workArea, 0.25);
        // Only the first angle's layouts keep rotation zero. A comparer that never prefers the
        // candidate keeps whichever result is considered first: the first angle's V.
        var angles = new List<double> { 0 };
        angles.AddRange(Enumerable.Repeat(System.Math.PI, 15));
        var expected = engine.Fill(FillHelpers.BuildRotatedPattern(group, 0), NestDirection.Vertical);
        var comparer = new RecordingComparer((_, _, _) => false);

        for (var run = 0; run < 50; run++)
            AssertSameLayout(expected, FillHelpers.FillPattern(engine, group, angles, workArea, comparer));
    }

    [Fact]
    public void FillPattern_RotatedGroup_PreservesDrawingIdentityPosesAndInputPrograms()
    {
        var group = MakeGroup();
        var before = Snapshot(group);
        var workArea = new Box(3, 5, 10, 9);
        var engine = new FillLinear(workArea, 0.25);
        var angle = System.Math.PI / 2;
        var angles = new List<double> { angle };
        var pattern = FillHelpers.BuildRotatedPattern(group, angle);
        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);
        var expected = FillScore.Compute(h, workArea) > FillScore.Compute(v, workArea) ? h : v;

        var actual = FillHelpers.FillPattern(engine, group, angles, workArea);

        Assert.NotEmpty(actual);
        AssertSameLayout(expected, actual);
        Assert.All(actual, part => Assert.Equal(angle, part.Rotation, 10));
        AssertValidLayout(actual, workArea);
        AssertNoInputPartsReturned(group, actual);
        Assert.Equal(before, Snapshot(group));
        Assert.Equal(new[] { angle }, angles);
    }

    [Theory]
    [InlineData("empty-angles", false)]
    [InlineData("empty-angles", true)]
    [InlineData("empty-group", false)]
    [InlineData("empty-group", true)]
    [InlineData("does-not-fit", false)]
    [InlineData("does-not-fit", true)]
    public void FillPattern_NoCandidates_ReturnsNullWithoutComparisonsOrScores(string scenario, bool custom)
    {
        var group = scenario == "empty-group" ? new List<Part>() : MakeGroup();
        var before = Snapshot(group);
        var angles = scenario == "empty-angles" ? new List<double>() : new List<double> { 0 };
        var anglesBefore = angles.ToArray();
        var workArea = scenario == "does-not-fit" ? new Box(3, 5, 1, 1) : new Box(3, 5, 5, 9);
        var areaBefore = Bounds(workArea);
        var engine = new FillLinear(workArea, 0.25);
        var comparer = new RecordingComparer((_, _, _) => true);
        PerfCounters.Reset();
        try
        {
            var result = FillHelpers.FillPattern(engine, group, angles, workArea, custom ? comparer : null);

            Assert.Null(result);
            Assert.Empty(comparer.Calls);
#if DEBUG
            Assert.Equal(0, PerfCounters.FillScoreComputations);
#endif
            Assert.Equal(before, Snapshot(group));
            Assert.Equal(anglesBefore, angles);
            Assert.Equal(areaBefore, Bounds(workArea));
        }
        finally
        {
            PerfCounters.Reset();
        }
    }

#if DEBUG
    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(0.37, 4)]
    public void BuildRotatedPattern_BoundsWork_RetainsRequiredCloneRecomputation(double angle, long expectedUpdates)
    {
        var group = MakeGroup();
        var expected = PreChangeRotatedPattern(group, angle);
        PerfCounters.Reset();
        try
        {
            var pattern = FillHelpers.BuildRotatedPattern(group, angle);
            // Removing the clone recompute changes accumulated-translation boxes at angle 0.
            // This site was retained, not optimized. Pattern.UpdateBounds aggregates boxes;
            // it does not call Part.UpdateBounds and remains outside this counter.
            Assert.Equal(expectedUpdates, PerfCounters.PartBoundsUpdates);
            AssertSameLayout(expected.Parts, pattern.Parts);
            Assert.Equal(Bounds(expected.BoundingBox), Bounds(pattern.BoundingBox));
        }
        finally
        {
            PerfCounters.Reset();
        }
    }

    [Theory]
    [InlineData(5, 9, 0, 2)]
    [InlineData(5, 9, 1, 0)]
    [InlineData(10, 4, 1, 0)]
    [InlineData(5, 9, 2, 2)]
    public void FillPattern_ScoreWork_OnlyDefaultPathOrComparerComputesScores(
        double length, double width, int comparerKind, long expectedComputations)
    {
        var group = MakeGroup();
        var workArea = new Box(3, 5, length, width);
        var engine = new FillLinear(workArea, 0.25);
        var angles = new List<double> { 0 };
        // Kind 1 does no scoring; kind 2 explicitly owns its two score computations.
        var comparer = new RecordingComparer((candidate, current, area) => comparerKind == 1
            ? candidate.Count < current.Count
            : FillScore.Compute(candidate, area) < FillScore.Compute(current, area));
        PerfCounters.Reset();
        try
        {
            var result = FillHelpers.FillPattern(engine, group, angles, workArea,
                comparerKind == 0 ? null : comparer);

            Assert.Equal(comparerKind == 0 ? 8 : 7, result.Count);
            Assert.Equal(comparerKind == 0 ? 0 : 1, comparer.Calls.Count);
            Assert.Equal(expectedComputations, PerfCounters.FillScoreComputations);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    private static List<Part> MakeGroup() => new()
    {
        new(new RectangleShape { Length = 2, Width = 1 }.GetDrawing(), new Vector(11, 13)),
        new(new RectangleShape { Length = 1, Width = 2 }.GetDrawing(), new Vector(13.5, 14.5)),
    };

    private static void AssertSameLayout(List<Part> expected, List<Part> actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i].BaseDrawing, actual[i].BaseDrawing);
            Assert.Equal((expected[i].Location.X, expected[i].Location.Y),
                (actual[i].Location.X, actual[i].Location.Y));
            Assert.Equal(expected[i].Rotation, actual[i].Rotation);
            Assert.Equal(Bounds(expected[i].BoundingBox), Bounds(actual[i].BoundingBox));
        }
    }

    private static void AssertNoInputPartsReturned(List<Part> input, List<Part> result)
    {
        Assert.NotSame(input, result);
        foreach (var part in result)
            Assert.DoesNotContain(input, original => ReferenceEquals(original, part));
    }

    private static void AssertValidLayout(List<Part> parts, Box area)
    {
        Assert.NotEmpty(parts);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            Assert.Equal(2.0, part.BaseDrawing.Area);
            Assert.True(area.Contains(part.BoundingBox));
            foreach (var value in new[] { part.Left, part.Right, part.Bottom, part.Top, part.Rotation })
                Assert.True(double.IsFinite(value));
            for (var j = 0; j < i; j++)
                Assert.False(part.BoundingBox.Intersects(parts[j].BoundingBox));
        }
    }

    private static (double X, double Y, double Length, double Width) Bounds(Box box) =>
        (box.X, box.Y, box.Length, box.Width);

    private static object[] Snapshot(List<Part> parts)
    {
        var values = new List<object>();
        foreach (var part in parts)
        {
            values.Add(part);
            values.Add(part.BaseDrawing);
            values.Add(part.BaseDrawing.Area);
            values.Add((part.Location.X, part.Location.Y));
            values.Add(part.Rotation);
            values.Add(Bounds(part.BoundingBox));
            foreach (var program in new[] { part.Program, part.BaseDrawing.Program })
            {
                values.Add(program);
                values.Add(program.Mode);
                values.Add(program.Rotation);
                foreach (var code in program.Codes)
                {
                    values.Add(code);
                    if (code is Motion motion)
                        values.Add((motion.EndPoint.X, motion.EndPoint.Y));
                    if (code is ArcMove arc)
                    {
                        values.Add((arc.CenterPoint.X, arc.CenterPoint.Y));
                        values.Add(arc.Rotation);
                        values.Add(arc.Layer);
                    }
                }
            }
        }
        return values.ToArray();
    }

    private sealed class RecordingComparer(Func<List<Part>, List<Part>, Box, bool> compare) : IFillComparer
    {
        public List<(List<Part> Candidate, List<Part> Current, Box WorkArea)> Calls { get; } = new();

        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea)
        {
            Calls.Add((candidate, current, workArea));
            return compare(candidate, current, workArea);
        }
    }
}

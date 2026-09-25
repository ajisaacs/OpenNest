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

        // One angle writes H then V on one worker's bag queue; enumeration is V then H.
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
    public void FillPattern_TiedScores_PreservesBagOrderAndStillCallsCustomComparer(bool acceptCandidate)
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

        AssertSameLayout(v, byScore); // Strict > retains the first bag result on a tie.
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
            Assert.Equal(expected[i].Location, actual[i].Location);
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
            values.Add(part.Location);
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
                        values.Add(motion.EndPoint);
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

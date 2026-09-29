using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class IncrementalOverlapAnalysisTests
{
    [Theory]
    [InlineData("move-onto")]
    [InlineData("move-away")]
    [InlineData("nudge-within")]
    [InlineData("rotate")]
    [InlineData("delete-first")]
    [InlineData("insert-first")]
    [InlineData("swap")]
    [InlineData("rename")]
    [InlineData("duplicate")]
    [InlineData("nothing")]
    public void IncrementalRecheckMatchesFullAnalysisExactly(string edit)
    {
        var drawing = Square("sq", 4);
        var holed = Holed("holed");
        var parts = new List<Part>
        {
            new(drawing, new Vector(0, 0)),
            new(drawing, new Vector(3, 0)),   // overlaps part 0
            new(holed, new Vector(20, 0)),
            new(drawing, new Vector(22, 2)),  // sits inside the hole: no overlap
            new(drawing, new Vector(40, 0)),
            new(drawing, new Vector(42, 1)),  // overlaps part 4
        };
        var cache = new OverlapMaterialCache();
        var previous = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        Assert.Equal(2, previous.Pairs.Count);

        switch (edit)
        {
            case "move-onto": parts[3].Offset(-1.5, 0); break;          // now hits hole material
            case "move-away": parts[1].Offset(10, 0); break;           // clears pair 0/1
            case "nudge-within": parts[5].Offset(0.25, 0.25); break;   // same pair, new area
            case "rotate": parts[1].Rotate(0.3, parts[1].Location); break;
            case "delete-first": parts.RemoveAt(0); break;             // renumbers every pair
            case "insert-first": parts.Insert(0, new Part(drawing, new Vector(41, 0))); break;
            case "swap": (parts[0], parts[1]) = (parts[1], parts[0]); break; // operand order flips
            case "rename": parts[0].BaseDrawing.Name = "renamed"; break;
            case "duplicate": parts.Add(new Part(drawing, new Vector(0, 0))); break; // coincident copy
        }

        var incremental = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache), previous);
        var oracle = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts));
        Assert.Equal(Describe(oracle), Describe(incremental));
    }

    [Fact]
    public void UnchangedPairsAreReusedAndOnlyTheMovedPartsNeighborsAreRecomputed()
    {
        var drawing = Square("sq", 4);
        var parts = new List<Part> { new(drawing, new Vector(0, 0)), new(drawing, new Vector(3, 0)),
            new(drawing, new Vector(40, 0)), new(drawing, new Vector(43, 0)) };
        var cache = new OverlapMaterialCache();
        var previous = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        parts[3].Offset(0.5, 0);
        var incremental = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache), previous);
        // The untouched pair's immutable geometry is shared; the moved pair is new.
        Assert.Same(previous.Pairs[0].Regions, incremental.Pairs[0].Regions);
        Assert.NotSame(previous.Pairs[1].Regions, incremental.Pairs[1].Regions);
        Assert.Equal(0.5 * 4, incremental.Pairs[1].Area, 9); // x 40..44 vs 43.5..47.5
    }

    [Fact]
    public void PairIssuesAreReusedAndRenumberedLikePairs()
    {
        // Two copies of a drawing at a pose whose pair clipping fails are reported as a pair issue;
        // an unrelated deletion before them must keep that issue, renumbered.
        var drawing = Square("sq", 4);
        var parts = new List<Part> { new(drawing, new Vector(100, 0)), new(drawing, new Vector(0, 0)),
            new(drawing, new Vector(2, 0)) };
        var cache = new OverlapMaterialCache();
        var previous = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        var issue = new PlateOverlapIssue(1, 2, "synthetic pair failure");
        var withIssue = WithIssue(previous, issue);
        parts.RemoveAt(0);
        var incremental = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache), withIssue);
        Assert.Equal(new PlateOverlapIssue(0, 1, "synthetic pair failure"), Assert.Single(incremental.Issues));
    }

    [Fact]
    public void BaselineFromAnotherCacheOrSnapshotIsNotReused()
    {
        var drawing = Square("sq", 4);
        var parts = new[] { new Part(drawing, new Vector(0, 0)), new Part(drawing, new Vector(3, 0)) };
        var previous = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, new OverlapMaterialCache()));
        var incremental = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, new OverlapMaterialCache()), previous);
        Assert.NotSame(previous.Pairs[0].Regions, incremental.Pairs[0].Regions);
        Assert.Equal(Describe(previous), Describe(incremental));
    }

    [Fact]
    public void CacheReusesPreparedMaterialUntilClearedOrTheProgramVisiblyChanges()
    {
        var drawing = Square("sq", 4);
        var parts = new[] { new Part(drawing, new Vector(0, 0)), new Part(drawing, new Vector(3, 0)) };
        var cache = new OverlapMaterialCache();
        var first = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        Assert.Equal(4, first.Pairs[0].Area, 9);

        // An in-place edit that keeps the code count is invisible to the cache: callers must Clear.
        drawing.Program.Codes[2] = new LinearMove(4, 8);
        drawing.Program.Codes[3] = new LinearMove(0, 8);
        Assert.Equal(4, PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache)).Pairs[0].Area, 9);
        cache.Clear();
        Assert.Equal(8, PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache)).Pairs[0].Area, 9);

        // A changed code count is detected without Clear (no stale reuse for obvious edits).
        drawing.Program.Codes.Clear();
        var broken = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        Assert.Empty(broken.Pairs);
        Assert.False(broken.IsComplete);
    }

    [Fact]
    public void CanceledPreparationIsNotCachedAsAFailure()
    {
        var drawing = Holed("holed");
        var parts = new[] { new Part(drawing, new Vector(0, 0)), new Part(drawing, new Vector(1, 0)) };
        var cache = new OverlapMaterialCache();
        var snapshot = PlateOverlapAnalyzer.Capture(parts, cache);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PlateOverlapAnalyzer.Analyze(snapshot, canceled.Token));
        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, cache));
        Assert.True(report.IsComplete);
        Assert.Single(report.Pairs);
    }

    // Same baseline snapshot, but its only result is a pair issue (real pair failures need
    // numeric edge cases); reuse must carry the issue rather than recompute a pair.
    private static PlateOverlapReport WithIssue(PlateOverlapReport report, PlateOverlapIssue issue) =>
        new(new List<PlateOverlapPair>(), new List<PlateOverlapIssue> { issue }, report.Snapshot);

    private static string Describe(PlateOverlapReport report) =>
        string.Join(";", report.Pairs.Select(pair =>
            $"{pair.PartAId}/{pair.PartBId}[{pair.PartAName}|{pair.PartBName}]:{pair.Area:R}:"
            + $"{pair.Centroid.X:R},{pair.Centroid.Y:R}:"
            + string.Join(",", pair.Regions.SelectMany(region => region.Vertices).Select(v => $"{v.X:R} {v.Y:R}"))))
        + "|" + string.Join(";", report.Issues.Select(issue => $"{issue.PartAId}/{issue.PartBId}:{issue.Message}"));

    private static Drawing Square(string name, double size) =>
        new(name, Program(new[] { new Vector(0, 0), new Vector(size, 0), new Vector(size, size), new Vector(0, size) }));

    private static Drawing Holed(string name) => new(name, Program(
        new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10) },
        new[] { new Vector(1, 1), new Vector(7, 1), new Vector(7, 7), new Vector(1, 7) }));

    private static Program Program(params Vector[][] contours)
    {
        var program = new Program(Mode.Absolute);
        foreach (var contour in contours)
        {
            program.Codes.Add(new RapidMove(contour[0]));
            foreach (var point in contour.Skip(1).Append(contour[0]))
                program.Codes.Add(new LinearMove(point));
        }
        return program;
    }
}

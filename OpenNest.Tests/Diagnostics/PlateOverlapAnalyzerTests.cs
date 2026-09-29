using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Diagnostics;

public class PlateOverlapAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsOwnedWorldPolygonsForEachOverlappingPair()
    {
        var parts = new[] { Rectangle(0, 0, 1, 1), Rectangle(0.5, 0, 1, 1) };

        var report = PlateOverlapAnalyzer.Analyze(parts);

        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Empty(report.Issues);
        var pair = Assert.Single(report.Pairs);
        Assert.Equal((0, 1), (pair.PartAId, pair.PartBId));
        Assert.Equal(0.5, pair.Area, 9);
        AssertCentroid(pair, 0.75, 0.5);
        Assert.NotEmpty(pair.Regions);
        Assert.All(pair.Regions, region =>
        {
            Assert.True(region.Area > 0);
            Assert.Equal(region.Vertices[0], region.Vertices[^1]);
            Assert.All(region.Vertices, point =>
            {
                Assert.InRange(point.X, 0.5, 1);
                Assert.InRange(point.Y, 0, 1);
            });
        });
        Assert.Equal(pair.Area, pair.Regions.Sum(r => r.Area), 9);
        Assert.Equal((0.5, 0.0, 1.0, 1.0),
            (pair.Bounds.Left, pair.Bounds.Bottom, pair.Bounds.Right, pair.Bounds.Top));
    }

    [Fact]
    public void Analyze_FullContainmentDoesNotRequireCrossingPoints()
    {
        var pair = Assert.Single(PlateOverlapAnalyzer.Analyze(new[]
        {
            Rectangle(0, 0, 4, 4), Rectangle(1, 1, 1, 1)
        }).Pairs);
        Assert.Equal(1, pair.Area, 9);
        AssertCentroid(pair, 1.5, 1.5);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void Analyze_DisjointAndBoundaryOnlyContactAreClear(double x, double y)
    {
        var report = PlateOverlapAnalyzer.Analyze(new[]
        {
            Rectangle(0, 0, 1, 1), Rectangle(x, y, 1, 1)
        });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_EmptyAndSinglePartAreComplete()
    {
        foreach (var parts in new[] { Array.Empty<Part>(), new[] { Rectangle(0, 0, 1, 1) } })
        {
            var report = PlateOverlapAnalyzer.Analyze(parts);
            Assert.True(report.IsComplete, string.Join("; ", report.Issues));
            Assert.Empty(report.Pairs);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_SubtractsHolesOfEitherOperand(bool swap)
    {
        var frame = WithContours(Square(0, 0, 4), Square(1, 1, 2));
        var insert = Rectangle(1.5, 1.5, 1, 1);
        var report = PlateOverlapAnalyzer.Analyze(swap ? new[] { insert, frame } : new[] { frame, insert });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Empty(report.Pairs);

        insert.Location = new Vector(0.5, 1.5);
        report = PlateOverlapAnalyzer.Analyze(swap ? new[] { insert, frame } : new[] { frame, insert });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(0.5, pair.Area, 9);
        AssertCentroid(pair, 0.75, 2);
    }

    [Fact]
    public void Analyze_SubtractsBothPartsHolesWithoutDuplicatingArea()
    {
        var a = WithContours(Square(0, 0, 4), Square(0.5, 0.5, 1));
        var b = WithContours(Square(0, 0, 4), Square(2, 2, 1));
        var report = PlateOverlapAnalyzer.Analyze(new[] { a, b });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(14, pair.Area, 8);
        AssertCentroid(pair, 28.5 / 14, 28.5 / 14);
    }

    [Fact]
    public void Analyze_ConcaveIntersectionKeepsDisconnectedFragments()
    {
        var u = WithContours(new[]
        {
            new Vector(0, 0), new Vector(3, 0), new Vector(3, 3), new Vector(2, 3),
            new Vector(2, 1), new Vector(1, 1), new Vector(1, 3), new Vector(0, 3)
        });
        var report = PlateOverlapAnalyzer.Analyze(new[] { u, Rectangle(0, 2, 3, 1) });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(2, pair.Area, 8);
        AssertCentroid(pair, 1.5, 2.5);
        Assert.All(pair.Regions, region => Assert.True(
            region.Vertices.All(p => p.X <= 1) || region.Vertices.All(p => p.X >= 2)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000000000, 1000000000)]
    [InlineData(-1000000000, -1000000000)]
    public void Analyze_ReversedWindingAndLargeTranslationPreserveArea(double x, double y)
    {
        var a = WithContours(Square(0, 0, 1).Reverse().ToArray());
        var b = Rectangle(0, 0, 1, 1);
        a.Location = new Vector(x, y);
        b.Location = new Vector(x + 0.5, y);
        var report = PlateOverlapAnalyzer.Analyze(new[] { a, b });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(0.5, pair.Area, 7);
        AssertCentroid(pair, x + 0.75, y + 0.5);
        Assert.Equal(x + 0.5, pair.Bounds.Left, 7);
        Assert.Equal(y + 1, pair.Bounds.Top, 7);
    }

    [Fact]
    public void Analyze_UsesBaselineAdjustedCleanDrawingPose()
    {
        var drawing = Rectangle(0, 0, 2, 1).BaseDrawing;
        drawing.Program.Rotate(System.Math.PI / 2);
        var part = new Part(drawing);
        part.Rotate(System.Math.PI / 2);
        part.Location = new Vector(10, 10);
        var report = PlateOverlapAnalyzer.Analyze(new[] { part, Rectangle(8, 9, 2, 1) });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(2, pair.Area, 8);
        AssertCentroid(pair, 9, 9.5);
    }

    [Fact]
    public void Analyze_CircularHoleIsNotSolidMaterial()
    {
        var ring = new Part(new RingShape { OuterDiameter = 10, InnerDiameter = 7 }.GetDrawing());
        var disk = new Part(new CircleShape { Diameter = 6 }.GetDrawing());
        ring.Location = new Vector(-36.8, 5.4);
        disk.Location = new Vector(-36.7, 5.4);
        var report = PlateOverlapAnalyzer.Analyze(new[] { ring, disk });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_KeepsInputIndicesAndDeterministicOrderWhileSkippingCutoffs()
    {
        var cutoff = Rectangle(0, 0, 1, 1);
        cutoff.BaseDrawing.IsCutOff = true;
        var drawing = Rectangle(0, 0, 1, 1).BaseDrawing;
        var parts = new[] { new Part(drawing), cutoff, new Part(drawing), new Part(drawing) };
        var report = PlateOverlapAnalyzer.Analyze(parts);
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Equal(new[] { (0, 2), (0, 3), (2, 3) },
            report.Pairs.Select(p => (p.PartAId, p.PartBId)));
        Assert.All(report.Pairs, p => Assert.Equal(1, p.Area, 9));
    }

    [Fact]
    public void Analyze_SweepMatchesAnalyticalExhaustiveRectanglePairs()
    {
        var random = new Random(928);
        var parts = Enumerable.Range(0, 40)
            .Select(_ => Rectangle(random.Next(-10, 10), random.Next(-10, 10), 3, 2)).ToArray();
        var expected = new List<(int, int, double)>();
        for (var a = 0; a < parts.Length; a++)
            for (var b = a + 1; b < parts.Length; b++)
            {
                var dx = System.Math.Min(parts[a].Right, parts[b].Right)
                    - System.Math.Max(parts[a].Left, parts[b].Left);
                var dy = System.Math.Min(parts[a].Top, parts[b].Top)
                    - System.Math.Max(parts[a].Bottom, parts[b].Bottom);
                if (dx > 0 && dy > 0)
                    expected.Add((a, b, dx * dy));
            }
        var report = PlateOverlapAnalyzer.Analyze(parts);
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Equal(expected.Select(pair => (pair.Item1, pair.Item2)),
            report.Pairs.Select(pair => (pair.PartAId, pair.PartBId)));
        foreach (var (expectedPair, actualPair) in expected.Zip(report.Pairs))
            Assert.Equal(expectedPair.Item3, actualPair.Area, 9);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("empty")]
    [InlineData("nan")]
    [InlineData("infinite-pose")]
    [InlineData("disjoint-outers")]
    [InlineData("nested-island")]
    [InlineData("crossing-holes")]
    [InlineData("self-crossing")]
    public void Analyze_InvalidPartMakesReportIncompleteButRetainsKnownOverlaps(string invalid)
    {
        var bad = Rectangle(0, 0, 1, 1);
        switch (invalid)
        {
            case "open": bad.BaseDrawing.Program.Codes.RemoveAt(bad.BaseDrawing.Program.Codes.Count - 1); break;
            case "empty": bad.BaseDrawing.Program.Codes.Clear(); break;
            case "nan": ((LinearMove)bad.BaseDrawing.Program.Codes[1]).EndPoint = new Vector(double.NaN, 0); break;
            case "infinite-pose": bad.Location = new Vector(double.PositiveInfinity, 0); break;
            case "disjoint-outers": bad = WithContours(Square(0, 0, 1), Square(3, 3, 1)); break;
            case "nested-island": bad = WithContours(Square(0, 0, 6), Square(1, 1, 4), Square(2, 2, 1)); break;
            case "crossing-holes": bad = WithContours(Square(0, 0, 6), Square(1, 1, 3), Square(2, 2, 3)); break;
            case "self-crossing": bad = WithContours(new[] { new Vector(0, 0), new Vector(3, 3), new Vector(0, 3), new Vector(2, 0) }); break;
        }
        var report = PlateOverlapAnalyzer.Analyze(new[] { Rectangle(0, 0, 1, 1), bad, Rectangle(0, 0, 1, 1) });
        Assert.False(report.IsComplete);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(1, issue.PartAId);
        Assert.Null(issue.PartBId);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal((0, 2), (pair.PartAId, pair.PartBId));
    }

    [Fact]
    public void Capture_IsIndependentOfLaterDrawingPoseAndCollectionChanges()
    {
        var a = Rectangle(0, 0, 1, 1);
        var b = new Part(a.BaseDrawing, new Vector(0.5, 0));
        var parts = new List<Part> { a, b };
        var snapshot = PlateOverlapAnalyzer.Capture(parts);
        a.BaseDrawing.Program.Codes.Clear();
        b.Location = new Vector(100, 100);
        parts.Clear();
        var first = PlateOverlapAnalyzer.Analyze(snapshot);
        var second = PlateOverlapAnalyzer.Analyze(snapshot);
        Assert.True(first.IsComplete);
        Assert.Equal(0.5, Assert.Single(first.Pairs).Area, 9);
        Assert.Equal(0.5, Assert.Single(second.Pairs).Area, 9);
    }

    [Fact]
    public void Analyze_IgnoresPlacedCuttingProgramAndDoesNotMutateParts()
    {
        var a = Rectangle(0, 0, 1, 1);
        var b = Rectangle(0.5, 0, 1, 1);
        Assert.True(a.RestoreLeadInProgram(Rectangle(100, 100, 4, 4).Program, locked: true));
        var program = a.Program;
        var source = a.BaseDrawing.Program;
        var sourceText = source.ToString();
        var placedText = program.ToString();
        var bounds = a.BoundingBox;
        var location = a.Location;
        var rotation = a.Rotation;
        var report = PlateOverlapAnalyzer.Analyze(new[] { a, b });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Equal(0.5, Assert.Single(report.Pairs).Area, 9);
        Assert.Same(program, a.Program);
        Assert.Same(source, a.BaseDrawing.Program);
        Assert.Equal(sourceText, source.ToString());
        Assert.Equal(placedText, program.ToString());
        Assert.Same(bounds, a.BoundingBox);
        Assert.Equal(location, a.Location);
        Assert.Equal(rotation, a.Rotation);
        Assert.True(a.HasManualLeadIns);
        Assert.True(a.LeadInsLocked);
    }

    [Fact]
    public void Analyze_IgnoresScribeRapidAndLeadPathsInCleanSource()
    {
        var a = Rectangle(0, 0, 1, 1);
        foreach (var layer in new[] { LayerType.Scribe, LayerType.Leadin, LayerType.Leadout })
        {
            a.BaseDrawing.Program.Codes.Add(new RapidMove(new Vector(0, 0)));
            a.BaseDrawing.Program.Codes.Add(new LinearMove(new Vector(10, 10)) { Layer = layer });
        }
        var report = PlateOverlapAnalyzer.Analyze(new[] { a, Rectangle(5, 5, 1, 1) });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_ReportCollectionsAndBoundsCannotBeMutated()
    {
        var report = PlateOverlapAnalyzer.Analyze(new[] { Rectangle(0, 0, 1, 1), Rectangle(0, 0, 1, 1) });
        var pair = Assert.Single(report.Pairs);
        Assert.Throws<NotSupportedException>(() => ((IList<PlateOverlapPair>)report.Pairs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PlateOverlapRegion>)pair.Regions).Clear());
        var region = pair.Regions[0];
        Assert.Throws<NotSupportedException>(() => ((IList<Vector>)region.Vertices)[0] = new Vector(99, 99));
        pair.Bounds.X = 99;
        Assert.Equal(0, pair.Bounds.X);
        var centroid = pair.Centroid;
        centroid.X = 99;
        centroid.Y = 99;
        AssertCentroid(pair, 0.5, 0.5);
    }

    [Fact]
    public async Task Capture_SharedHoleSubprogramsAreOwnedAndReusableAcrossWorkers()
    {
        var source = Rectangle(0, 0, 10, 10).BaseDrawing;
        var hole = Rectangle(0, 0, 1, 1).BaseDrawing.Program;
        hole.Mode = Mode.Incremental;
        source.Program.SubPrograms[-1] = hole;
        source.Program.Codes.Add(new SubProgramCall { Id = -1, Program = hole, Offset = new Vector(2, 2) });
        source.Program.Codes.Add(new SubProgramCall { Id = -1, Program = hole, Offset = new Vector(6, 2) });
        source.Program.Rotate(System.Math.PI / 2);
        var placed = new Part(source);
        placed.Rotate(System.Math.PI / 2);
        placed.Location = new Vector(20, 20);
        var parts = new[] { placed, Rectangle(10, 10, 10, 10) };
        var before = source.Program.Codes.Select(code => code.ToString()).ToArray();
        var holeBefore = hole.Codes.Select(code => code.ToString()).ToArray();
        var snapshot = PlateOverlapAnalyzer.Capture(parts);
        Assert.Equal(before, source.Program.Codes.Select(code => code.ToString()));
        Assert.Equal(holeBefore, hole.Codes.Select(code => code.ToString()));
        foreach (var call in source.Program.Codes.OfType<SubProgramCall>())
            call.Offset = new Vector(100, 100);
        hole.Codes.Clear();
        source.Program.Codes.Clear();
        var reports = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => PlateOverlapAnalyzer.Analyze(snapshot))));
        foreach (var report in reports)
        {
            Assert.True(report.IsComplete, string.Join("; ", report.Issues));
            Assert.Equal(98, Assert.Single(report.Pairs).Area, 8);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_InvalidSubprogramGraphIsIncompleteRatherThanRecursive(bool missing)
    {
        var part = Rectangle(0, 0, 1, 1);
        var call = new SubProgramCall();
        if (!missing)
            call.Program = part.BaseDrawing.Program;
        part.BaseDrawing.Program.Codes.Add(call);
        var report = PlateOverlapAnalyzer.Analyze(new[] { part });
        Assert.False(report.IsComplete);
        Assert.Single(report.Issues);
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_NullArgumentsThrowAndNullEntriesAreIssues()
    {
        Assert.Throws<ArgumentNullException>(() => PlateOverlapAnalyzer.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => PlateOverlapAnalyzer.Analyze((PlateOverlapSnapshot)null!));
        var report = PlateOverlapAnalyzer.Analyze(new Part[] { null! });
        Assert.False(report.IsComplete);
        Assert.Single(report.Issues);
        Assert.Empty(report.Pairs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_HugeOuterWithSmallClockwisePartIsOrderIndependent(bool swap)
    {
        var outer = Rectangle(0, 0, 1e9, 1e9);
        var small = WithContours(Square(0, 0, 1).Reverse().ToArray());
        small.Location = new Vector(999999998, 999999998);
        var report = PlateOverlapAnalyzer.Analyze(swap ? new[] { small, outer } : new[] { outer, small });
        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(1, pair.Area, 8);
        AssertCentroid(pair, 999999998.5, 999999998.5);
    }

    [Fact]
    public void Analyze_TranslationThatCollapsesMaterialIsIncompleteNotClear()
    {
        var report = PlateOverlapAnalyzer.Analyze(new[]
        {
            Rectangle(1e16, 1e16, 1, 1), Rectangle(1e16, 1e16, 1, 1)
        });
        Assert.False(report.IsComplete);
        Assert.Equal(new[] { 0, 1 }, report.Issues.Select(issue => issue.PartAId));
        Assert.Empty(report.Pairs);
    }

    [Theory]
    [InlineData(9.0)]
    [InlineData(9.0002)]
    public void Analyze_NativeCircularHoleContactCannotBeHiddenByTessellation(double y)
    {
        var source = Rectangle(0, 0, 10, 10).BaseDrawing;
        source.Program.Codes.Add(new RapidMove(new Vector(6, y)));
        source.Program.Codes.Add(new ArcMove(new Vector(6, y), new Vector(5, y), RotationType.CCW));
        var report = PlateOverlapAnalyzer.Analyze(new[] { new Part(source) });
        Assert.False(report.IsComplete);
        Assert.Single(report.Issues);
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_SubChordThinRingIsExplicitlyUncheckableNotClear()
    {
        const double radius = 4.995098381203606;
        var ring = new Part(new RingShape
        {
            OuterDiameter = 2 * (radius + 0.0001),
            InnerDiameter = 2 * (radius - 0.0001)
        }.GetDrawing());
        var report = PlateOverlapAnalyzer.Analyze(new[] { ring });
        Assert.False(report.IsComplete);
        Assert.Single(report.Issues);
        Assert.Empty(report.Pairs);
    }

    [Fact]
    public void Analyze_CancellationThrowsInsteadOfPublishingPartialClear()
    {
        var parts = new[] { Rectangle(0, 0, 1, 1), Rectangle(0, 0, 1, 1) };
        var snapshot = PlateOverlapAnalyzer.Capture(parts);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PlateOverlapAnalyzer.Capture(parts, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => PlateOverlapAnalyzer.Analyze(snapshot, cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000000000)]
    [InlineData(-1000000000)]
    public void Analyze_UnequalDisconnectedOverlapWeightsAllFragments(double offset)
    {
        var u = WithContours(new[]
        {
            new Vector(0, 0), new Vector(6, 0), new Vector(6, 3), new Vector(3, 3),
            new Vector(3, 1), new Vector(1, 1), new Vector(1, 3), new Vector(0, 3)
        });
        u.Location = new Vector(offset, offset);

        var report = PlateOverlapAnalyzer.Analyze(new[] { u, Rectangle(offset, offset + 2, 6, 1) });

        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(4, pair.Area, 8);
        AssertCentroid(pair, offset + 3.5, offset + 2.5);
    }

    [Fact]
    public void Analyze_RotatedTriangleHasTrueMaterialCentroid()
    {
        var triangle = WithContours(new[] { new Vector(0, 0), new Vector(6, 0), new Vector(0, 3) });
        triangle.Rotate(System.Math.PI / 2);
        triangle.Location = new Vector(10, 20);

        var report = PlateOverlapAnalyzer.Analyze(new[] { Rectangle(0, 0, 30, 30), triangle });

        Assert.True(report.IsComplete, string.Join("; ", report.Issues));
        var pair = Assert.Single(report.Pairs);
        Assert.Equal(9, pair.Area, 8);
        AssertCentroid(pair, 9, 22);
    }

    [Fact]
    public void Analyze_OverflowingPairMomentsAreIncompleteAndRetainOtherPairs()
    {
        var huge = Rectangle(0, 0, 1e103, 1e103);
        var report = PlateOverlapAnalyzer.Analyze(new[]
        {
            huge, new Part(huge.BaseDrawing), Rectangle(-2, -2, 1, 1), Rectangle(-2, -2, 1, 1)
        });

        Assert.False(report.IsComplete);
        var issue = Assert.Single(report.Issues);
        Assert.Equal((0, (int?)1), (issue.PartAId, issue.PartBId));
        Assert.Contains("moment", issue.Message, StringComparison.OrdinalIgnoreCase);
        var valid = Assert.Single(report.Pairs);
        Assert.Equal((2, 3), (valid.PartAId, valid.PartBId));
        AssertCentroid(valid, -1.5, -1.5);
    }

    private static void AssertCentroid(PlateOverlapPair pair, double x, double y)
    {
        Assert.True(double.IsFinite(pair.Centroid.X));
        Assert.True(double.IsFinite(pair.Centroid.Y));
        Assert.Equal(x, pair.Centroid.X, 7);
        Assert.Equal(y, pair.Centroid.Y, 7);
    }

    private static Part Rectangle(double x, double y, double width, double height) =>
        new(WithContours(new[] { new Vector(0, 0), new Vector(width, 0),
            new Vector(width, height), new Vector(0, height) }).BaseDrawing, new Vector(x, y));

    private static Vector[] Square(double x, double y, double size) =>
        new[] { new Vector(x, y), new Vector(x + size, y), new Vector(x + size, y + size), new Vector(x, y + size) };

    private static Part WithContours(params Vector[][] contours)
    {
        var program = new Program(Mode.Absolute);
        foreach (var contour in contours)
        {
            program.Codes.Add(new RapidMove(contour[0]));
            foreach (var point in contour.Skip(1).Append(contour[0]))
                program.Codes.Add(new LinearMove(point));
        }
        return new Part(new Drawing("same name", program));
    }
}

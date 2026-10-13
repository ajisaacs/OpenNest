using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Diagnostics;

/// <summary>
/// Slice 4 (C1): the material-overlap analyzer reports part-vs-clamp findings like
/// other overlap findings - owned fragments, area and centroid - and they flow into
/// hover text and paging. Files/plates without zones behave exactly as before.
/// </summary>
public class PlateOverlapAnalyzerClampZoneTests
{
    private static Program Rect(double lengthX, double widthY)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(lengthX, 0);
        program.LineTo(lengthX, widthY);
        program.LineTo(0, widthY);
        program.LineTo(0, 0);
        return program;
    }

    private static Drawing Square(string name, double size) => new(name, Rect(size, size));

    private static readonly ClampZone Front1 = new("front-1", new Box(10, 0, 1.5, 0.625));
    private static readonly ClampZone Front2 = new("front-2", new Box(20, 0, 1.5, 0.625));

    [Fact]
    public void ManualCutoffCrossingClampAppearsInOverlapCheckWithoutBeingMaterial()
    {
        var plate = new Plate(new Size(24, 48));
        plate.ClampZones.Add(Front1);
        plate.CutOffs.Add(new CutOff(new Vector(10.5, 0), CutOffAxis.Vertical));
        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));
        Assert.Empty(report.Pairs);
        Assert.Empty(report.ClampFindings);
        Assert.True(report.IsComplete);
        var finding = Assert.Single(report.CutOffClampFindings);
        Assert.Equal("front-1", finding.ClampId);
        Assert.Equal(0, finding.CutOffIndex);
        Assert.InRange(finding.Location.X, 10, 11.5);
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, report));
        Assert.Contains("cut-off", state.Message);
        plate.CutOffs.Clear();
        Assert.False(state.EnsureFresh(plate));
        Assert.Equal(OverlapCheckStatus.Stale, state.Status);
    }

    [Fact]
    public void PartOverlappingClampProducesHoleAwareFinding()
    {
        // 4x4 square at (9.25, 0): the shared fragment with front-1 is exactly its footprint.
        var parts = new List<Part> { new(Square("plate-part", 4), new Vector(9.25, 0)) };

        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { Front1, Front2 }));

        Assert.True(report.IsComplete, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        var finding = Assert.Single(report.ClampFindings);
        Assert.Equal(0, finding.PartAId);
        Assert.Equal("plate-part", finding.PartAName);
        Assert.Contains("front-1", finding.PartBName);
        Assert.Equal(1.5 * 0.625, finding.Area, 3);
        Assert.InRange(finding.Centroid.X, 10 - 1e-3, 11.5 + 1e-3);
        Assert.InRange(finding.Centroid.Y, 0 - 1e-3, 0.625 + 1e-3);
    }

    [Fact]
    public void ClearPartsProduceNoClampFindings()
    {
        var parts = new List<Part>
        {
            new(Square("far", 4), new Vector(30, 10)),
            new(Square("behind", 2), new Vector(10, 5)), // between the clamps, above them
        };

        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { Front1, Front2 }));

        Assert.True(report.IsComplete);
        Assert.Empty(report.Pairs);
        Assert.Empty(report.ClampFindings);
    }

    [Fact]
    public void LegacyCaptureFindsNoClampsAndKeepsPartPairs()
    {
        var parts = new List<Part>
        {
            new(Square("a", 4), new Vector(0, 0)),
            new(Square("b", 4), new Vector(3, 0)), // overlaps a
        };

        var legacy = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts));
        Assert.Single(legacy.Pairs);
        Assert.Empty(legacy.ClampFindings);

        var explicitEmpty = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, System.Array.Empty<ClampZone>()));
        Assert.Equal(Describe(legacy), Describe(explicitEmpty));
    }

    [Fact]
    public void IncrementalRecheckAfterMovingAClampMatchesAFullAnalysis()
    {
        var parts = new List<Part>
        {
            new(Square("a", 4), new Vector(9.25, 0)),  // hits the zone at X 10
            new(Square("b", 4), new Vector(30, 10)),   // clear of the zone at X 10
        };
        var moved = new ClampZone("front-1", new Box(29, 10, 1.5, 0.625)); // now hits b

        var previous = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { Front1 }));
        Assert.Equal(0, Assert.Single(previous.ClampFindings).PartAId);

        var incremental = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { moved }), previous);
        var oracle = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { moved }));

        Assert.Equal(Describe(oracle), Describe(incremental));
        Assert.Equal(1, Assert.Single(incremental.ClampFindings).PartAId);
    }

    [Fact]
    public void FindingsRenderAsClampDetailsAndPageLikeOtherFindings()
    {
        var parts = new List<Part>
        {
            new(Square("hit-one", 4), new Vector(9.25, 0)),
            new(Square("hit-two", 4), new Vector(19.25, 0)),
            new(Square("also-hit-two", 4), new Vector(20.5, 0.3)),
        };

        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { Front1, Front2 }));

        Assert.Equal(3, report.ClampFindings.Count); // hit-one/1, hit-two/2, hit-two/3 -> two zones each
        var finding = report.ClampFindings.First(f => f.PartAName == "hit-one");
        Assert.True(OverlapPairPresentation.IsClampFinding(finding));
        var details = OverlapPairPresentation.Details(finding, Units.Inches, CultureInfo.InvariantCulture);
        Assert.Contains("front-1", details);
        Assert.Contains("hit-one", details);
        Assert.DoesNotContain("Pair ", details);

        // Hover text joins every finding; paging must reach all of them.
        var text = string.Join("\n\n", report.ClampFindings.Select(f =>
            OverlapPairPresentation.Details(f, Units.Inches, CultureInfo.InvariantCulture)));
        var pages = OverlapHoverPages.Create(text, report.ClampFindings.Count, 6, 60, s => s.Length);
        Assert.True(pages.PageCount > 1);
        var all = string.Concat(Enumerable.Range(0, pages.PageCount)
            .Select(page => { pages.MovePage(page == 0 ? 0 : 1); return string.Concat(pages.Lines); }));
        Assert.Contains("front-2", all);
        Assert.Contains("also-hit-two", all);
    }

    [Fact]
    public void PublishedClampFindingIsVisibleAndZoneEditInvalidatesIt()
    {
        var plate = new Plate(new Size(24, 48));
        plate.Parts.Add(new Part(Square("hit", 4), new Vector(9.25, 0)));
        plate.ClampZones.Add(Front1);
        var state = new OverlapReportState();
        var generation = state.Begin(plate);
        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(plate.Parts.ToArray(), plate.ClampZones.ToArray()));
        Assert.True(state.TryPublish(generation, plate, report));
        Assert.Single(state.DisplayPairs);
        Assert.Contains("clamp", state.Message, System.StringComparison.OrdinalIgnoreCase);
        plate.ClampZones.Clear();
        state.Invalidate(plate);
        Assert.Empty(state.DisplayPairs);
        Assert.Equal(OverlapCheckStatus.Stale, state.Status);
    }

    [Fact]
    public void PublicationRefusesZonesChangedWhileWorkerWasRunning()
    {
        var plate = new Plate(new Size(24, 48));
        plate.Parts.Add(new Part(Square("hit", 4), new Vector(9.25, 0)));
        plate.ClampZones.Add(Front1);
        var state = new OverlapReportState();
        var generation = state.Begin(plate);
        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(plate.Parts.ToArray(), plate.ClampZones.ToArray()));
        plate.ClampZones.Clear();
        Assert.False(state.TryPublish(generation, plate, report));
        Assert.NotEqual(OverlapCheckStatus.Current, state.Status);
    }

    // C16 (PM 6859db53): a 1e-5 square zone, far below the outline tolerance. A part covering
    // it is still reported (never silently clear); flush or clear parts are not.
    [Theory]
    [InlineData(7.5, true)]
    [InlineData(6.0, false)]
    [InlineData(8.00001, false)]
    [InlineData(3.0, false)]
    public void TinyClampZoneIsJudgedWithoutOutlineCollapse(double x, bool overlaps)
    {
        var tiny = new ClampZone("tiny", new Box(8, 0, 1e-5, 1e-5));
        var parts = new List<Part> { new(Square("p", 2), new Vector(x, 0)) };

        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(parts, new[] { tiny }));

        Assert.True(report.IsComplete, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        Assert.Equal(overlaps, report.ClampFindings.Any(f => f.PartBName.Contains("tiny")));
    }

    // PM 407f5eb7: real material, not the 0.001 arc flattening, decides a tiny clamp.
    [Theory]
    [InlineData("rect", 8.00002, 0.5, true)]
    [InlineData("rect", 8.00005, 0.5, false)]
    [InlineData("disk", 1.9998, 0, true)]
    [InlineData("disk", 2.0002, 0, false)]
    [InlineData("disk-box", 11.935615871667656, 10.503377293956166, true)] // arc cuts a corner
    public void TinyClampIsJudgedAgainstRealMaterial(string material, double a, double b, bool overlaps)
    {
        Program program;
        Box zone;
        if (material == "rect")
        {
            program = Rect(8.00004, 2);
            zone = new Box(a, b, 1e-5, 1e-5);
        }
        else
        {
            program = new Program();
            program.MoveTo(12, 10);
            program.ArcTo(8, 10, 10, 10, RotationType.CCW);
            program.ArcTo(12, 10, 10, 10, RotationType.CCW);
            if (material == "disk-box")
            {
                zone = new Box(a, b, 1e-5, 1e-5);
            }
            else
            {
                var cx = 10 + a * System.Math.Cos(0.02);
                var cy = 10 + a * System.Math.Sin(0.02);
                zone = new Box(cx - 0.000005, cy - 0.000005, 1e-5, 1e-5);
            }
        }
        var parts = new List<Part> { new(new Drawing(material, program), new Vector(0, 0)) };

        var report = PlateOverlapAnalyzer.Analyze(
            PlateOverlapAnalyzer.Capture(parts, new[] { new ClampZone("tiny", zone) }));

        Assert.True(report.IsComplete, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        Assert.Equal(overlaps, report.ClampFindings.Count > 0);
    }

    // Delta review 2: material beside a concave (notch) arc is not lost for a tiny clamp.
    [Fact]
    public void TinyClampBesideAConcaveArcIsReported()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(20, 0);
        program.LineTo(20, 20);
        program.LineTo(12, 20);
        program.ArcTo(8, 20, 10, 20, RotationType.CW);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        var arc = new ShapeProfile(OpenNest.Converters.ConvertProgram.ToGeometry(program))
            .Perimeter.Entities.OfType<Arc>().Single();
        var native = arc.ToPoints(arc.SegmentsForTolerance(1e-5 / 4)).First(v => v.X > 11.9 && v.Y < 19.5);
        var centre = new Vector(10, 20);
        var outward = centre + (native - centre) / 2 * (2 + 6e-7);
        var zone = new ClampZone("tiny", new Box(outward.X - 1e-5, outward.Y, 1e-5, 1e-5));

        var report = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.Capture(
            new List<Part> { new(new Drawing("notch", program), new Vector(0, 0)) }, new[] { zone }));

        Assert.True(report.IsComplete, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        Assert.NotEmpty(report.ClampFindings);
    }

    private static string Describe(PlateOverlapReport report) =>
        string.Join(";", report.Pairs.Select(p => $"{p.PartAId}/{p.PartBId}:{p.Area:R}"))
        + "|"
        + string.Join(";", report.ClampFindings.Select(f => $"{f.PartAId}/{f.PartBName}:{f.Area:R}"));
}

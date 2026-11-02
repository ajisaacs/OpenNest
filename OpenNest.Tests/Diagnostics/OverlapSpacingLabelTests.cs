using System.Globalization;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using static OpenNest.Tests.TestHelpers;

namespace OpenNest.Tests.Diagnostics;

public class OverlapSpacingLabelTests
{
    private const double Spacing = 0.25;

    [Fact]
    public void GapUnderSpacing_IsCountedWithoutAnOverlap()
    {
        var plate = PlateWith(Spacing, Square(0, "a"), Square(4.1, "b"));

        var report = Analyze(plate);

        Assert.Empty(report.Pairs);
        var pair = Assert.Single(report.SpacingPairs);
        Assert.Equal((0, 1, "a", "b"), (pair.PartAId, pair.PartBId, pair.PartAName, pair.PartBName));
        Assert.Equal(0.1, pair.Gap, 6);
        Assert.Equal(4.05, pair.Marker.X, 3);
        Assert.Equal("No material overlaps; part spacing: 1 pairs too close", Label(plate));
    }

    [Fact]
    public void ClearPlate_SaysSpacingWasChecked()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.5));

        Assert.Equal("No material overlaps or part spacing violations detected", Label(plate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void SpacingNotEnforced_ClaimsOnlyOverlaps(double spacing)
    {
        var plate = PlateWith(spacing, Square(0), Square(4));

        Assert.False(Analyze(plate).SpacingChecked);
        Assert.Equal("No material overlaps detected", Label(plate));
    }

    [Fact]
    public void BareGroupOfParts_HasNoSpacingToCheck()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1));

        var report = PlateOverlapAnalyzer.Analyze(plate.Parts.ToArray());

        Assert.False(report.SpacingChecked);
        Assert.Empty(report.SpacingPairs);
    }

    [Fact]
    public void OverlappingPair_IsNotAlsoASpacingPair()
    {
        var plate = PlateWith(Spacing, Square(0), Square(3), Square(7.1));

        var report = Analyze(plate);

        Assert.Single(report.Pairs);
        Assert.Equal((1, 2), (report.SpacingPairs[0].PartAId, report.SpacingPairs[0].PartBId));
        Assert.Equal("Overlaps: 1 pairs; part spacing: 1 pairs too close", Label(plate));
    }

    [Fact]
    public void CutOffs_AreSkippedAndKeepInputIds()
    {
        var line = new Program();
        line.MoveTo(0, 4.05);
        line.LineTo(4, 4.05);
        var plate = PlateWith(Spacing, new Part(new Drawing("cutoff", line) { IsCutOff = true }),
            Square(0), Square(4.1));

        var pair = Assert.Single(Analyze(plate).SpacingPairs);

        Assert.Equal((1, 2), (pair.PartAId, pair.PartBId));
    }

    [Fact]
    public void UnreadablePart_LeavesTheLabelIncomplete()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1), new Part(new Drawing("invalid", new Program())));

        var report = Analyze(plate);

        Assert.False(report.IsComplete);
        Assert.Single(report.SpacingPairs);
        Assert.Contains("1 pairs under part spacing; 1 parts could not be checked", Label(plate));
    }

    [Fact]
    public void MatchesThePrePostReview()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.PartSpacing = Spacing;
        foreach (var part in new[] { Square(0, "a"), Square(4.1, "b"), Square(8.3, "c"), Square(13, "d") })
            plate.Parts.Add(part);

        var label = Analyze(plate).SpacingPairs.Select(pair => (pair.PartAId + 1, pair.PartBId + 1));
        var review = PostVerificationAnalyzer.Analyze(nest).Findings
            .Where(finding => finding.Kind == PostVerificationKind.PartSpacing)
            .Select(finding => (finding.PartNumber!.Value, finding.OtherPartNumber!.Value));

        Assert.Equal(review, label);
    }

    [Fact]
    public void MovedPart_IsRecheckedAgainstThePreviousReport()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1));
        var cache = new OverlapMaterialCache();
        var first = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate, cache));
        Assert.Single(first.SpacingPairs);

        plate.Parts[1].Offset(1, 0);
        var second = PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate, cache), first);

        Assert.Empty(second.SpacingPairs);
    }

    [Fact]
    public void ChangingPartSpacing_MakesTheReportStale()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.5));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));

        plate.PartSpacing = 1;

        Assert.False(state.EnsureFresh(plate));
        Assert.Equal(OverlapCheckStatus.Stale, state.Status);
    }

    [Fact]
    public void Markers_KeepOnlyPairsOfUnchangedPartsUntilTheRecheck()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1), Square(20), Square(24.1));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        Assert.Equal(2, state.DisplaySpacingPairs.Count);

        plate.Parts[3].Offset(1, 0);
        Assert.False(state.EnsureFresh(plate));

        var kept = Assert.Single(state.DisplaySpacingPairs);
        Assert.Equal((0, 1), (kept.PartAId, kept.PartBId));
    }

    [Fact]
    public void Markers_ClearWhenPartSpacingChanges()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));

        plate.PartSpacing = 0.05;
        state.EnsureFresh(plate);

        Assert.Empty(state.DisplaySpacingPairs);
    }

    [Theory]
    [InlineData(4.5, 0.0, Spacing, true)] // clear
    [InlineData(4.0, 0.0, 0.0, true)] // contact with nothing enforced
    [InlineData(4.1, 0.0, Spacing, false)] // too close
    [InlineData(3.0, 0.0, Spacing, false)] // overlap
    public void AllClear_RequiresNoFindings(double x, double y, double spacing, bool clear)
    {
        var second = Square(x);
        second.Offset(0, y);
        var plate = PlateWith(spacing, Square(0), second);
        var state = new OverlapReportState();
        Assert.False(state.IsAllClear);

        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));

        Assert.Equal(clear, state.IsAllClear);
    }

    [Fact]
    public void AllClear_IsNeverShownForAnIncompleteOrStaleCheck()
    {
        var plate = PlateWith(Spacing, Square(0), new Part(new Drawing("invalid", new Program())));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        Assert.False(state.IsAllClear);

        plate = PlateWith(Spacing, Square(0), Square(4.5));
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        Assert.True(state.IsAllClear);
        plate.Parts[1].Offset(-0.4, 0);
        state.EnsureFresh(plate);
        Assert.False(state.IsAllClear);
    }

    [Fact]
    public void SettledResult_SurvivesEditsAndRechecksButNotFailureOrCancel()
    {
        var plate = PlateWith(Spacing, Square(0), Square(4.1));
        var state = new OverlapReportState();
        Assert.Null(state.SettledMessage);
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        const string message = "No material overlaps; part spacing: 1 pairs too close";

        plate.Parts[1].Offset(1, 0);
        state.EnsureFresh(plate);
        Assert.Equal((message, false), (state.SettledMessage, state.SettledClear));
        var request = state.Begin(plate);
        Assert.Equal(message, state.SettledMessage);

        Assert.True(state.TryPublish(request, plate, Analyze(plate)));
        Assert.True(state.SettledClear);

        Assert.True(state.TryFail(state.Begin(plate), plate));
        Assert.Null(state.SettledMessage);
        Assert.False(state.SettledClear);
    }

    [Theory]
    [InlineData(4.5, "Layout: OK", false)]
    [InlineData(4.1, "Layout: 1 too close", true)]
    [InlineData(3.0, "Layout: 1 overlap", true)]
    public void Summary_StaysShort(double x, string expected, bool attention)
    {
        var plate = PlateWith(Spacing, Square(0), Square(x));
        var state = new OverlapReportState();
        Assert.Equal("Layout: not checked", state.Summary);
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));

        Assert.Equal(expected, state.Summary);
        Assert.Equal(attention, state.NeedsAttention);
    }

    [Fact]
    public void Summary_CountsOverlapsAndSpacingTogether()
    {
        var plate = PlateWith(Spacing, Square(0), Square(3), Square(3.5), Square(7.6));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));

        Assert.Equal("Layout: 3 overlaps, 1 too close", state.Summary);
    }

    [Fact]
    public void Summary_FlagsAnUnfinishedCheck()
    {
        var plate = PlateWith(Spacing, Square(0), new Part(new Drawing("invalid", new Program())));
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        Assert.Equal("Layout: check incomplete", state.Summary);
        Assert.True(state.NeedsAttention);

        Assert.True(state.TryFail(state.Begin(plate), plate));
        Assert.Equal("Layout: check failed", state.Summary);
        Assert.True(state.NeedsAttention);
    }

    [Fact]
    public void Presentation_ShowsPositionsNamesAndGap()
    {
        var pair = new PlateSpacingPair(1, 2, "a", "b", 0.1, new Vector(4.05, 2));

        Assert.Equal("2/3 gap 0.1", OverlapPairPresentation.Label(pair, CultureInfo.InvariantCulture));
        Assert.Equal("Part spacing 2/3: a / b\nGap ≈ 0.1 in\nMarker: (4.05, 2) in",
            OverlapPairPresentation.Details(pair, Units.Inches, CultureInfo.InvariantCulture));
    }

    private static string Label(Plate plate)
    {
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        return state.Message;
    }

    private static PlateOverlapReport Analyze(Plate plate) =>
        PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));

    private static Plate PlateWith(double spacing, params Part[] parts)
    {
        var plate = new Plate { PartSpacing = spacing };
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static Part Square(double x, string name = "square") =>
        new(new Drawing(name, MakeSquareProgram(0, 0, 4)), new Vector(x, 0));
}

using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class OverlapReportStateTests
{
    [Fact]
    public void RestartRejectsOldSuccessAndFailureWithoutDisturbingNewRequest()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var old = state.Begin(plate);
        var current = state.Begin(plate);
        Assert.False(state.TryPublish(old, plate, Analyze(plate)));
        Assert.False(state.TryFail(old, plate));
        Assert.Equal(OverlapCheckStatus.Checking, state.Status);
        Assert.True(state.TryPublish(current, plate, Analyze(plate)));
        Assert.Equal(OverlapCheckStatus.Current, state.Status);
        Assert.Equal("Overlaps: 1 pairs", state.Message);
    }

    [Theory]
    [InlineData("move-x")]
    [InlineData("move-y")]
    [InlineData("rotate")]
    [InlineData("reorder")]
    [InlineData("replace-part")]
    [InlineData("replace-drawing")]
    [InlineData("placed-program")]
    [InlineData("clean-program")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("plate")]
    public void ExactOrderedStampRejectsEditsBeforePublishAndAfterCompletion(string edit)
    {
        foreach (var publishFirst in new[] { false, true })
        {
            var plate = PlateWithParts();
            var state = new OverlapReportState();
            var request = state.Begin(plate);
            var report = Analyze(plate);
            if (publishFirst)
                Assert.True(state.TryPublish(request, plate, report));
            var part = plate.Parts[0];
            switch (edit)
            {
                case "move-x": part.Offset(1e-10, 0); break;
                case "move-y": part.Offset(0, 1e-10); break;
                case "rotate": part.Rotate(0.01); break;
                case "reorder": (plate.Parts[0], plate.Parts[1]) = (plate.Parts[1], plate.Parts[0]); break;
                case "replace-part": plate.Parts[0] = part.CloneAtOffset(new Vector()); break;
                case "replace-drawing": plate.Parts[0] = Rectangle(); break;
                case "placed-program": part.Update(); break;
                case "clean-program": part.BaseDrawing.Program = (Program)part.BaseDrawing.Program.Clone(); break;
                case "add": plate.Parts.Add(Rectangle()); break;
                case "remove": plate.Parts.RemoveAt(0); break;
                case "plate":
                    var replacement = new Plate();
                    replacement.Parts.AddRange(plate.Parts);
                    plate = replacement;
                    break;
            }
            Assert.False(state.EnsureFresh(plate));
            Assert.False(state.TryPublish(request, plate, report));
            Assert.Null(state.Report);
            Assert.Equal(OverlapCheckStatus.Stale, state.Status);
            Assert.Equal("Overlap check out of date — run Check Overlaps again", state.Message);
        }
    }

    [Fact]
    public void CancelFailureAndIncompleteAreNeverClear()
    {
        var plate = new Plate();
        var state = new OverlapReportState();
        var request = state.Begin(plate);
        state.Cancel();
        Assert.False(state.TryPublish(request, plate, Analyze(plate)));
        Assert.Equal(OverlapCheckStatus.Canceled, state.Status);
        Assert.Null(state.Report);
        request = state.Begin(plate);
        Assert.True(state.TryFail(request, plate));
        Assert.Equal(OverlapCheckStatus.Failed, state.Status);
        Assert.Contains("failed", state.Message);
        Assert.Null(state.Report);

        plate.Parts.Add(new Part(new Drawing("open", new Program())));
        request = state.Begin(plate);
        Assert.True(state.TryPublish(request, plate, Analyze(plate)));
        Assert.Equal(OverlapCheckStatus.Incomplete, state.Status);
        Assert.Equal("Overlap check incomplete: 0 overlapping pairs; 1 parts could not be checked", state.Message);
        state.Invalidate();
        Assert.Equal(OverlapCheckStatus.Stale, state.Status);
        Assert.Null(state.Report);
    }

    [Fact]
    public void IssueCountIncludesBothPairIdsAndDeduplicatesAcrossAllIssues()
    {
        var issues = new[]
        {
            new PlateOverlapIssue(2, null, "input"),
            new PlateOverlapIssue(2, 4, "pair"),
            new PlateOverlapIssue(4, 7, "pair"),
            new PlateOverlapIssue(7, null, "input")
        };
        Assert.Equal(3, OverlapReportState.CountUncheckedParts(issues));
    }

    [Fact]
    public void DisplayAndNonGeometryChangesRetainReportAndGeneration()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        Assert.Equal(OverlapDisplayMode.Areas, state.DisplayMode);
        var request = state.Begin(plate);
        var report = Analyze(plate);
        Assert.True(state.TryPublish(request, plate, report));
        state.DisplayMode = OverlapDisplayMode.Off;
        plate.Parts[0].BaseDrawing.Name = "renamed";
        plate.Quantity = 2;
        plate.PartSpacing = 123;
        Assert.True(state.EnsureFresh(plate));
        Assert.Same(report, state.Report);
        Assert.Equal(request, state.Generation);
        state.DisplayMode = OverlapDisplayMode.Areas;
        Assert.Same(report, state.Report);
        Assert.Equal(request, state.Generation);
        state.DisplayMode = OverlapDisplayMode.Off;
        state.Begin(plate);
        Assert.Equal(OverlapDisplayMode.Areas, state.DisplayMode);
        Assert.Null(state.Report);
    }

    [Theory]
    [InlineData(OverlapDisplayMode.Off, OverlapDisplayMode.Areas)]
    [InlineData(OverlapDisplayMode.Areas, OverlapDisplayMode.Areas)]
    [InlineData(OverlapDisplayMode.Centroids, OverlapDisplayMode.Centroids)]
    [InlineData(OverlapDisplayMode.Both, OverlapDisplayMode.Both)]
    public void CheckAndRecheckPreserveVisibleMode(OverlapDisplayMode chosen, OverlapDisplayMode expected)
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState { DisplayMode = chosen };
        var request = state.Begin(plate);
        Assert.Equal(expected, state.DisplayMode);
        Assert.True(state.TryPublish(request, plate, Analyze(plate)));
        state.Begin(plate);
        Assert.Equal(expected, state.DisplayMode);
    }

    [Fact]
    public void NewPlateResetsStateButRetainsDocumentDisplayPreference()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var request = state.Begin(plate);
        state.DisplayMode = OverlapDisplayMode.Off;
        state.Reset();
        Assert.Equal(OverlapCheckStatus.NotChecked, state.Status);
        Assert.Null(state.Report);
        Assert.Equal(OverlapDisplayMode.Off, state.DisplayMode);
        Assert.False(state.TryPublish(request, plate, Analyze(plate)));
    }

    [Fact]
    public void CutoffSlotsRemainPartOfTheOrderedIdentityStamp()
    {
        var plate = PlateWithParts();
        var cutoff = Rectangle();
        cutoff.BaseDrawing.IsCutOff = true;
        plate.Parts.Insert(0, cutoff);
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        var pair = Assert.Single(state.Report.Pairs);
        Assert.Equal((1, 2), (pair.PartAId, pair.PartBId));
        plate.Parts.RemoveAt(0);
        Assert.False(state.EnsureFresh(plate));
        Assert.Null(state.Report);
    }

    [Fact]
    public void InPlaceGeometryEditorsMustExplicitlyInvalidate()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var request = state.Begin(plate);
        var report = Analyze(plate);
        // Deliberately not serialized or converted by the stamp.
        plate.Parts[0].BaseDrawing.Program.Codes.Clear();
        Assert.True(state.EnsureFresh(plate));
        state.Invalidate();
        Assert.False(state.TryPublish(request, plate, report));
        Assert.Equal(OverlapCheckStatus.Stale, state.Status);
    }

    [Fact]
    public void EmptyCompleteReportAloneCanSayClear()
    {
        var plate = new Plate();
        var state = new OverlapReportState();
        Assert.Equal(OverlapCheckStatus.NotChecked, state.Status);
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        Assert.Equal("No material overlaps detected", state.Message);
    }

    private static PlateOverlapReport Analyze(Plate plate) => PlateOverlapAnalyzer.Analyze(plate.Parts.ToArray());

    private static Plate PlateWithParts()
    {
        var plate = new Plate();
        plate.Parts.Add(Rectangle());
        plate.Parts.Add(Rectangle());
        return plate;
    }

    private static Part Rectangle()
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("same name", program));
    }
}

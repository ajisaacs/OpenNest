using System.Globalization;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using static OpenNest.Tests.TestHelpers;

namespace OpenNest.Tests.Diagnostics;

/// <summary>The status bar's Layout label also checks the sheet edge.</summary>
public class OverlapEdgeLabelTests
{
    [Theory]
    [InlineData(10, 10, "Layout: OK", false)]
    [InlineData(118, 10, "Layout: 1 off plate", true)]
    [InlineData(130, 10, "Layout: 1 off plate", true)]
    [InlineData(10, -2, "Layout: 1 off plate", true)]
    [InlineData(0.2, 10, "Layout: 1 outside work area", true)]
    public void Summary_ReportsPartsOffThePlateOrOutsideTheWorkArea(double x, double y, string expected, bool attention)
    {
        var plate = PlateWith(new Spacing(0.5, 0.5, 0.5, 0.5), Square(x, y));
        var state = Publish(plate);

        Assert.Equal(expected, state.Summary);
        Assert.Equal(attention, state.NeedsAttention);
    }

    [Fact]
    public void OffPlateFinding_NamesThePartAndMarksTheEdge()
    {
        var plate = PlateWith(new Spacing(), Square(10, 10), Square(-1, 10, "hanging"));

        var finding = Assert.Single(Analyze(plate).EdgeFindings);

        Assert.Equal((1, "hanging", true), (finding.PartId, finding.PartName, finding.OffPlate));
        Assert.Contains("1.000 past the plate's left edge", finding.Message);
        Assert.Equal(0, finding.Marker.X, 6);
        Assert.Contains("1 parts off the plate", Publish(plate).Message);
    }

    [Fact]
    public void CountsCombineWithOverlaps()
    {
        var plate = PlateWith(new Spacing(), Square(10, 10), Square(12, 10), Square(119, 10));

        Assert.Equal("Layout: 1 overlap, 1 off plate", Publish(plate).Summary);
    }

    [Fact]
    public void EdgeSpacingChange_MakesTheResultStale()
    {
        var plate = PlateWith(new Spacing(), Square(0.2, 10));
        var state = Publish(plate);
        Assert.Equal("Layout: OK", state.Summary);

        plate.EdgeSpacing = new Spacing(0.5, 0.5, 0.5, 0.5);

        Assert.False(state.EnsureFresh(plate));
        Assert.Equal("Layout: out of date", state.Summary);
    }

    [Fact]
    public void EdgeFindings_AreDrawnAndHitAtTheirMarkers()
    {
        var plate = PlateWith(new Spacing(0.5, 0.5, 0.5, 0.5), Square(10, 10), Square(0.2, 10, "close"),
            Square(-1, 30, "hanging"));
        var state = Publish(plate);

        Assert.Equal(new[] { (1, false), (2, true) },
            state.DisplayEdgeFindings.Select(finding => (finding.PartId, finding.OffPlate)));
        var close = state.DisplayEdgeFindings[0];
        Assert.Equal("2 outside work area", OverlapPairPresentation.Label(close));
        Assert.Equal("3 off plate", OverlapPairPresentation.Label(state.DisplayEdgeFindings[1]));

        var hit = Assert.Single(OverlapPairPresentation.HitTest(state.DisplayEdgeFindings, world => world,
            close.Marker + new Vector(3, 0), 96));
        Assert.Same(close, hit);
        Assert.Empty(OverlapPairPresentation.HitTest(state.DisplayEdgeFindings, world => world,
            close.Marker + new Vector(11, 0), 96));
        var details = OverlapPairPresentation.Details(hit, Units.Inches, CultureInfo.InvariantCulture);
        Assert.StartsWith("Outside work area: part 2\n'close' is outside the work area: 0.200 from the plate's "
            + "left edge, inside its 0.500 edge spacing.\nMarker: (", details);
    }

    [Fact]
    public void EdgeMarkers_FollowTheirPartsThroughEdits()
    {
        var plate = PlateWith(new Spacing(0.5, 0.5, 0.5, 0.5), Square(10, 10), Square(0.2, 10, "close"),
            Square(-1, 30, "hanging"));
        var state = Publish(plate);

        plate.Parts[0].Offset(1, 0);
        state.Invalidate(plate);
        Assert.Equal(new[] { 1, 2 }, state.DisplayEdgeFindings.Select(finding => finding.PartId));

        plate.Parts[2].Offset(5, 0);
        state.Invalidate(plate);
        Assert.Equal(1, Assert.Single(state.DisplayEdgeFindings).PartId);

        plate.EdgeSpacing = new Spacing();
        state.Invalidate(plate);
        Assert.Empty(state.DisplayEdgeFindings);
    }

    [Fact]
    public void BareParts_HaveNoSheetToCheck()
    {
        var report = PlateOverlapAnalyzer.Analyze(new[] { Square(-50, -50) });

        Assert.Empty(report.EdgeFindings);
        Assert.True(report.IsComplete);
    }

    private static OverlapReportState Publish(Plate plate)
    {
        var state = new OverlapReportState();
        Assert.True(state.TryPublish(state.Begin(plate), plate, Analyze(plate)));
        return state;
    }

    private static PlateOverlapReport Analyze(Plate plate) =>
        PlateOverlapAnalyzer.Analyze(PlateOverlapAnalyzer.CapturePlate(plate));

    private static Plate PlateWith(Spacing edges, params Part[] parts)
    {
        // 120 long (X) by 60 wide (Y), quadrant 1.
        var plate = new Plate { EdgeSpacing = edges };
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static Part Square(double x, double y, string name = "square") =>
        new(new Drawing(name, MakeSquareProgram(0, 0, 4)), new Vector(x, y));
}

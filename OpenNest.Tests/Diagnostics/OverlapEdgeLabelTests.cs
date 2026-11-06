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
    [InlineData(0.2, 10, "Layout: 1 near edge", true)]
    public void Summary_ReportsPartsOffThePlateOrNearItsEdge(double x, double y, string expected, bool attention)
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

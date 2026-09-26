using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Tests.BestFit;

public class NfpBestFitIntegrationTests
{
    [Fact]
    public void FindBestFits_ReturnsKeptResults_ForSquare()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);
        Assert.NotEmpty(results);
        Assert.NotEmpty(results.Where(r => r.Keep));
    }

    [Fact]
    public void FindBestFits_ResultsHaveValidDimensions()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);

        foreach (var result in results.Where(r => r.Keep))
        {
            Assert.True(result.BoundingWidth > 0);
            Assert.True(result.BoundingHeight > 0);
            Assert.True(result.RotatedArea > 0);
        }
    }

    [Fact]
    public void FindBestFits_LShape_HasBetterUtilization_ThanBoundingBox()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeLShapeDrawing();
        var results = finder.FindBestFits(drawing);

        var bestUtilization = results.Where(r => r.Keep).Max(r => r.Utilization);
        Assert.True(bestUtilization > 0.5);
    }

    [Fact]
    public void FindBestFits_KeepsLowUtilizationFrame_CutoutsDoNotRejectPair()
    {
        // A thin-walled hollow frame nests tightly despite tiny part/bbox utilization.
        // Utilization must never reject a pair; ranking is by pair bounding-box area.
        var finder = new BestFitFinder(48, 96);
        var drawing = TestHelpers.MakeFrameDrawing(w: 20, h: 6, t: 0.5);

        var results = finder.FindBestFits(drawing);

        Assert.NotEmpty(results.Where(r => r.Keep));
        Assert.All(results.Where(r => r.Keep), r => Assert.Equal("Valid", r.Reason));
        var best = results.Where(r => r.Keep).First();
        Assert.True(best.Utilization < 0.3);
    }

    [Fact]
    public void FindBestFits_ResultsAreSortedByPairBoundingArea()
    {
        var finder = new BestFitFinder(48, 96);
        var results = finder.FindBestFits(TestHelpers.MakeFrameDrawing());

        Assert.True(results.Count > 1);
        Assert.True(
            results.Zip(results.Skip(1), (a, b) => a.RotatedArea <= b.RotatedArea).All(x => x)
        );
    }

    [Fact]
    public void FindBestFits_NoOverlaps_InKeptResults()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);

        Assert.All(results.Where(r => r.Keep), r => Assert.Equal("Valid", r.Reason));
    }
}

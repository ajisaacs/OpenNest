using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Tests;

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

        var bestUtilization = results
            .Where(r => r.Keep)
            .Max(r => r.Utilization);
        Assert.True(bestUtilization > 0.5);
    }

    [Fact]
    public void FindBestFits_NoOverlaps_InKeptResults()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);

        Assert.All(results.Where(r => r.Keep), r =>
            Assert.Equal("Valid", r.Reason));
    }
}

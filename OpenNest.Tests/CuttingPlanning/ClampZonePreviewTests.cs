using OpenNest.Engine.CuttingPlanning;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>Machine clamps stay visible on a cutting-plan preview of a clamped plate.</summary>
public class ClampZonePreviewTests
{
    [Fact]
    public void PreviewCarriesThePlatesClampZones()
    {
        var plate = BestEffortCuttingPlanTests.Plate(BestEffortCuttingPlanTests.TouchingContours());
        plate.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0, 21 }));

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        var preview = proposal.BuildPreview(0);

        Assert.NotNull(preview);
        Assert.Equal(plate.ClampZones, preview.ClampZones); // same immutable zones, same order
        Assert.Equal(2, preview.ClampZones.Count);
        Assert.Null(preview.ClampRail); // a detached display copy never re-resolves
    }

    [Fact]
    public void PreviewCarriesThePlatesSupportBed()
    {
        var plate = BestEffortCuttingPlanTests.Plate(BestEffortCuttingPlanTests.TouchingContours());
        plate.SupportBed = new OpenNest.Support.SupportGrid(25, 100, 2, 2, stabilityMargin: 0.1);

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        var preview = proposal.BuildPreview(0);

        Assert.NotNull(preview);
        Assert.Same(plate.SupportBed, preview.SupportBed);
    }
}

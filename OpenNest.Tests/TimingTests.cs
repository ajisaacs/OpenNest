using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// D9: a sheet's cut-time estimate gains the rail's removal minutes once per removed clamp,
/// scaled by the plate's cut quantity like every other timing field; a plate with nothing
/// removed is unaffected.
/// </summary>
public class TimingTests
{
    private static Plate PlateWithOneRemovedClamp(double removalMinutes, int quantity = 1)
    {
        var plate = new Plate(3, 23) { Quantity = quantity, EdgeSpacing = new Spacing(0, 0, 0, 0) };
        var rail = new ClampRail(1.5, 0.625, new[] { 6.0, 21.0 }, removalMinutes);
        plate.SetClampRail(rail, new[] { "clamp-1" });
        return plate;
    }

    [Fact]
    public void PlateWithNoRemovedClampsAddsNoTime()
    {
        var plate = new Plate(48, 96) { Quantity = 3 };

        var info = Timing.GetTimingInfo(plate);

        Assert.Equal(0, info.ClampRemovalMinutes);
    }

    [Fact]
    public void RemovedClampMinutesAreAddedOncePerPhysicalSheet()
    {
        var plate = PlateWithOneRemovedClamp(removalMinutes: 5, quantity: 3);

        var info = Timing.GetTimingInfo(plate);

        Assert.Equal(15, info.ClampRemovalMinutes); // 5 minutes x 3 physical sheets
    }

    [Fact]
    public void CalculateTimeAddsTheRemovalMinutesToTheEstimate()
    {
        var info = new TimingInfo { ClampRemovalMinutes = 7 };

        var time = Timing.CalculateTime(info, CutParameters.Default);

        Assert.Equal(TimeSpan.FromMinutes(7), time);
    }

    [Fact]
    public void NestTotalsSumRemovalMinutesAcrossPlates()
    {
        var nest = new Nest();
        nest.Plates.Add(PlateWithOneRemovedClamp(removalMinutes: 5));
        nest.Plates.Add(new Plate(48, 96)); // ordinary plate, no clamps

        var info = Timing.GetTimingInfo(nest);

        Assert.Equal(5, info.ClampRemovalMinutes);
    }
}

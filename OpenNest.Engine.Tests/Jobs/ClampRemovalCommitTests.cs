using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// D8/C13: a plate Auto Nest commits with a last-resort clamp removal keeps following its
/// rail - minus the removed clamp - through later resizes, exactly like an ordinary
/// rail-following sheet (Slice 3), and the removal itself is real, inspectable plate state.
/// </summary>
public class ClampRemovalCommitTests
{
    [Fact]
    public void CommittedPlateKeepsFollowingTheRailMinusTheRemovedClamp()
    {
        // Same geometry as ClampRemovalRecoveryTests.BlockedPartRemovesOnlyTheNearestOriginCoveredClamp:
        // clamps 15" apart on a 23"-long, 3"-wide sheet; a 14"-wide part can only clear both by
        // removing one, and "clamp-1" (nearest the origin) wins the tie.
        var drawing = new Drawing("wide", TestDrawingFactory.Rectangle(14, 3));
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.PlateDefaults.Size = new Size(3, 23);
        nest.PlateDefaults.EdgeSpacing = new Spacing(0, 0, 0, 0);
        nest.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0, 21.0 }));
        nest.CreatePlate(); // empty template the commit fills
        using var manager = new PlateManager(nest);

        var request = new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = drawing, Quantity = 1 } },
            NestStockBuilder.SinglePlate(nest.Plates[0]));
        var result = NestPipeline.Run(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));

        var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager);
        var plate = Assert.Single(applied);

        Assert.Equal(new[] { "clamp-1" }, plate.RemovedClamps);
        Assert.NotNull(plate.ClampRail);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-1");
        Assert.Contains(plate.ClampZones, z => z.Id == "clamp-2");
        Assert.Equal(ClampRail.DefaultRemovalMinutes, plate.ClampRemovalMinutes); // 1 clamp removed

        // C13: still follows the rail (minus the removal) through a resize/rotate.
        plate.Size = new Size(3, 60);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-1");
        Assert.Contains(plate.ClampZones, z => z.Id == "clamp-2");
    }
}

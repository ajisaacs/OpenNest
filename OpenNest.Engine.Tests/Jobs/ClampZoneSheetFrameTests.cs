using System;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Every <see cref="Plate"/> stores clamp zones in the sheet's lower-left frame, including
/// plates the engine builds from stock; consumers map them into the signed placement frame
/// through <see cref="ClampZoneFrame"/>. Fixtures use a real rail in all quadrants, with
/// expected footprints from the physical rule (center +/- width/2 from the origin along the
/// edge, depth into the sheet).
/// </summary>
public class ClampZoneSheetFrameTests
{
    private static (string, long, long, long, long) Bits(ClampZone zone) =>
        (zone.Id, BitConverter.DoubleToInt64Bits(zone.Bounds.X), BitConverter.DoubleToInt64Bits(zone.Bounds.Y),
            BitConverter.DoubleToInt64Bits(zone.Bounds.Length), BitConverter.DoubleToInt64Bits(zone.Bounds.Width));

    private static Box World(int quadrant, double center, double width = 1.5, double depth = 0.625)
    {
        var x = quadrant is 2 or 3 ? -(center + width / 2) : center - width / 2;
        var y = quadrant is 3 or 4 ? -depth : 0;
        return new Box(x, y, width, depth);
    }

    private static Program Rectangle(double length, double width)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(length, 0);
        program.LineTo(length, width);
        program.LineTo(0, width);
        program.LineTo(0, 0);
        return program;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MaterializedPlatesStoreSheetFrameZonesAndWarnForCutsThroughThem(int quadrant)
    {
        var live = new Plate(81, 120) { Quadrant = quadrant };
        live.SetClampRail(new ClampRail(1.5, 0.625, new[] { 35.0, 120.5 }));
        var stock = DrawingJobMapper.FromPlate("s", live, 1);
        var job = new NestJob(Array.Empty<NestJobPart>(), new[] { stock });
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, Array.Empty<NestJobPlacement>()) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());

        var nest = NestResultMaterializer.Materialize(job, result).Nest;
        var plate = Assert.Single(nest.Plates);

        Assert.Equal(live.ClampZones.Select(Bits), plate.ClampZones.Select(Bits));
        Assert.Equal(stock.ClampZones.Select(Bits), ClampZoneFrame.ToPlacement(plate).Select(Bits));
        var footprint = World(quadrant, 35);
        Assert.Equal(footprint.Left, stock.ClampZones[0].Bounds.Left, 9);
        Assert.Equal(footprint.Bottom, stock.ClampZones[0].Bounds.Bottom, 9);

        // A short cut-off through the physical clamp must be found and require consent.
        plate.CutOffs.Add(new CutOff(new Vector(quadrant is 2 or 3 ? -35 : 35, 0), CutOffAxis.Vertical)
        {
            StartLimit = quadrant is 3 or 4 ? -2 : 0,
            EndLimit = quadrant is 3 or 4 ? 0 : 2,
        });
        Assert.Equal("clamp-1", Assert.Single(ClampCutDiagnostics.Find(plate)).ClampId);
        plate.RegenerateCutOffs(new CutOffSettings());
        var report = PostVerificationAnalyzer.Analyze(nest);
        Assert.Contains(report.Findings, f => f.ClampId == "clamp-1");
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void StockClampZonesCannotBeEditedThroughTheReturnedZones()
    {
        // Stock is immutable input: editing a returned zone must not move the stock's clamp
        // away from the zones its materialized plates (validation, fill, posting) use.
        var live = new Plate(24, 48);
        live.ClampZones.Add(new ClampZone("z", new Box(10, 0, 1.5, 0.625)));
        var stock = DrawingJobMapper.FromPlate("s", live, 1);

        stock.ClampZones[0].Bounds.X += 10;
        foreach (var zone in stock.ClampZones)
            zone.Bounds.Y = 5;

        var current = Assert.Single(stock.ClampZones);
        Assert.Equal(10, current.Bounds.X);
        Assert.Equal(0, current.Bounds.Y);
        var plate = DrawingJobMapper.CreatePlate(stock);
        Assert.Equal(stock.ClampZones.Select(Bits), ClampZoneFrame.ToPlacement(plate).Select(Bits));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InteractiveFillOnLiveRailPlateAvoidsClampsInEveryQuadrant(int quadrant)
    {
        // Five rows of 4.29 squares at 0.5 spacing span 23.45 of the 23.5 work-area height, so an
        // unobstructed fill reaches the front edge in every quadrant.
        var plate = new Plate(new Size(24, 48))
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(0.25, 0.25),
            PartSpacing = 0.5,
        };
        var centers = new[] { 10.75, 20.75, 30.75, 40.75 };
        plate.SetClampRail(new ClampRail(1.5, 0.625, centers));
        var keepOuts = centers.Select(c => World(quadrant, c).Offset(0.25)).ToArray();
        var item = new NestItem { Drawing = new Drawing("square", Rectangle(4.29, 4.29)), Quantity = 500 };

        var parts = PlateFillService.FillItem("Fill", plate, item, plate.WorkArea(), null, CancellationToken.None);

        Assert.True(parts.Count >= 20, $"only {parts.Count} parts placed");
        foreach (var part in parts)
        {
            var box = part.BoundingBox;
            Assert.DoesNotContain(keepOuts, k => box.Left < k.Right - 1e-6 && k.Left < box.Right - 1e-6
                && box.Bottom < k.Top - 1e-6 && k.Bottom < box.Top - 1e-6);
        }
    }
}

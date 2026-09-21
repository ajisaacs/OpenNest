using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Runnable end-to-end example of the whole-job engine API: multiple part requirements, multiple plate
/// sizes, and an enumeration of every returned plate, placement, leftover, and stock line. Also the
/// documentation checkpoint for the legacy caller boundaries that have not been migrated (task 8).
/// </summary>
public class NestJobExampleTests
{
    [Fact]
    public void MultiRequirementMultiStockJobEnumeratesEveryPlateAndLeftover()
    {
        // Two requirements with independent IDs, quantities, and priorities.
        var job = new NestJob(
            new[]
            {
                Part("bracket", 100.0, 60.0, 5, priority: 0),
                Part("plate-clip", 40.0, 40.0, 8, priority: 1),
            },
            // Mixed inventory: five large sheets and unlimited small sheets.
            new[]
            {
                new NestPlateStock(
                    "large",
                    new Size(600.0, 400.0),
                    quantity: 5,
                    partSpacing: 2.0,
                    edgeSpacing: new Spacing(5.0, 5.0, 5.0, 5.0),
                    quadrant: 1
                ),
                new NestPlateStock(
                    "small",
                    new Size(300.0, 300.0),
                    quantity: null,
                    partSpacing: 2.0,
                    edgeSpacing: new Spacing(5.0, 5.0, 5.0, 5.0),
                    quadrant: 1
                ),
            }
        );

        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(job);

        // -- Every physical plate is enumerated with its stock identity and placements. --
        Console.WriteLine($"Status: {result.Status}, stop reason: {result.StopReason}.");
        foreach (var plate in result.Plates)
        {
            Console.WriteLine(
                $"Plate {plate.PlateIndex} from stock '{plate.StockId}' "
                    + $"({plate.Stock.Size.Width} x {plate.Stock.Size.Length}):"
            );
            foreach (var placement in plate.Placements)
                Console.WriteLine(
                    $"  {placement.PartId} #{placement.InstanceIndex} at "
                        + $"({placement.X:F1}, {placement.Y:F1}) rotated {placement.Rotation:F3} rad."
                );
        }

        // -- Every requirement reports exact fulfillment, including leftovers. --
        foreach (var fulfillment in result.Fulfillment)
            Console.WriteLine(
                $"Requirement '{fulfillment.PartId}': requested {fulfillment.Requested}, "
                    + $"placed {fulfillment.Placed}, unplaced {fulfillment.Unplaced}."
            );

        // -- Every stock line reports physical sheets used and remaining availability. --
        foreach (var usage in result.StockUsage)
            Console.WriteLine(
                $"Stock '{usage.StockId}': used {usage.Used}, "
                    + $"remaining {(usage.Remaining.HasValue ? usage.Remaining.Value.ToString() : "unlimited")}."
            );

        // Invariants the enumeration relies on: conservation per requirement and per stock line, no
        // empty plates, every plate bound to supplied stock, and per-placement instance accounting.
        foreach (var fulfillment in result.Fulfillment)
        {
            Assert.Equal(fulfillment.Requested, fulfillment.Placed + fulfillment.Unplaced);
            Assert.True(fulfillment.Unplaced >= 0);
        }
        foreach (var usage in result.StockUsage)
        {
            var stock = job.Plates.First(candidate => candidate.Id == usage.StockId);
            Assert.True(usage.Used >= 0);
            Assert.Equal(
                stock.Quantity is int capacity ? capacity - usage.Used : (int?)null,
                usage.Remaining
            );
        }

        Assert.All(result.Plates, plate => Assert.NotEmpty(plate.Placements));
        var plateCountByStock = result
            .Plates.GroupBy(plate => plate.StockId)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var usage in result.StockUsage)
            Assert.Equal(usage.Used, plateCountByStock.GetValueOrDefault(usage.StockId));

        var instanceIndicesByPart = result
            .Plates.SelectMany(plate => plate.Placements)
            .GroupBy(placement => placement.PartId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(placement => placement.InstanceIndex)
            );
        foreach (var fulfillment in result.Fulfillment)
            Assert.Equal(
                Enumerable.Range(0, fulfillment.Placed),
                instanceIndicesByPart
                    .GetValueOrDefault(fulfillment.PartId, new List<int>())
                    .OrderBy(index => index)
            );

        // The default heuristic completes this synthetic job from the mixed inventory.
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(NestJobStopReason.Completed, result.StopReason);
        Assert.Equal(
            5,
            result.Fulfillment.Single(fulfillment => fulfillment.PartId == "bracket").Placed
        );
        Assert.Equal(
            8,
            result.Fulfillment.Single(fulfillment => fulfillment.PartId == "plate-clip").Placed
        );
    }

    [Fact]
    public void MaxPlatesExampleShowsExplicitLeftovers()
    {
        // Same shape of job, but a plate budget forces an explicit partial result.
        var job = new NestJob(
            new[] { Part("part", 100.0, 100.0, 6, priority: 0) },
            new[]
            {
                new NestPlateStock(
                    "sheet",
                    new Size(220.0, 220.0),
                    quantity: null,
                    partSpacing: 2.0,
                    edgeSpacing: new Spacing(5.0, 5.0, 5.0, 5.0),
                    quadrant: 1
                ),
            },
            new NestJobOptions("Default", maxPlates: 1)
        );

        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(job);

        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
        var single = Assert.Single(result.Plates);
        Assert.Equal("sheet", single.StockId);
        var fulfillment = Assert.Single(result.Fulfillment);
        Assert.Equal(6, fulfillment.Requested);
        Assert.Equal(single.Placements.Count, fulfillment.Placed);
        Assert.Equal(fulfillment.Requested - fulfillment.Placed, fulfillment.Unplaced);
    }

    /// <summary>
    /// Legacy caller boundaries documented for task 8 — these paths still use the old single-plate
    /// engine entry points and are deliberately NOT migrated in this slice. Verified against source at
    /// the time of writing:
    /// - Desktop UI: OpenNest/Forms/MainForm.cs RunAutoNestAsync (~line 1004) and NestSinglePlateAsync
    ///   (~line 1087) orchestrate plate-first and part-first fills directly against NestEngineRegistry
    ///   engines. Migration requires preserving populated-plate editing, preview routing, and
    ///   Accept-versus-Cancel semantics — a separate adapter design (documented follow-on).
    /// - CLI: OpenNest.Console/Program.cs calls engine.Nest(...) (~line 316) on one plate. Migration
    ///   point: build a NestJob from imported drawings plus CLI plate options and call Solve once.
    /// - MCP: OpenNest.Mcp/Tools/NestingTools.cs calls engine.Nest(...) (~line 239) on the session
    ///   plate. Migration point: same single job call, materialized through NestResultMaterializer.
    /// The public API (OpenNest.Api NestRunner) already delegates to NestJobRunner.Solve (task 6).
    /// This test exercises the legacy compatibility signature so an accidental removal of that entry
    /// point breaks the documented contract.
    /// </summary>
    [Fact]
    public void LegacyCompatibilityEntryPointsStillExist()
    {
        var plate = new Plate { Size = new Size(300.0, 200.0), Quadrant = 1 };
        var drawing = new Drawing("legacy", TestDrawingFactory.Rectangle(50.0, 50.0));
        var item = new NestItem { Drawing = drawing, Quantity = 1 };

        // MainForm/Console/MCP still reach the legacy single-plate signature unchanged; the engine
        // returns placed Parts for the caller to attach (legacy paths do not attach on their own).
        var engine = NestEngineRegistry.Create(plate);
        var parts = engine.Nest(new List<NestItem> { item }, null, CancellationToken.None);

        Assert.NotNull(engine);
        var placed = Assert.Single(parts);
        Assert.Same(drawing, placed.BaseDrawing);
    }

    private static NestJobPart Part(
        string id,
        double width,
        double length,
        int quantity,
        int priority
    ) =>
        new(
            id,
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(width, length)),
            quantity,
            priority
        );
}

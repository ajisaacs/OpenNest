using OpenNest.Data.MaterialLedger;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Data;

/// <summary>
/// After Auto Nest commits, each plate on ledger stock takes the next codes of its row in nest
/// order; nothing else touches codes, so a rejected proposal leaves them as they were.
/// </summary>
public class LedgerStockAssignmentTests
{
    private static LedgerStockRow Row(string id, double width, double length, params string[] codes) =>
        new(id, new Size(width, length), codes.Length, IsDrop: codes.Distinct().Count() > 1, IsIrregular: false,
            codes, Array.Empty<string>());

    [Fact]
    public void Plates_DequeueTheirRowsCodes_InNestOrder_AndBundlesRepeatTheirCode()
    {
        var drops = Row("ledger:1", 30, 52.5, "DROP-1182", "DROP-1190", "DROP-1201");
        var bundle = Row("ledger:2", 60, 120, "SHEET-456", "SHEET-456", "SHEET-456");
        var nest = new Nest();
        var first = Add(nest, 1);
        var second = Add(nest, 2);
        var third = Add(nest, 2);
        var hand = Add(nest, 1);
        hand.StockItemCodes = ["STALE"];

        // Committed out of nest order: codes still follow Nest.Plates.
        LedgerStockAssignment.Assign(nest.Plates,
            [(third, "ledger:2"), (first, "ledger:1"), (second, "ledger:1"), (hand, "option-0")],
            [drops, bundle]);

        Assert.Equal(["DROP-1182"], first.StockItemCodes);
        Assert.Equal(["DROP-1190", "DROP-1201"], second.StockItemCodes);
        Assert.Equal(["SHEET-456", "SHEET-456"], third.StockItemCodes);
        Assert.Empty(hand.StockItemCodes);
    }

    [Fact]
    public void ARowThatRunsOut_LeavesTheRemainingSheetsUncoded()
    {
        var nest = new Nest();
        var first = Add(nest, 2);
        var second = Add(nest, 1);

        LedgerStockAssignment.Assign(nest.Plates, [(first, "ledger:1"), (second, "ledger:1")],
            [Row("ledger:1", 30, 52.5, "DROP-1")]);

        Assert.Equal(["DROP-1"], first.StockItemCodes);
        Assert.Empty(second.StockItemCodes);
    }

    [Fact]
    public void ASheetSolvedWithClampsRemoved_TakesItsBaseRowsCodes()
    {
        var nest = new Nest();
        var plate = Add(nest, 1);

        LedgerStockAssignment.Assign(nest.Plates, [(plate, "ledger:3::removed:clamp-2")],
            [Row("ledger:3", 48, 96, "SHEET-9")]);

        Assert.Equal(["SHEET-9"], plate.StockItemCodes);
        Assert.Equal("ledger:3", LedgerStockAssignment.BaseStockId("ledger:3::removed:clamp-2,clamp-3"));
    }

    [Fact]
    public void CommittedProposal_CodesThePlatesItWasSolvedOn()
    {
        var (nest, drawing, manager) = Workspace();
        using (manager)
        {
            var rows = new[] { Row("ledger:1", 30, 52.5, "DROP-1", "DROP-2") };
            var result = Solve(drawing, overlap: false);
            Assert.True(result.IsValid, string.Join("; ", result.Violations));

            var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager);
            LedgerStockAssignment.Assign(nest.Plates,
                NestPipelineCommit.PairWithStock(result, applied).Select(p => (p.Plate, p.Stock.Id)), rows);

            Assert.Equal(["DROP-1"], Assert.Single(applied).StockItemCodes);
        }
    }

    [Fact]
    public void RejectedProposal_LeavesCodesUntouched()
    {
        var (nest, drawing, manager) = Workspace();
        using (manager)
        {
            var empty = nest.Plates.Single();
            empty.StockItemCodes = ["SHEET-OLD"];
            var result = Solve(drawing, overlap: true);
            Assert.False(result.IsValid);

            // Auto Nest assigns only after a commit; without consent there is none.
            Assert.Throws<InvalidOperationException>(() => NestPipelineCommit.ApplyToEmptyPlates(result, manager));

            Assert.Equal(["SHEET-OLD"], empty.StockItemCodes);
            Assert.Empty(empty.Parts);
        }
    }

    [Fact]
    public void Describe_NamesEachCodeWithItsLocation_CountsRuns_AndNotesUncodedSheets()
    {
        var plate = new Plate(60, 120) { Quantity = 4 };
        Assert.Null(LedgerStockAssignment.Describe(plate, null));

        plate.StockItemCodes = ["SHEET-456", "SHEET-456", "DROP-1182"];
        var locations = new Dictionary<string, string> { ["SHEET-456"] = "A1" };

        Assert.Equal("Stock: SHEET-456 (A1) x2, DROP-1182, 1 uncoded", LedgerStockAssignment.Describe(plate, locations));
    }

    [Fact]
    public void Mapper_KeepsEachItemsLocationForDisplay()
    {
        var mapping = LedgerStockMapper.Map(
        [
            new LedgerStockItem("D-1", 1, LedgerItemKinds.Drop, 24, 36, 1, LedgerItemShapes.Rectangular,
                "R1", "Rack 1", null, null, DateTime.UtcNow),
            new LedgerStockItem("D-2", 1, LedgerItemKinds.Drop, 24, 36, 1, LedgerItemShapes.Rectangular,
                "R2", "Rack 2", null, null, DateTime.UtcNow),
        ], Units.Inches);

        var row = Assert.Single(mapping.Rows);
        Assert.Equal("R1", row.ItemLocations["D-1"]);
        Assert.Equal("R2", row.ItemLocations["D-2"]);
    }

    private static Plate Add(Nest nest, int quantity)
    {
        var plate = new Plate(30, 52.5) { Quantity = quantity };
        nest.Plates.Add(plate);
        return plate;
    }

    private static (Nest Nest, Drawing Drawing, PlateManager Manager) Workspace()
    {
        var nest = new Nest();
        var drawing = new RectangleShape { Name = "SYN", Length = 4, Width = 2 }.GetDrawing();
        nest.Drawings.Add(drawing);
        nest.CreatePlate();
        return (nest, drawing, new PlateManager(nest));
    }

    private static NestPipelineResult Solve(Drawing drawing, bool overlap) =>
        NestPipeline.Run(new OnFirstStock(overlap), "stub", new NestPipelineRequest("stub",
            [new NestItem { Drawing = drawing, Quantity = 2 }],
            [new NestPlateStock("ledger:1", new Size(30, 52.5), 2, 0.25, new Spacing(1, 1), 1)]));

    // Two copies on one sheet of the first stock row; overlapping when asked, so validation fails.
    private sealed class OnFirstStock(bool overlap) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default) => new(
                NestJobStatus.Complete, NestJobStopReason.Completed,
                [new NestJobPlateResult(0, job.Plates[0],
                [
                    new NestJobPlacement(job.Parts[0].Id, 0, 2, 2, 0),
                    new NestJobPlacement(job.Parts[0].Id, 1, overlap ? 2 : 20, 2, 0),
                ])],
                [new PartFulfillment(job.Parts[0].Id, 2, 2, 0)],
                [new StockUsage(job.Plates[0].Id, 1, 1)]);
    }
}

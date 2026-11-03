using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Data;

public class ReleaseLedgerStockTests
{
    private const string Material = "Synthetic Steel";

    private static Nest NestOf(double partSize, int quantity, string grade = "SYN-1")
    {
        var nest = new Nest("N26-0001")
        {
            Material = new Material(Material, grade),
            Thickness = 0.25,
        };
        nest.PlateDefaults.Size = new Size(48, 96);
        var drawing = new RectangleShape { Name = "SYN-A", Length = partSize, Width = partSize }
            .GetDrawing();
        drawing.Quantity.Required = quantity;
        nest.Drawings.Add(drawing);
        return nest;
    }

    private static LedgerStockItem Sheet(string code, decimal width, decimal length) =>
        new(code, 1, LedgerItemKinds.FullSheet, width, length, 1, LedgerItemShapes.Rectangular,
            "R1", "Rack 1", null, null, DateTime.UnixEpoch);

    private static LedgerStockResult Stock(params LedgerStockItem[] items) =>
        new LedgerStockResult.Ok(Array.Empty<LedgerStockSpec>(), items);

    private sealed class FakeLedger
    {
        public LedgerStockResult Stock { get; init; } = Stock();
        public LedgerCodesInUse InUse { get; init; } =
            LedgerCodesInUse.Known(Array.Empty<NestStockCodeUse>());
        public List<(string Material, string Grade, double Thickness)> StockReads { get; } = new();
        public int InUseReads { get; private set; }

        public ReleaseLedgerStock Create() => new(
            (material, grade, thickness, _) =>
            {
                StockReads.Add((material, grade, thickness));
                return Task.FromResult(Stock);
            },
            _ =>
            {
                InUseReads++;
                return Task.FromResult(InUse);
            });
    }

    private static async Task<(NestRenestResult Renest, string Note)> Release(
        ReleaseLedgerStock ledger, Nest nest)
    {
        var offer = await ledger.OfferAsync(nest, CancellationToken.None);
        var renest = NestRenest.Run(nest, NestingEngineRegistry.Create("Rectangles"), "Rectangles",
            offer.Options);
        return (renest, ledger.Assign(nest, offer, renest.Committed));
    }

    [Fact]
    public async Task LedgerStockIsUsedFirst_EvenWhenTheDefaultSheetWouldBeSmaller()
    {
        // One 60x120 ledger sheet holds both parts; a single pass scored by area would take
        // the smaller 48x96 Nest Defaults sheet instead.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };
        var nest = NestOf(20, 2);

        var (renest, note) = await Release(ledger.Create(), nest);

        Assert.True(renest.Nested, renest.Note);
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(new Size(60, 120), plate.Size);
        Assert.Equal(["S-1"], plate.StockItemCodes);
        Assert.Equal(0, nest.Drawings.First().Quantity.Remaining);
        Assert.Equal("1 sheet from MaterialLedger stock.", note);
        Assert.Equal((Material, "SYN-1", 0.25), Assert.Single(ledger.StockReads));
    }

    [Fact]
    public async Task CopiesTheLedgerCannotHold_TopUpOnTheNestDefaultsSheet_WithoutACode()
    {
        // Two free 40x40 sheets each hold one 30x30 part; the third copy needs a default sheet.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 40, 40), Sheet("S-2", 40, 40)) };
        var nest = NestOf(30, 3);

        var (renest, note) = await Release(ledger.Create(), nest);

        Assert.True(renest.Nested, renest.Note);
        Assert.Empty(renest.Unplaced);
        Assert.Equal(0, nest.Drawings.First().Quantity.Remaining);
        var coded = nest.Plates.Where(p => p.StockItemCodes.Count > 0).ToList();
        Assert.Equal(["S-1", "S-2"], coded.SelectMany(p => p.StockItemCodes).Order());
        Assert.All(coded, p => Assert.Equal(new Size(40, 40), p.Size));
        var topUp = Assert.Single(nest.Plates.Where(p => p.StockItemCodes.Count == 0));
        Assert.Equal(new Size(48, 96), topUp.Size);
        Assert.Equal(3, renest.Sheets);
        Assert.Equal("2 sheets from MaterialLedger stock.", note);
    }

    [Fact]
    public async Task ALaterNestOfTheSameRun_DoesNotGetCodesAnEarlierNestTook()
    {
        // The release uploads every nest only after all are nested, so the server cannot
        // report the first nest's codes yet; a substitute row can list the same sheet twice.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };
        var run = ledger.Create();
        await Release(run, NestOf(20, 2));

        var second = NestOf(20, 2, grade: "SYN-2");
        var (renest, note) = await Release(run, second);

        Assert.True(renest.Nested, renest.Note);
        var plate = Assert.Single(second.Plates);
        Assert.Empty(plate.StockItemCodes);
        Assert.Equal(new Size(48, 96), plate.Size);
        Assert.Equal("MaterialLedger not used: no free stock of Synthetic Steel SYN-2 0.25 in.",
            note);
        Assert.Equal(["S-1"], run.Taken);
        Assert.Equal(1, ledger.InUseReads);
    }

    [Fact]
    public async Task CodesOnOtherServerNests_AreLeftOut()
    {
        var ledger = new FakeLedger
        {
            Stock = Stock(Sheet("S-1", 60, 120), Sheet("S-2", 60, 120)),
            InUse = LedgerCodesInUse.Known(
                [new NestStockCodeUse { ItemCode = "S-1", NestName = "N26-0099" }]),
        };

        var offer = await ledger.Create().OfferAsync(NestOf(20, 2), CancellationToken.None);

        Assert.Null(offer.NotUsed);
        Assert.Equal(["S-2"], Assert.Single(offer.Rows).ItemCodes);
    }

    [Fact]
    public async Task AnUnreachableLedger_NestsOnTheNestDefaultsSheet_AndSaysSo()
    {
        var ledger = new FakeLedger { Stock = new LedgerStockResult.Unreachable("timed out") };
        var nest = NestOf(20, 2);

        var (renest, note) = await Release(ledger.Create(), nest);

        Assert.True(renest.Nested, renest.Note);
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(new Size(48, 96), plate.Size);
        Assert.Empty(plate.StockItemCodes);
        Assert.Equal("MaterialLedger not used: unreachable: timed out.", note);
        Assert.Equal(0, ledger.InUseReads);
    }

    [Fact]
    public async Task UncheckedOtherNests_AreNamedInTheNote()
    {
        var ledger = new FakeLedger
        {
            Stock = Stock(Sheet("S-1", 60, 120)),
            InUse = LedgerCodesInUse.Unknown("the nest server did not answer"),
        };

        var (_, note) = await Release(ledger.Create(), NestOf(20, 2));

        Assert.Equal("1 sheet from MaterialLedger stock. Codes on other nests were not checked: "
            + "the nest server did not answer.", note);
    }
}

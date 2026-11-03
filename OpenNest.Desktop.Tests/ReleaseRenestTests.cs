using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Desktop.Tests;

public class ReleaseRenestTests
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

    private static LedgerStockItem Bundle(string code, decimal width, decimal length,
        int quantity) =>
        new(code, 1, LedgerItemKinds.FullSheetBundle, width, length, quantity,
            LedgerItemShapes.Rectangular, "B3", "Bay 3", null, null, DateTime.UnixEpoch);

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

    /// <summary>
    /// Rectangles, except that a sheet <c>overlaps</c> picks gets every copy at one spot.
    /// </summary>
    private sealed class OverlapOn(Func<string, bool> overlaps) : INestingEngine
    {
        private readonly INestingEngine _inner = NestingEngineRegistry.Create("Rectangles");

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default)
        {
            if (!overlaps(job.Plates[0].Id))
                return _inner.Solve(job, progress, token);
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(job.Plates[0], Enumerable.Range(0, job.Parts[0].Quantity)
                .Select(_ => (job.Parts[0].Id, 5.0, 5.0, 0.0)).ToArray());
            return builder.Build(NestJobStopReason.Completed);
        }
    }

    private static Task<ReleaseRenestResult> Release(ReleaseLedgerStock ledger, Nest nest,
        INestingEngine engine = null) =>
        ReleaseRenest.RunHeadlessAsync(nest, ledger,
            engine ?? NestingEngineRegistry.Create("Rectangles"), "Rectangles", 0,
            CancellationToken.None);

    [Fact]
    public async Task LedgerStockIsUsedFirst_EvenWhenTheDefaultSheetWouldBeSmaller()
    {
        // One 60x120 ledger sheet holds both parts; a single pass scored by area would take
        // the smaller 48x96 Nest Defaults sheet instead.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };
        var nest = NestOf(20, 2);

        var result = await Release(ledger.Create(), nest);

        Assert.True(result.Nested, result.Note);
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(new Size(60, 120), plate.Size);
        Assert.Equal(["S-1"], plate.StockItemCodes);
        Assert.Equal(0, nest.Drawings.First().Quantity.Remaining);
        Assert.EndsWith(" 1 sheet from MaterialLedger stock.", result.Note);
        Assert.Equal("R1", result.LedgerLocations["S-1"]);
        Assert.Equal((Material, "SYN-1", 0.25), Assert.Single(ledger.StockReads));
    }

    [Fact]
    public async Task CopiesTheLedgerCannotHold_TopUpOnTheNestDefaultsSheet_WithoutACode()
    {
        // Two free 40x40 sheets each hold one 30x30 part; the third copy needs a default sheet.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 40, 40), Sheet("S-2", 40, 40)) };
        var nest = NestOf(30, 3);

        var result = await Release(ledger.Create(), nest);

        Assert.True(result.Nested, result.Note);
        Assert.Equal(0, nest.Drawings.First().Quantity.Remaining);
        var coded = nest.Plates.Where(p => p.StockItemCodes.Count > 0).ToList();
        Assert.Equal(["S-1", "S-2"], coded.SelectMany(p => p.StockItemCodes).Order());
        Assert.All(coded, p => Assert.Equal(new Size(40, 40), p.Size));
        var topUp = Assert.Single(nest.Plates.Where(p => p.StockItemCodes.Count == 0));
        Assert.Equal(new Size(48, 96), topUp.Size);
        Assert.StartsWith("Nested on 3 sheets, ", result.Note);
        Assert.EndsWith(" 2 sheets from MaterialLedger stock.", result.Note);
    }

    [Fact]
    public async Task ALaterNestOfTheSameRun_DoesNotGetASheetAnEarlierNestTook()
    {
        // The release uploads every nest only after all are nested, so the server cannot
        // report the first nest's codes yet; a substitute row can list the same sheet twice.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };
        var run = ledger.Create();
        await Release(run, NestOf(20, 2));

        var second = NestOf(20, 2, grade: "SYN-2");
        var result = await Release(run, second);

        Assert.True(result.Nested, result.Note);
        var plate = Assert.Single(second.Plates);
        Assert.Empty(plate.StockItemCodes);
        Assert.Equal(new Size(48, 96), plate.Size);
        Assert.EndsWith(" MaterialLedger not used: no usable stock of Synthetic Steel SYN-2 0.25 in"
            + " (1 taken by earlier nests of this release).", result.Note);
        Assert.Equal(1, run.Taken["S-1"]);
        Assert.Equal(1, ledger.InUseReads);
    }

    [Fact]
    public async Task ABundleIsSharedSheetBySheet_AcrossNestsOfTheRun()
    {
        var ledger = new FakeLedger { Stock = Stock(Bundle("B-1", 60, 120, 10)) };
        var run = ledger.Create();
        await Release(run, NestOf(20, 2));

        var offer = await run.OfferAsync(NestOf(20, 2, grade: "SYN-2"), CancellationToken.None);

        Assert.Equal(9, Assert.Single(offer.Rows).Quantity);
    }

    [Fact]
    public async Task CodesOnOtherServerNests_AreLeftOut_AndNamedWhenNothingIsLeft()
    {
        var inUse = LedgerCodesInUse.Known(
            [new NestStockCodeUse { ItemCode = "S-1", NestName = "N26-0099" }]);
        var partly = new FakeLedger
        {
            Stock = Stock(Sheet("S-1", 60, 120), Sheet("S-2", 60, 120)),
            InUse = inUse,
        };
        var all = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)), InUse = inUse };

        var offer = await partly.Create().OfferAsync(NestOf(20, 2), CancellationToken.None);
        var none = await all.Create().OfferAsync(NestOf(20, 2), CancellationToken.None);

        Assert.Null(offer.NotUsed);
        Assert.Equal(["S-2"], Assert.Single(offer.Rows).ItemCodes);
        Assert.Equal("no usable stock of Synthetic Steel SYN-1 0.25 in (1 on other nests)",
            none.NotUsed);
    }

    [Fact]
    public async Task NoStockAtAll_IsCalledNoFreeStock()
    {
        var result = await Release(new FakeLedger().Create(), NestOf(20, 2));

        Assert.EndsWith(" MaterialLedger not used: no free stock of Synthetic Steel SYN-1 0.25 in.",
            result.Note);
    }

    [Theory]
    [InlineData("unreachable", "unreachable: No answer within 10 s")]
    [InlineData("unauthorized", "it rejected the API key (Tools > Options)")]
    [InlineData("unmapped", "no mapping for Synthetic Steel SYN-1 0.25 in")]
    [InlineData("not configured", "not set up on this PC (Tools > Options)")]
    public async Task AnUnusableLedger_NestsOnTheNestDefaultsSheet_AndSaysWhy(string state,
        string why)
    {
        LedgerStockResult stock = state switch
        {
            "unreachable" => new LedgerStockResult.Unreachable("No answer within 10 s."),
            "unauthorized" => new LedgerStockResult.Unauthorized(),
            "unmapped" => new LedgerStockResult.Unmapped(),
            _ => new LedgerStockResult.NotConfigured(),
        };
        var ledger = new FakeLedger { Stock = stock };
        var nest = NestOf(20, 2);

        var result = await Release(ledger.Create(), nest);

        Assert.True(result.Nested, result.Note);
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(new Size(48, 96), plate.Size);
        Assert.Empty(plate.StockItemCodes);
        Assert.EndsWith($" MaterialLedger not used: {why}.", result.Note);
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

        var result = await Release(ledger.Create(), NestOf(20, 2));

        Assert.EndsWith(" 1 sheet from MaterialLedger stock. Codes on other nests were not "
            + "checked: the nest server did not answer.", result.Note);
    }

    [Fact]
    public async Task NothingToNest_DoesNotReadTheLedger()
    {
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };

        var result = await Release(ledger.Create(), NestOf(20, 0));

        Assert.Equal((false, NestRenest.NothingToNestNote), (result.Nested, result.Note));
        Assert.Empty(ledger.StockReads);
    }

    [Fact]
    public async Task ARejectedLedgerLayout_FallsToTheNestDefaults_AndSaysTheLedgerWasNotUsed()
    {
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) };
        var nest = NestOf(20, 2);

        var result = await Release(ledger.Create(), nest,
            new OverlapOn(LedgerStockMapper.IsLedgerId));

        Assert.True(result.Nested, result.Note);
        Assert.Equal(new Size(48, 96), Assert.Single(nest.Plates).Size);
        Assert.EndsWith(" MaterialLedger not used: Auto Nest could not keep a layout on its stock.",
            result.Note);
    }

    [Fact]
    public async Task ARejectedDefaultsLayout_ListsEveryCopyItAskedFor()
    {
        // One 40x40 ledger sheet holds one of three copies; the defaults layout never validates.
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 40, 40)) };
        var nest = NestOf(30, 3);

        var result = await Release(ledger.Create(), nest,
            new OverlapOn(id => !LedgerStockMapper.IsLedgerId(id)));

        Assert.True(result.Nested, result.Note);
        Assert.Equal(2, nest.Drawings.First().Quantity.Remaining);
        Assert.Contains(" 2 copies not placed, left to nest: "
            + "SYN-A: 2 not placed (the proposal cannot be kept).", result.Note);
        Assert.EndsWith(" 1 sheet from MaterialLedger stock.", result.Note);
    }

    [Fact]
    public async Task CodesAreGivenInsideTheCommit_BeforeItsUndoStepCloses()
    {
        var ledger = new FakeLedger { Stock = Stock(Sheet("S-1", 60, 120)) }.Create();
        var nest = NestOf(20, 2);
        var codesAtClose = new List<string>();

        await ReleaseRenest.RunAsync(nest, ledger, "Rectangles", 0,
            request => Task.FromResult(NestPipeline.Run(NestingEngineRegistry.Create("Rectangles"),
                "Rectangles", request, null, CancellationToken.None)),
            (result, finish) =>
            {
                var plates = NestPipelineCommit.ApplyToPlates(result, nest.CreatePlate);
                finish(plates);
                // Where the live tab completes its undo step.
                codesAtClose.AddRange(plates.SelectMany(p => p.StockItemCodes));
                return plates;
            },
            CancellationToken.None);

        Assert.Equal(["S-1"], codesAtClose);
    }
}

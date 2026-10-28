using OpenNest.Data.MaterialLedger;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

/// <summary>
/// Before save and post, a nest's codes are checked against the ledger's free stock (plus the
/// nest's own open holds) and other open nests; conflicting codes can be taken off the plates.
/// </summary>
public class LedgerCodeCheckTests
{
    [Fact]
    public void FreeCodesOnNoOtherNest_AreClear()
    {
        var plates = Plates(["S-1"], ["B-1", "B-1"]);

        var conflicts = LedgerCodeCheck.Find(plates, [Item("S-1", 1), Item("B-1", 2)], null,
            LedgerCodesInUse.Known([]));

        Assert.Empty(conflicts);
    }

    [Fact]
    public void TakenCodes_AreReportedPerPlateWithTheirReason()
    {
        var plates = Plates(["GONE"], ["B-1", "B-1"], ["B-1", "OTHER"]);
        var inUse = LedgerCodesInUse.Known([LedgerCodeFilterTests.Use("OTHER", "2026-0042")]);

        var conflicts = LedgerCodeCheck.Find(plates, [Item("B-1", 2), Item("OTHER", 1)], null, inUse);

        Assert.Equal(
        [
            (1, "GONE", "Not free in MaterialLedger"),
            (2, "B-1", "Only 2 of the 3 sheets needed free in MaterialLedger"),
            (3, "B-1", "Only 2 of the 3 sheets needed free in MaterialLedger"),
            (3, "OTHER", "On nest 2026-0042"),
        ], conflicts.Select(c => (c.PlateNumber, c.ItemCode, c.Reason)));
    }

    [Fact]
    public void BothReasons_AreJoined()
    {
        var inUse = LedgerCodesInUse.Known([LedgerCodeFilterTests.Use("S-1", "B"), LedgerCodeFilterTests.Use("S-1", "A")]);

        var conflict = Assert.Single(LedgerCodeCheck.Find(Plates(["S-1"]), [], null, inUse));

        Assert.Equal("Not free in MaterialLedger; on nests A, B", conflict.Reason);
    }

    [Fact]
    public void TheNestsOwnOpenHolds_KeepItsSheets()
    {
        var plates = Plates(["S-1"], ["B-1", "B-1"]);
        LedgerHold[] holds =
        [
            Hold("S-1", LedgerHoldStatuses.Approved),
            Hold("B-1", LedgerHoldStatuses.Proposed),
            Hold("B-1", LedgerHoldStatuses.Cancelled),
        ];

        var conflicts = LedgerCodeCheck.Find(plates, [Item("B-1", 1)], holds, LedgerCodesInUse.Known([]));

        Assert.Empty(conflicts);
    }

    [Fact]
    public void UnknownCodesInUse_CheckTheLedgerAlone()
    {
        var conflicts = LedgerCodeCheck.Find(Plates(["S-1"]), [Item("S-1", 1)], null,
            LedgerCodesInUse.Unknown("nests are saved as files"));

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Remove_TakesEveryCopyOffAndKeepsTheRestInOrder()
    {
        var plates = Plates(["B-1", "S-1", "B-1"], ["S-2"]);

        var removed = LedgerCodeCheck.Remove(plates, ["B-1"]);

        Assert.Equal(2, removed);
        Assert.Equal(["S-1"], plates[0].StockItemCodes);
        Assert.Equal(3, plates[0].Quantity);
        Assert.Equal(["S-2"], plates[1].StockItemCodes);
        Assert.True(LedgerCodeCheck.HasCodes(plates));
        LedgerCodeCheck.Remove(plates, ["S-1", "S-2"]);
        Assert.False(LedgerCodeCheck.HasCodes(plates));
    }

    private static List<Plate> Plates(params string[][] codes) =>
        codes.Select(c =>
        {
            var plate = new Plate(new Size(48, 96)) { Quantity = c.Length };
            plate.StockItemCodes = c;
            return plate;
        }).ToList();

    private static LedgerStockItem Item(string code, int quantity) =>
        new(code, 7, LedgerItemKinds.FullSheet, 48, 96, quantity, LedgerItemShapes.Rectangular, "R1", "Rack 1",
            null, null, DateTime.UtcNow);

    private static LedgerHold Hold(string code, string status) =>
        new(1, code, 48, 96, status, "R1", DateTime.UtcNow);
}

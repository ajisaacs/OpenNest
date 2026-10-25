using System.Globalization;
using OpenNest.CNC;
using OpenNest.Data.MaterialLedger;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class LedgerHoldSummaryTests
{
    [Fact]
    public void SheetCountSumsPlateQuantitiesAndSkipsTheTrailingEmptyPlate()
    {
        var nest = new Nest("N");
        nest.Plates.Add(WithPart(quantity: 3));
        nest.Plates.Add(new Plate { Quantity = 2 }); // Interior: /sheets still lists it.
        nest.Plates.Add(WithPart(quantity: 1));
        nest.Plates.Add(new Plate { Quantity = 5 }); // The editor's new-plate slot.

        Assert.Equal(6, LedgerHoldSummary.PhysicalSheetCount(nest));
        Assert.Equal(0, LedgerHoldSummary.PhysicalSheetCount(new Nest("Empty")));
    }

    [Fact]
    public void AllSheetsHeld()
    {
        var summary = Summarize(4, Hold("Approved"), Hold("Approved"), Hold("Proposed"), Hold("AwaitingCloseout"));
        Assert.Equal(LedgerHoldState.Held, summary.State);
        Assert.Equal("Ledger: 4 of 4 sheets held", summary.Text);
        Assert.Equal(0, summary.ShortSheets);
    }

    // ML's Release lets a hold go: the sheet is free stock again, so the nest is short of it.
    [Fact]
    public void ReleasedClosedAndUnknownHoldsDoNotCountAsHeld()
    {
        var summary = Summarize(4, Hold("Approved"), Hold("Proposed"), Hold("Released"), Hold("ClosedConsumed"),
            Hold("Cancelled"), Hold("Exception"), Hold("OnTruck"));
        Assert.Equal(LedgerHoldState.Short, summary.State);
        Assert.Equal((2, 5, 2), (summary.OpenHolds, summary.ClosedHolds, summary.ShortSheets));
        Assert.Equal("Ledger: 2 of 4 held, 2 short", summary.Text);
    }

    [Fact]
    public void NoHoldsAndNoOpenHolds()
    {
        var none = Summarize(4);
        Assert.Equal(LedgerHoldState.None, none.State);
        Assert.Equal("Ledger: no holds", none.Text);

        var closed = Summarize(2, Hold("ClosedConsumed"), Hold("ClosedReturned"));
        Assert.Equal(LedgerHoldState.None, closed.State);
        Assert.Equal("Ledger: no open holds (2 closed)", closed.Text);
    }

    [Fact]
    public void MoreHoldsThanSheetsIsHeldWithExtras()
    {
        var summary = Summarize(1, Hold("Approved"), Hold("Approved"));
        Assert.Equal(LedgerHoldState.Held, summary.State);
        Assert.Equal("Ledger: 1 of 1 sheets held, 1 extra", summary.Text);
    }

    [Fact]
    public void FailuresAreUnavailableAndNotConfiguredShowsNothing()
    {
        Assert.Null(LedgerHoldSummary.From(new LedgerHoldsResult.NotConfigured(), 4));

        var down = LedgerHoldSummary.From(new LedgerHoldsResult.Unreachable("timed out"), 4)!;
        Assert.Equal((LedgerHoldState.Unavailable, "Ledger: unavailable", "timed out"), (down.State, down.Text, down.Reason));

        var rejected = LedgerHoldSummary.From(new LedgerHoldsResult.Unauthorized(), 4)!;
        Assert.Equal(LedgerHoldState.Unavailable, rejected.State);
        Assert.Contains("API key", rejected.Reason);
    }

    [Fact]
    public void ToolTipListsHoldsInNestUnits()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var summary = Summarize(2, new LedgerHold(91, "DROP-1182", 30, 52.5m, "AwaitingCloseout", "B3", default),
                new LedgerHold(92, "FS-1", 48, 96, "Approved", null, default));

            Assert.Equal("Ledger: 2 of 2 sheets held" + Environment.NewLine
                + "DROP-1182  30 x 52.5 in  B3  Awaiting Closeout" + Environment.NewLine
                + "FS-1  48 x 96 in  Approved", summary.ToolTip(Units.Inches));
            Assert.Equal("DROP-1182  762 x 1333.5 mm  B3  Awaiting Closeout",
                LedgerHoldSummary.FormatHold(summary.Holds[0], Units.Millimeters));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static LedgerHoldSummary Summarize(int sheets, params LedgerHold[] holds) =>
        LedgerHoldSummary.From(new LedgerHoldsResult.Ok(holds), sheets)!;

    private static LedgerHold Hold(string status) => new(1, "X", 48, 96, status, "A1", default);

    private static Plate WithPart(int quantity)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 0)));
        program.Codes.Add(new LinearMove(new Vector(10, 10)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        var plate = new Plate { Quantity = quantity };
        plate.Parts.Add(new Part(new Drawing("test", program)));
        return plate;
    }
}

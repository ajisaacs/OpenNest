using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Save and post re-check a nest's ledger item codes: taken codes are listed and the
/// programmer goes on, removes them (one undoable edit) or cancels; a ledger that cannot be
/// read never blocks.
/// </summary>
public class LedgerCodeRecheckTests
{
    [Fact]
    public void Proceed_KeepsTheCodesAndRecordsNothing() => UndoTestEnv.Run(env =>
    {
        var fake = new Fakes { Answer = LedgerCodeRecheck.Choice.Proceed };
        var before = UndoTestEnv.Describe(env.Nest);
        var count = env.Editor.History.UndoCount;

        Assert.True(Confirm(fake, env, "Post"));

        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
        Assert.Equal(count, env.Editor.History.UndoCount);
        Assert.Equal("Post", fake.AskedAction);
        Assert.Equal(
            [(1, "B-1", "Only 1 of the 2 sheets needed free in MaterialLedger"), (2, "S-1", "On nest 2026-0042")],
            fake.Asked!.Select(c => (c.PlateNumber, c.ItemCode, c.Reason)));
    }, CodedNest);

    [Fact]
    public void RemoveCodes_IsOneUndoableEditThatKeepsTheLayout() => UndoTestEnv.Run(env =>
    {
        var fake = new Fakes { Answer = LedgerCodeRecheck.Choice.RemoveCodes };
        var parts = env.Nest.Plates.Sum(p => p.Parts.Count);

        env.AssertOneUndoableEdit(LedgerCodeRecheck.RemoveLabel, () => Assert.True(Confirm(fake, env, "Save")));

        Assert.Equal(parts, env.Nest.Plates.Sum(p => p.Parts.Count));
        Assert.Equal(2, env.Nest.Plates[0].Quantity);
        Assert.Empty(env.Nest.Plates[0].StockItemCodes);
        Assert.Equal(["KEEP-1"], env.Nest.Plates[3].StockItemCodes);
        Assert.Equal("Removed 2 ledger item codes from 3 sheets", fake.Status);
    }, CodedNest);

    [Fact]
    public void Cancel_StopsTheAttemptAndChangesNothing() => UndoTestEnv.Run(env =>
    {
        var fake = new Fakes { Answer = LedgerCodeRecheck.Choice.Cancel };
        var before = UndoTestEnv.Describe(env.Nest);

        Assert.False(Confirm(fake, env, "Save"));

        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
        Assert.Equal("Save canceled", fake.Status);
    }, CodedNest);

    [Fact]
    public void UnreachableLedger_SkipsTheCheckWithANote() => UndoTestEnv.Run(env =>
    {
        var fake = new Fakes { Stock = new LedgerStockResult.Unreachable("No answer within 5 s.") };

        Assert.True(Confirm(fake, env, "Save"));

        Assert.Null(fake.Asked);
        Assert.Equal("Ledger codes not re-checked: MaterialLedger unreachable (No answer within 5 s.)", fake.Status);

        var failing = new Fakes { StockError = new HttpRequestException("refused") };
        Assert.True(Confirm(failing, env, "Post"));
        Assert.Null(failing.Asked);
        Assert.Equal("Ledger codes not re-checked: refused", failing.Status);
    }, CodedNest);

    [Fact]
    public void NoLedgerOrNoCodes_ChecksNothing() => UndoTestEnv.Run(env =>
    {
        var unconfigured = new Fakes { Configured = false };
        Assert.True(Confirm(unconfigured, env, "Save"));
        Assert.Equal(0, unconfigured.Fetches);
        Assert.Null(unconfigured.Status);

        foreach (var plate in env.Nest.Plates)
            plate.StockItemCodes = [];
        var uncoded = new Fakes();
        Assert.True(Confirm(uncoded, env, "Save"));
        Assert.Equal(0, uncoded.Fetches);
    }, CodedNest);

    [Fact]
    public void OtherNestsUnknown_ChecksTheLedgerAloneAndSaysSo() => UndoTestEnv.Run(env =>
    {
        var fake = new Fakes
        {
            Stock = Free(("B-1", 2), ("S-1", 1), ("KEEP-1", 1)),
            InUse = LedgerCodesInUse.Unknown("nests are saved as files"),
        };

        Assert.True(Confirm(fake, env, "Save"));

        Assert.Null(fake.Asked);
        Assert.Equal("Ledger codes checked against MaterialLedger only; other nests not checked: nests are saved as files", fake.Status);
    }, CodedNest);

    [Fact]
    public void Dialog_GoesOnOnlyAfterTheAcknowledgement() => StaTestThread.Run(() =>
    {
        LedgerCodeConflict[] conflicts = [new(1, "B-1", "Not free in MaterialLedger")];
        using (var form = new LedgerCodeConflictForm("Post", conflicts))
        {
            form.Show();
            var proceed = Find<Button>(form, "proceedButton");
            Assert.Equal("Post Anyway", proceed.Text);
            Assert.False(proceed.Enabled);
            Assert.Single(Find<ListView>(form, "conflictList").Items);

            Find<CheckBox>(form, "acknowledgeBox").Checked = true;
            Assert.True(proceed.Enabled);
            proceed.PerformClick();
            Assert.Equal(LedgerCodeRecheck.Choice.Proceed, form.Choice);
        }

        using (var form = new LedgerCodeConflictForm("Save", conflicts))
        {
            form.Show();
            Find<Button>(form, "removeButton").PerformClick();
            Assert.Equal(LedgerCodeRecheck.Choice.RemoveCodes, form.Choice);
        }

        using (var form = new LedgerCodeConflictForm("Save", conflicts))
        {
            form.Show();
            Find<Button>(form, "cancelButton").PerformClick();
            Assert.Equal(LedgerCodeRecheck.Choice.Cancel, form.Choice);
        }
    }, TimeSpan.FromSeconds(15), "The STA test did not complete.");

    private static bool Confirm(Fakes fake, UndoTestEnv env, string action) =>
        fake.Create().ConfirmAsync(env.Host, env.Editor, action).GetAwaiter().GetResult();

    /// <summary>
    /// The undo nest with a material: plate 1 cuts two sheets of bundle B-1, plate 2 is on S-1,
    /// plate 4 on KEEP-1 (free and on no other nest).
    /// </summary>
    private static Nest CodedNest()
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Material = new Material("Steel", "A36");
        nest.Thickness = 0.25;
        nest.Plates[0].Quantity = 2;
        nest.Plates[0].StockItemCodes = ["B-1", "B-1"];
        nest.Plates[1].StockItemCodes = ["S-1"];
        nest.Plates[3].StockItemCodes = ["KEEP-1"];
        return nest;
    }

    private static LedgerStockResult Free(params (string Code, int Quantity)[] items) =>
        new LedgerStockResult.Ok(Array.Empty<LedgerStockSpec>(), items.Select(i =>
            new LedgerStockItem(i.Code, 7, LedgerItemKinds.FullSheet, 48, 96, i.Quantity,
                LedgerItemShapes.Rectangular, "R1", "Rack 1", null, null, DateTime.UtcNow)).ToList());

    private static T Find<T>(Form form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private sealed class Fakes
    {
        public bool Configured { get; init; } = true;
        public LedgerStockResult Stock { get; init; } = Free(("B-1", 1), ("S-1", 1), ("KEEP-1", 1));
        public Exception? StockError { get; init; }
        public LedgerCodesInUse InUse { get; init; } = LedgerCodesInUse.Known(
            [new NestStockCodeUse { ItemCode = "S-1", NestId = Guid.NewGuid(), NestName = "2026-0042" }]);
        public LedgerCodeRecheck.Choice Answer { get; init; }
        public int Fetches { get; private set; }
        public string? AskedAction { get; private set; }
        public IReadOnlyList<LedgerCodeConflict>? Asked { get; private set; }
        public string? Status { get; private set; }

        public LedgerCodeRecheck Create() => new(
            () => Configured,
            (_, _, _, _) =>
            {
                Fetches++;
                return StockError != null ? Task.FromException<LedgerStockResult>(StockError) : Task.FromResult(Stock);
            },
            (_, _) => Task.FromResult<LedgerHoldsResult>(new LedgerHoldsResult.Ok(Array.Empty<LedgerHold>())),
            (_, _) => Task.FromResult(InUse),
            (_, action, conflicts) =>
            {
                AskedAction = action;
                Asked = conflicts;
                return Answer;
            },
            text => Status = text);
    }
}

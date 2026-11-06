using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Forms;

// Make Nests nesting live in the main window. Reads Application.OpenForms through MainForm,
// so it must not overlap fill-lifetime tests.
[Collection("Fill operation lifetime")]
public class MainFormReleaseNestingTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public void EachNestNestsInItsOwnTab_ASavedOneIsBoundNotReopened_AndAnUnsavedOneCloses() => StaTestThread.Run(() =>
    {
        using var main = new Host();
        main.SolveReleasedNest = (_, request, cts) =>
            Task.Run(() => NestPipeline.Run(NestingEngineRegistry.Create("Rectangles"), "Rectangles", request, null, cts.Token));
        main.Show();
        IReleaseNestingHost host = main;
        var saved = NestOf("N26-0001", 6);
        var unsaved = NestOf("N26-0002", 2);

        host.Begin();
        var first = Run(host.RenestAsync(saved, null, CancellationToken.None));
        var tab = Assert.Single(Tabs(main));
        Assert.Same(saved, tab.Nest);
        Assert.True(first.Nested, first.Note);
        Assert.StartsWith("Nested on 1 sheet", first.Note);
        Assert.Equal(6, saved.Plates.Sum(p => p.Parts.Count * p.Quantity));
        Assert.Equal(Guid.Empty, tab.Document.RemoteId);
        // The kept layout is one undoable step in the tab; undo waits for the release.
        Assert.Equal(1, tab.History.UndoCount);
        Assert.Equal("Auto Nest", tab.History.UndoLabel);
        Assert.False(main.TryUndo(tab));
        Run(host.RenestAsync(unsaved, null, CancellationToken.None));
        Assert.Equal(2, Tabs(main).Count);
        var unsavedTab = Tabs(main).Single(f => ReferenceEquals(f.Nest, unsaved));
        Assert.True(unsavedTab.HasUnsavedChanges);

        // Make Nests opens what it saved: the tab already showing it is bound instead, and is
        // saved as it stands, so closing it later saves no second revision.
        var record = new NestRecord { Id = Guid.NewGuid(), Revision = 1, Name = saved.Name };
        main.OpenServerNest(saved, record);
        Assert.Equal(2, Tabs(main).Count);
        Assert.Equal((record.Id, 1), (tab.Document.RemoteId, tab.Document.RemoteRevision));
        Assert.False(tab.HasUnsavedChanges);
        Assert.Equal(saved.Name, tab.Text);

        // The nest it did not save closes without asking: Save would be refused for it.
        var prompts = new List<string>();
        var previous = EditNestForm.UnsavedChangesPrompt;
        EditNestForm.UnsavedChangesPrompt = form =>
        {
            prompts.Add(form.Nest.Name);
            return DialogResult.Yes;
        };
        try
        {
            host.End(null!);
            Application.DoEvents();
        }
        finally
        {
            EditNestForm.UnsavedChangesPrompt = previous;
        }
        Assert.Empty(prompts);
        Assert.Same(tab, Assert.Single(Tabs(main)));
    }, TestTimeout, "The live release nesting test did not complete.");

    [Fact]
    public void StoppingTheAutoNestStopsTheRelease_AndLeavesNoLayout() => StaTestThread.Run(() =>
    {
        using var main = new Host();
        main.SolveReleasedNest = (_, _, cts) =>
        {
            // The progress window's Stop.
            cts.Cancel();
            return Task.FromException<NestPipelineResult>(new OperationCanceledException(cts.Token));
        };
        main.Show();
        IReleaseNestingHost host = main;
        var nest = NestOf("N26-0003", 4);

        host.Begin();
        var renest = host.RenestAsync(nest, null, CancellationToken.None);
        PumpUntil(() => renest.IsCompleted);

        Assert.True(renest.IsCanceled || renest.Exception?.InnerException is OperationCanceledException);
        Assert.DoesNotContain(nest.Plates, p => p.Parts.Count > 0);
        host.End(null!);
        Application.DoEvents();
        Assert.Empty(Tabs(main));
    }, TestTimeout, "The live release stop test did not complete.");

    [Fact]
    public void LedgerStockIsNestedFirst_InTheTab_ThenTheRestOnTheNestDefaultsSheet() =>
        StaTestThread.Run(() =>
    {
        using var main = new Host();
        main.SolveReleasedNest = (_, request, cts) => Task.Run(() => NestPipeline.Run(
            NestingEngineRegistry.Create("Rectangles"), "Rectangles", request, null, cts.Token));
        main.Show();
        IReleaseNestingHost host = main;
        // One free 10x10 ledger sheet holds one 8x8 part; the second copy tops up on 48x96.
        var nest = NestOf("N26-0004", 2, partSize: 8);
        nest.Material = new Material("Synthetic Steel", "SYN-1");
        nest.Thickness = 0.25;
        var ledger = new ReleaseLedgerStock(
            (_, _, _, _) => Task.FromResult<LedgerStockResult>(new LedgerStockResult.Ok(
                Array.Empty<LedgerStockSpec>(),
                [new LedgerStockItem("S-1", 1, LedgerItemKinds.FullSheet, 10, 10, 1,
                    LedgerItemShapes.Rectangular, "B3", "Bay 3", null, null, DateTime.UnixEpoch)])),
            _ => Task.FromResult(LedgerCodesInUse.Known(Array.Empty<NestStockCodeUse>())));

        host.Begin();
        var renest = Run(host.RenestAsync(nest, ledger, CancellationToken.None));

        var tab = Assert.Single(Tabs(main));
        Assert.True(renest.Nested, renest.Note);
        Assert.EndsWith(" 1 sheet from MaterialLedger stock.", renest.Note);
        var coded = Assert.Single(nest.Plates, p => p.StockItemCodes.Count > 0);
        Assert.Equal((new Size(10, 10), "S-1"), (coded.Size, Assert.Single(coded.StockItemCodes)));
        Assert.Contains(nest.Plates, p => p.Parts.Count > 0 && p.Size.Equals(new Size(48, 96))
            && p.StockItemCodes.Count == 0);
        Assert.Equal(2, nest.Plates.Sum(p => p.Parts.Count * p.Quantity));
        Assert.Equal("B3", tab.LedgerLocations["S-1"]);
        // Each stage is its own Auto Nest step in the tab, and its codes are part of that step.
        Assert.Equal(2, tab.History.UndoCount);
        tab.Undo();
        tab.Undo();
        tab.Redo();
        // Redone to the ledger step alone: its own recorded state carries the code.
        var redone = Assert.Single(tab.Nest.Plates, p => p.Parts.Count > 0);
        Assert.Equal(new Size(10, 10), redone.Size);
        Assert.Equal("S-1", Assert.Single(redone.StockItemCodes));
        tab.Redo();
        Assert.Single(tab.Nest.Plates, p => p.StockItemCodes.Count > 0);
        host.End(null!);
        Application.DoEvents();
    }, TestTimeout, "The live release ledger test did not complete.");

    private static Nest NestOf(string name, int quantity, double partSize = 0)
    {
        var nest = new Nest(name) { Units = Units.Inches };
        nest.PlateDefaults.Size = new Size(48, 96);
        var drawing = new RectangleShape
        {
            Name = name + "-A",
            Length = partSize > 0 ? partSize : 4,
            Width = partSize > 0 ? partSize : 2,
        }.GetDrawing();
        drawing.Quantity.Required = quantity;
        nest.Drawings.Add(drawing);
        return nest;
    }

    private static List<EditNestForm> Tabs(MainForm main) =>
        main.MdiChildren.OfType<EditNestForm>().Where(f => !f.IsDisposed).ToList();

    private static T Run<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "Timed out.");
    }

    private sealed class Host : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}

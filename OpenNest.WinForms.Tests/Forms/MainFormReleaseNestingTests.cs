using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
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
        var first = Run(host.RenestAsync(saved, CancellationToken.None));
        var tab = Assert.Single(Tabs(main));
        Assert.Same(saved, tab.Nest);
        Assert.True(first.Nested, first.Note);
        Assert.StartsWith("Nested on 1 sheet", first.Note);
        Assert.Equal(6, saved.Plates.Sum(p => p.Parts.Count * p.Quantity));
        Assert.Equal(Guid.Empty, tab.Document.RemoteId);
        Run(host.RenestAsync(unsaved, CancellationToken.None));
        Assert.Equal(2, Tabs(main).Count);

        // Make Nests opens what it saved: the tab already showing it is bound instead.
        var record = new NestRecord { Id = Guid.NewGuid(), Revision = 1, Name = saved.Name };
        main.OpenServerNest(saved, record);
        Assert.Equal(2, Tabs(main).Count);
        Assert.Equal((record.Id, 1), (tab.Document.RemoteId, tab.Document.RemoteRevision));

        host.End(null!);
        Application.DoEvents();
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
        var renest = host.RenestAsync(nest, CancellationToken.None);
        PumpUntil(() => renest.IsCompleted);

        Assert.True(renest.IsCanceled || renest.Exception?.InnerException is OperationCanceledException);
        Assert.DoesNotContain(nest.Plates, p => p.Parts.Count > 0);
        host.End(null!);
        Application.DoEvents();
        Assert.Empty(Tabs(main));
    }, TestTimeout, "The live release stop test did not complete.");

    private static Nest NestOf(string name, int quantity)
    {
        var nest = new Nest(name) { Units = Units.Inches };
        nest.PlateDefaults.Size = new Size(48, 96);
        var drawing = new RectangleShape { Name = name + "-A", Length = 4, Width = 2 }.GetDrawing();
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

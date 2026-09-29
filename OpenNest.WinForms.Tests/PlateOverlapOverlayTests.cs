using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

[CollectionDefinition("Overlap overlay", DisableParallelization = true)]
public class OverlapOverlayCollection;

[Collection("Overlap overlay")]
public class PlateOverlapOverlayTests
{
    [Fact]
    public void RestartAndCancelRejectLateCompletionsOnTheUiThread() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var uiThread = Environment.CurrentManagedThreadId;
        run.View.OverlapStateChanged += (_, _) => Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
        var first = run.Start();
        var old = run.Next();
        var second = run.Start();
        var current = run.Next();
        Assert.True(old.Token.IsCancellationRequested);
        old.Complete();
        run.Pump(first);
        Assert.Equal(OverlapCheckStatus.Checking, run.View.OverlapStatus);
        current.Complete();
        run.Pump(second);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
        Assert.Single(run.View.OverlapReport.Pairs);

        var canceled = run.Start();
        var late = run.Next();
        run.View.CancelOverlapCheck();
        Assert.True(late.Token.IsCancellationRequested);
        late.Complete();
        run.Pump(canceled);
        Assert.Equal(OverlapCheckStatus.Canceled, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
    });

    [Theory]
    [InlineData("move")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("reorder")]
    [InlineData("editor")]
    [InlineData("plate")]
    public void EditOrPlateSwitchRejectsPendingReport(string edit) => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        var work = run.Next();
        var plate = run.View.Plate;
        switch (edit)
        {
            case "move": plate.Parts[0].Offset(1e-10, 0); break;
            case "add": plate.Parts.Add(Rectangle()); break;
            case "remove": plate.Parts.RemoveAt(0); break;
            case "replace": plate.Parts[0] = Rectangle(); break;
            case "reorder": (plate.Parts[0], plate.Parts[1]) = (plate.Parts[1], plate.Parts[0]); break;
            case "editor": run.View.InvalidateOverlapCheck(); break;
            case "plate": run.View.Plate = new Plate(); break;
        }
        if (edit != "move")
            Assert.True(work.Token.IsCancellationRequested);
        work.Complete();
        run.Pump(task);
        Assert.True(work.Token.IsCancellationRequested);
        Assert.Null(run.View.OverlapReport);
        Assert.Equal(edit == "plate" ? OverlapCheckStatus.NotChecked : OverlapCheckStatus.Stale,
            run.View.OverlapStatus);
    });

    [Fact]
    public void PaintChecksFreshnessEvenWhenDisplayIsOff() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(240, 240);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.View.Plate.Parts[0].Offset(1e-10, 0);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
    });

    [Fact]
    public void DisplayPanZoomAndPreviewDoNotAnalyzeOrChangeCommittedParts() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var parts = run.View.Plate.Parts.ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var quantities = parts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        run.View.SelectAll();
        var selection = run.View.SelectedParts.ToArray();
        run.View.SetActiveParts(new List<Part> { Rectangle() });
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.Finish();
        Assert.Equal(OverlapDisplayMode.Areas, run.View.OverlapDisplay);
        Assert.Single(run.View.OverlapReport.Pairs); // Preview is not a third input.
        var report = run.View.OverlapReport;
        using var image = new Bitmap(240, 240);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        var path = run.View.OverlapOverlay.CachedPath;
        Assert.Equal(FillMode.Winding, path.FillMode);
        run.View.Pan(35, -20);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Same(path, run.View.OverlapOverlay.CachedPath);
        run.View.OverlapDisplay = OverlapDisplayMode.Off;
        run.View.OverlapDisplay = OverlapDisplayMode.Areas;
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Same(path, run.View.OverlapOverlay.CachedPath);
        run.View.ZoomToPoint(new Vector(), 2);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotSame(path, run.View.OverlapOverlay.CachedPath);
        Assert.Same(report, run.View.OverlapReport);
        Assert.Equal(1, run.Calls);
        Assert.Equal(parts, run.View.Plate.Parts.ToArray());
        Assert.Equal(locations, parts.Select(p => p.Location));
        Assert.Equal(programs, parts.Select(p => p.Program));
        Assert.Equal(quantities, parts.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.Equal(selection, run.View.SelectedParts);
    });

    [Fact]
    public void TripleOverlapUsesOneFillAndGraphCoordinatesDoNotDoublePan() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.View.Plate.Parts.Clear();
        run.View.Plate.Parts.Add(Rectangle());
        run.View.Plate.Parts.Add(Rectangle(2));
        run.View.Plate.Parts.Add(Rectangle(3));
        run.Finish();
        run.View.ZoomToPoint(new Vector(), 20);
        run.View.SetOrigin(60, 220);
        using var image = new Bitmap(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.TranslateTransform(60, 220);
        var transform = graphics.Transform.Elements;
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Equal(transform, graphics.Transform.Elements); // Label restores graph space.
        Assert.Equal(image.GetPixel(110, 180), image.GetPixel(130, 180)); // Double/triple material, same alpha.
        Assert.NotEqual(Color.White.ToArgb(), image.GetPixel(130, 180).ToArgb());
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(70, 180).ToArgb());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleDestructionOrDisposalNeverResurrectsRequests(bool dispose) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        var task = run.Start();
        var work = run.Next();
        if (dispose)
            run.View.Dispose();
        else
            run.View.Recreate();
        Assert.True(work.Token.IsCancellationRequested);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        work.Complete();
        run.Pump(task);
        Assert.Null(run.View.OverlapReport);
        Assert.False(run.View.IsOverlapCheckRunning);
    });

    [Fact]
    public void CompletedPathIsReleasedOnHandleRecreationAndCannotReappear() => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        run.View.Recreate();
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
        Assert.Null(run.View.OverlapReport);
        Assert.Equal(1, run.Calls);
    });

    [Fact]
    public void FailureAndIncompleteAreVisibleAndNeverReportedAsClear() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        run.Next().Fail();
        run.Pump(task);
        Assert.Equal(OverlapCheckStatus.Failed, run.View.OverlapStatus);
        Assert.Contains("failed", run.View.Status);
        Assert.Null(run.View.OverlapReport);
        run.View.Plate.Parts.Add(new Part(new Drawing("invalid", new Program())));
        run.Finish();
        Assert.Equal(OverlapCheckStatus.Incomplete, run.View.OverlapStatus);
        Assert.Contains("incomplete: 1 overlapping pairs; 1 parts", run.View.Status);
    });

    [Fact]
    public void SnapshotKeepsOriginalLabelsWhenLiveNamesChange() => RunSta(() =>
    {
        using var run = new OverlayRun();
        var task = run.Start();
        var work = run.Next();
        run.View.Plate.Parts[0].BaseDrawing.Name = "new label";
        work.Complete();
        run.Pump(task);
        Assert.Equal("same name", Assert.Single(run.View.OverlapReport.Pairs).PartAName);
        Assert.Equal(OverlapCheckStatus.Current, run.View.OverlapStatus);
    });

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    public void CollectionEventsImmediatelyDiscardCurrentReportAndPath(string edit) => RunSta(() =>
    {
        using var run = new OverlayRun();
        run.Finish();
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        run.View.OverlapOverlay.Draw(graphics);
        Assert.NotNull(run.View.OverlapOverlay.CachedPath);
        switch (edit)
        {
            case "add": run.View.Plate.Parts.Add(Rectangle()); break;
            case "remove": run.View.Plate.Parts.RemoveAt(0); break;
            case "replace": run.View.Plate.Parts[0] = Rectangle(); break;
            case "clear": run.View.Plate.Parts.Clear(); break;
        }
        Assert.Equal(OverlapCheckStatus.Stale, run.View.OverlapStatus);
        Assert.Null(run.View.OverlapReport);
        Assert.Null(run.View.OverlapOverlay.CachedPath);
    });

    [Fact]
    public void MenusFollowMdiActivationDocumentDisplayAndRunningState() => RunSta(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var check = Menu(host, "mnuOverlapCheckActive");
        var cancel = Menu(host, "mnuOverlapCancel");
        var off = Menu(host, "mnuOverlapOff");
        var areas = Menu(host, "mnuOverlapAreas");
        Assert.False(check.Enabled);
        Assert.False(cancel.Enabled);
        Assert.False(off.Enabled);
        Assert.False(areas.Enabled);
        Assert.All(new[] { check, cancel, off, areas }, item => Assert.Equal(Keys.None, item.ShortcutKeys));

        using var first = new EditNestForm(new Nest("first")) { MdiParent = host };
        first.Show();
        Assert.True(check.Enabled);
        Assert.True(areas.Checked);
        off.PerformClick();
        Assert.Equal(OverlapDisplayMode.Off, first.OverlapDisplay);
        using var second = new EditNestForm(new Nest("second")) { MdiParent = host };
        second.Show();
        Assert.True(areas.Checked);
        Assert.False(off.Checked);
        first.Activate();
        Assert.True(off.Checked);
        Assert.False(areas.Checked);
        Assert.Equal(OverlapDisplayMode.Areas, second.OverlapDisplay);

        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        using var pending = new BlockingCollection<Work>();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            first.PlateView.OverlapOverlay.Analyze = (snapshot, token) =>
            {
                var request = new Work(snapshot, token);
                pending.Add(request);
                return request.WaitForResult();
            };
            var task = first.CheckOverlapsAsync();
            Assert.True(pending.TryTake(out var request, TimeSpan.FromSeconds(15)));
            Assert.True(cancel.Enabled);
            Assert.True(areas.Checked); // Checking from Off selects Areas.
            cancel.PerformClick();
            Assert.False(cancel.Enabled);
            Assert.True(request!.Token.IsCancellationRequested);
            request.Complete();
            context.Pump(task);
            Assert.Equal(OverlapCheckStatus.Canceled, first.PlateView.OverlapStatus);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        first.Dispose();
        second.Dispose();
        Assert.False(check.Enabled);
        Assert.False(cancel.Enabled);
    });

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    private sealed class MenuHost : MainForm
    {
        // Do not run startup migrations/automatic new-document creation or save test window settings.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }

    private sealed class TestView : PlateView
    {
        public void Pan(float x, float y) { origin.X += x; origin.Y += y; }
        public void SetOrigin(float x, float y) => origin = new PointF(x, y);
        public void Recreate() => RecreateHandle();
    }

    private sealed class OverlayRun : IDisposable
    {
        private readonly PumpContext context = new();
        private readonly SynchronizationContext? previous;
        private readonly BlockingCollection<Work> work = new();
        public TestView View { get; } = new();
        public int Calls;

        public OverlayRun()
        {
            View.CreateControl();
            previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            View.Plate.Parts.Add(Rectangle());
            View.Plate.Parts.Add(Rectangle(1));
            View.OverlapOverlay.Analyze = (snapshot, token) =>
            {
                Interlocked.Increment(ref Calls);
                var request = new Work(snapshot, token);
                work.Add(request);
                return request.WaitForResult();
            };
        }

        public Task Start() => View.CheckOverlapsAsync();
        public Work Next()
        {
            Assert.True(work.TryTake(out var result, TimeSpan.FromSeconds(15)), "Worker did not start.");
            return result!;
        }
        public void Pump(Task task) => context.Pump(task);
        public void Finish()
        {
            var task = Start();
            Next().Complete();
            Pump(task);
        }
        public void Dispose()
        {
            View.Dispose();
            SynchronizationContext.SetSynchronizationContext(previous);
            work.Dispose();
        }
    }

    private sealed class Work(PlateOverlapSnapshot snapshot, CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<PlateOverlapReport> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only called by Task.Run workers; the STA supplies the result and pumps continuations.
        public PlateOverlapReport WaitForResult() => Result.Task.GetAwaiter().GetResult();
        public void Complete() => Result.SetResult(PlateOverlapAnalyzer.Analyze(snapshot));
        public void Fail() => Result.SetException(new InvalidOperationException("test failure"));
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<System.Action> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add(() => callback(state));
        public void Pump(Task task)
        {
            while (!task.IsCompleted)
            {
                Assert.True(callbacks.TryTake(out var callback, TimeSpan.FromSeconds(15)), "UI continuation did not arrive.");
                callback!();
            }
            task.GetAwaiter().GetResult();
        }
    }

    private static Part Rectangle(double x = 0)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("same name", program), new Vector(x, 0));
    }

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

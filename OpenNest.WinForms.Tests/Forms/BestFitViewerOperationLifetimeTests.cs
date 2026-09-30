using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.Engine.BestFit;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

[Collection("Fill operation lifetime")]
public class BestFitViewerOperationLifetimeTests
{
    [Fact]
    public void ShownAndSelectionPublishCompleteResultsWithPagingAndSourceSelection() => RunSta(() =>
    {
        using var run = new ViewerRun();
        var first = run.Queue(run.Drawings.ElementAt(0), 16);
        run.Form.Show();
        Application.DoEvents(); // Deliver WinForms' posted Shown event before blocking for the worker.
        first.WaitForEntry();
        run.FinishEvents(first);
        Assert.Equal(15, run.Grid.Controls.Count);
        Assert.Contains("16 candidates (16 kept)", run.Form.Text);
        Assert.Equal("1", run.Control("txtPage").Text);
        Invoke(run.Form, "NavigatePage", 1);
        Assert.Single(run.Grid.Controls.Cast<Control>());
        Assert.Equal("2", run.Control("txtPage").Text);

        var second = run.Queue(run.Drawings.ElementAt(1), 1);
        ((DrawingListBox)run.Control("drawingListBox")).SelectedIndex = 1;
        second.WaitForEntry();
        run.FinishEvents(second);
        Assert.Same(run.Drawings.ElementAt(1), second.Input.Drawing);
        Assert.Equal((120d, 36d, 0.5d), (second.Input.Length, second.Input.Width, second.Input.Spacing));
        Assert.Equal("1", run.Control("txtPage").Text);
        var cell = Assert.Single(run.Grid.Controls.OfType<BestFitCell>());
        typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cell, [EventArgs.Empty]);
        Assert.Same(second.Result.Results[0], run.Form.SelectedResult);
        Assert.Same(run.Drawings.ElementAt(1), run.Form.SelectedDrawing);
        Assert.Equal(2, run.Form.SelectedParts.Count);
        Assert.All(run.Form.SelectedParts, part => Assert.Same(run.Drawings.ElementAt(1), part.BaseDrawing));
        Assert.Equal(DialogResult.OK, run.Form.DialogResult);
        Assert.Empty(run.Form.Notices);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentComputeOrRenderFailureReportsOnceAndClearsPartialAndPreviousResults(bool renderFailure) => RunSta(() =>
    {
        using var run = new ViewerRun();
        var previous = run.Queue(run.Drawings.ElementAt(0), 2);
        run.Finish(run.Start(previous), previous);
        Assert.Equal(2, run.Grid.Controls.Count);
        var work = run.Queue(run.Drawings.ElementAt(0), 1);
        var error = new InvalidOperationException("Injected Best-Fit failure");
        if (renderFailure)
            run.Form.RenderFailure = error;
        else
            work.Error = error;
        var task = run.Start(work);
        var source = run.Source;
        run.Finish(task, work);
        var notice = Assert.Single(run.Form.Notices);
        Assert.Same(error, notice.Error);
        Assert.Same(run.Drawings.ElementAt(0), notice.Drawing);
        Assert.Empty(run.Grid.Controls.Cast<Control>());
        Assert.Equal(0, Field<int>(run.Form, "pageCount"));
        Assert.Null(Field<List<BestFitResult>?>(run.Form, "results"));
        Assert.True(run.Control("btnPrev").Enabled);
        Assert.True(run.Control("btnNext").Enabled);
        Assert.True(run.Control("txtPage").Enabled);
        Assert.Equal(Cursors.Default, run.Form.Cursor);
        Assert.Contains("Could not load results", run.Form.Text);
        Assert.Null(run.Source);
        AssertDisposed(source);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyRequestedCancellationIsExpectedAndNonmodal(bool cancel) => RunSta(() =>
    {
        using var run = new ViewerRun();
        var work = run.Queue(run.Drawings.ElementAt(0), 1);
        work.Error = new OperationCanceledException("Injected cancellation");
        var task = run.Start(work);
        var source = run.Source!;
        if (cancel)
            source.Cancel();
        run.Finish(task, work);
        Assert.Equal(cancel ? 0 : 1, run.Form.Notices.Count);
        Assert.True(run.Control("txtPage").Enabled);
        AssertDisposed(source);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OldCompletionCannotPublishReportOrResetNewerOperation(bool failOld, bool oldCompletesFirst) => RunSta(() =>
    {
        using var run = new ViewerRun();
        var old = run.Queue(run.Drawings.ElementAt(0), 2);
        if (failOld)
            old.Error = new InvalidOperationException("Stale failure");
        var oldTask = run.Start(old);
        var oldSource = run.Source!;
        var newer = run.Queue(run.Drawings.ElementAt(0), 1);
        var newTask = run.Start(newer);
        var newSource = run.Source!;
        Assert.True(oldSource.IsCancellationRequested);
        Assert.True(oldSource.Token.IsCancellationRequested); // Not disposed while its worker still runs.
        Assert.False(newSource.IsCancellationRequested);
        if (oldCompletesFirst)
        {
            run.Finish(oldTask, old);
            Assert.Same(newSource, run.Source);
            Assert.False(run.Control("txtPage").Enabled);
            Assert.Equal(Cursors.WaitCursor, run.Form.Cursor);
            Assert.Contains("Computing", run.Form.Text);
            Assert.Empty(run.Grid.Controls.OfType<BestFitCell>());
            AssertDisposed(oldSource);
            run.Finish(newTask, newer);
        }
        else
        {
            run.Finish(newTask, newer);
            var title = run.Form.Text;
            var loadingChanges = run.Form.LoadingChanges.Count;
            run.Finish(oldTask, old);
            Assert.Equal(title, run.Form.Text);
            Assert.Equal(loadingChanges, run.Form.LoadingChanges.Count);
        }
        Assert.Same(newer.Result.Results, Field<List<BestFitResult>>(run.Form, "results"));
        Assert.Single(run.Grid.Controls.OfType<BestFitCell>());
        Assert.Empty(run.Form.Notices);
        Assert.Null(run.Source);
        AssertDisposed(oldSource);
        AssertDisposed(newSource);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CloseOrDisposalRejectsLateResultsAndErrorsWithoutTouchingControls(bool dispose, bool fail) => RunSta(() =>
    {
        using var run = new ViewerRun();
        var work = run.Queue(run.Drawings.ElementAt(0), 1);
        if (fail)
            work.Error = new InvalidOperationException("Late failure");
        run.Form.Show();
        Application.DoEvents();
        work.WaitForEntry();
        var source = run.Source!;
        if (dispose)
            run.Form.Dispose();
        else
            run.Form.Close();
        if (!dispose)
        {
            Assert.True(source.IsCancellationRequested);
            Assert.Null(run.Source);
        }
        _ = source.Token; // Closing must not dispose a source still owned by its worker.
        var changes = run.Form.LoadingChanges.Count;
        run.FinishEvents(work);
        Assert.Empty(run.Form.Notices);
        Assert.Equal(changes, run.Form.LoadingChanges.Count);
        Assert.Equal(0, run.Form.LateUiCalls);
        AssertDisposed(source);
        Assert.Null(run.Source);
        Assert.True(run.Form.LoadResultsAsync().IsCompletedSuccessfully);
        Assert.Null(run.Source);
    });

    private sealed class ViewerRun : IDisposable
    {
        private readonly SynchronizationContext? priorContext = SynchronizationContext.Current;
        private readonly Dispatcher dispatcher = new();
        private readonly List<Work> works = [];
        private readonly List<Task> tasks = [];
        public DrawingCollection Drawings { get; } = new();
        public TestViewer Form { get; }
        public TableLayoutPanel Grid => (TableLayoutPanel)Control("gridPanel");
        public CancellationTokenSource? Source => Field<CancellationTokenSource?>(Form, "computeCts");

        public ViewerRun()
        {
            foreach (var name in new[] { "first", "second" })
            {
                var program = new CNC.Program();
                program.Codes.Add(new RapidMove(0, 0));
                program.Codes.Add(new LinearMove(10, 0));
                program.Codes.Add(new LinearMove(10, 5));
                program.Codes.Add(new LinearMove(0, 5));
                program.Codes.Add(new LinearMove(0, 0));
                Drawings.Add(new Drawing(name, program));
            }
            Form = new TestViewer(Drawings, new Plate(36, 120) { PartSpacing = 0.5 });
            SynchronizationContext.SetSynchronizationContext(dispatcher);
        }

        public Control Control(string name) => Form.Controls.Find(name, true).Single();

        public Work Queue(Drawing drawing, int count)
        {
            var work = new Work
            {
                Result = new BestFitViewerForm.ComputeResult
                {
                    Results = Enumerable.Range(0, count).Select(_ => new BestFitResult
                    {
                        Candidate = new PairCandidate { Drawing = drawing, Part2Offset = new Vector(11, 0) },
                        BoundingWidth = 21,
                        BoundingHeight = 5,
                        Keep = true,
                    }).ToList(),
                    TotalResults = count,
                    KeptCount = count,
                    ComputeSeconds = 0.1,
                    TotalSeconds = 0.2,
                },
            };
            works.Add(work);
            Form.Workers.Add(work, CancellationToken.None);
            return work;
        }

        public Task Start(Work work)
        {
            var task = Form.LoadResultsAsync();
            tasks.Add(task);
            work.WaitForEntry();
            Assert.False(Control("txtPage").Enabled);
            return task;
        }

        public void Finish(Task task, Work work)
        {
            work.Release.Set();
            dispatcher.RunUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        public void FinishEvents(Work work)
        {
            work.Release.Set();
            dispatcher.RunUntil(() => dispatcher.Operations == 0);
        }

        public void Dispose()
        {
            try
            {
                foreach (var work in works)
                    work.Release.Set();
                dispatcher.RunUntil(() => tasks.All(task => task.IsCompleted) && dispatcher.Operations == 0);
                Form.Dispose();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
                foreach (var work in works)
                    work.Dispose();
            }
        }
    }

    private sealed class Work : IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public BestFitViewerForm.ComputeResult Result;
        public Exception? Error;
        public (Drawing Drawing, double Length, double Width, double Spacing) Input;
        public void WaitForEntry() => Assert.True(Entered.Wait(TimeSpan.FromSeconds(15)), "Worker did not start.");
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }

    private sealed class TestViewer(DrawingCollection drawings, Plate plate) : BestFitViewerForm(drawings, plate)
    {
        public BlockingCollection<Work> Workers { get; } = new();
        public List<(Drawing Drawing, Exception Error)> Notices { get; } = [];
        public List<bool> LoadingChanges { get; } = [];
        public Exception? RenderFailure { get; set; }
        public int LateUiCalls { get; private set; }
        private bool closed;

        internal override ComputeResult ComputeResults(Drawing drawing, double length, double width, double spacing)
        {
            var work = Workers.Take();
            work.Input = (drawing, length, width, spacing);
            work.Entered.Set();
            if (!work.Release.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Worker was not released.");
            if (work.Error != null)
                throw work.Error;
            return work.Result;
        }

        internal override void ReportLoadFailure(Drawing drawing, Exception error)
        {
            CheckLifetime();
            Notices.Add((drawing, error));
        }

        internal override void SetLoading(bool loading)
        {
            CheckLifetime();
            LoadingChanges.Add(loading);
            base.SetLoading(loading);
        }

        internal override void ShowPage(int page)
        {
            CheckLifetime();
            base.ShowPage(page);
            if (RenderFailure != null)
                throw RenderFailure; // A failure after cells were added must not leave a partial display.
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;
            base.OnFormClosed(e);
        }

        private void CheckLifetime()
        {
            if (closed || IsDisposed || Disposing)
                LateUiCalls++;
        }
    }

    private sealed class Dispatcher : SynchronizationContext
    {
        private readonly BlockingCollection<System.Action> callbacks = new();
        public int Operations { get; private set; }
        public override void OperationStarted() => Operations++;
        public override void OperationCompleted() => Operations--;
        public override void Post(SendOrPostCallback d, object? state) => callbacks.Add(() => d(state));
        public void RunUntil(Func<bool> done)
        {
            while (!done())
            {
                Assert.True(callbacks.TryTake(out var callback, TimeSpan.FromSeconds(15)), "UI continuation did not complete.");
                callback();
            }
        }
    }

    private static void AssertDisposed(CancellationTokenSource? source) =>
        Assert.Throws<ObjectDisposedException>(() => source!.Token);

    private static T Field<T>(BestFitViewerForm form, string name) =>
        (T)typeof(BestFitViewerForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void Invoke(BestFitViewerForm form, string name, params object[] args) =>
        typeof(BestFitViewerForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

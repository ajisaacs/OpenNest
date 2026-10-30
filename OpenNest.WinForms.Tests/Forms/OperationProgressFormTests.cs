using System.Diagnostics;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class OperationProgressFormTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void CancelWaitsForTheWorkerBeforeClosing() => StaTestThread.Run(() =>
    {
        using var worker = new HeldWorker();
        using var form = new OperationProgressForm("Expand Spacing", "Working", worker.Task, worker.Source);
        try
        {
            form.Show();
            worker.WaitForEntry();

            // Escape clicks the same CancelButton.
            Assert.NotNull(form.CancelButton);
            form.CancelButton.PerformClick();

            Assert.True(form.CancelRequested);
            Assert.False(form.CancelEnabled);
            Assert.StartsWith("Canceling", form.StatusText);
            PumpFor(TimeSpan.FromMilliseconds(200));
            Assert.False(form.IsDisposed, "Cancel closed the window while the worker was still running.");
            Assert.True(form.Visible);

            worker.Release();
            PumpUntil(() => form.IsDisposed, "The window did not close after the worker stopped.");
            Assert.True(Completed(worker.Task), "The worker did not observe cancellation.");
        }
        finally
        {
            worker.Release();
        }
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ClosingWhileRunningRequestsCancellationAndWaits() => StaTestThread.Run(() =>
    {
        using var worker = new HeldWorker();
        using var form = new OperationProgressForm("Expand Spacing", "Working", worker.Task, worker.Source);
        try
        {
            form.Show();
            worker.WaitForEntry();

            form.Close();

            Assert.True(form.CancelRequested);
            Assert.False(form.IsDisposed, "Closing did not wait for the worker.");
            Assert.True(form.Visible);

            worker.Release();
            PumpUntil(() => form.IsDisposed, "The window did not close after the worker stopped.");
        }
        finally
        {
            worker.Release();
        }
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void RunReturnsAQuickResultAndRethrowsItsFailure() => StaTestThread.Run(() =>
    {
        // A long show delay keeps both runs below it, so no window can open.
        var delay = TimeSpan.FromSeconds(30);

        Assert.Equal(42, OperationProgressForm.Run(null, "Title", "Working", _ => 42, delay));

        var error = Assert.Throws<InvalidOperationException>(() =>
            OperationProgressForm.Run<int>(
                null,
                "Title",
                "Working",
                _ => throw new InvalidOperationException("search failed"),
                delay
            )
        );
        Assert.Equal("search failed", error.Message);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void CanceledExpandMovesNoPartAndReportsIt() => StaTestThread.Run(() =>
    {
        using var view = TwoPartView();
        var before = view.Plate.Parts.Select(p => p.Location).ToArray();

        var handled = view.ExpandSelected(work =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            return work(canceled.Token);
        });

        Assert.True(handled);
        Assert.Equal(before, view.Plate.Parts.Select(p => p.Location).ToArray());
        Assert.Contains("canceled", view.Status);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ExpandRunsItsSearchThroughTheSuppliedRunner() => StaTestThread.Run(() =>
    {
        using var view = TwoPartView();
        var before = view.Plate.Parts.Select(p => p.Location).ToArray();
        var runs = 0;

        var handled = view.ExpandSelected(work =>
        {
            runs++;
            return work(CancellationToken.None);
        });

        Assert.True(handled);
        Assert.Equal(1, runs);
        Assert.NotEqual(before, view.Plate.Parts.Select(p => p.Location).ToArray());
        Assert.StartsWith("Expanded spacing to", view.Status);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static PlateView TwoPartView()
    {
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("left", Square()), new Vector(1, 1)));
        plate.Parts.Add(new Part(new Drawing("right", Square()), new Vector(12, 1)));
        var view = new PlateView { Plate = plate };
        view.SelectAll();
        return view;
    }

    private static CNC.Program Square()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }

    private static void PumpFor(TimeSpan duration)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void PumpUntil(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Timeout, message);
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static bool Completed(Task<bool> task)
    {
        Assert.True(task.Wait(Timeout), "The worker did not finish.");
        return task.Result;
    }

    /// <summary>A worker held until released; its result says whether it saw cancellation.</summary>
    private sealed class HeldWorker : IDisposable
    {
        private readonly ManualResetEventSlim entered = new();
        private readonly ManualResetEventSlim release = new();

        public HeldWorker()
        {
            var token = Source.Token;
            Task = System.Threading.Tasks.Task.Run(() =>
            {
                entered.Set();
                release.Wait(Timeout);
                return token.IsCancellationRequested;
            });
        }

        public CancellationTokenSource Source { get; } = new();

        public Task<bool> Task { get; }

        public void WaitForEntry() => Assert.True(entered.Wait(Timeout), "Worker did not start.");

        public void Release() => release.Set();

        public void Dispose()
        {
            release.Set();
            Task.Wait(Timeout);
            Source.Dispose();
            entered.Dispose();
            release.Dispose();
        }
    }
}

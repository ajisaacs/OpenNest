using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenNest.Forms;

/// <summary>
/// Small "working" window with a Cancel button for a long plate operation that
/// runs on a worker thread. Cancel, Escape and the close box only request
/// cancellation: the window stays open showing "Cancelling..." until the worker
/// has actually stopped, so the caller never continues while it is still running.
/// </summary>
internal sealed class OperationProgressForm : Form
{
    /// <summary>Runs that finish within this delay never show the window.</summary>
    internal static readonly TimeSpan DefaultShowDelay = TimeSpan.FromMilliseconds(300);

    private readonly Task task;
    private readonly CancellationTokenSource cts;
    private readonly SynchronizationContext uiContext;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Label statusLabel;
    private readonly Button cancelButton;
    private readonly System.Windows.Forms.Timer elapsedTimer;
    private bool finished;

    internal OperationProgressForm(string title, string message, Task task, CancellationTokenSource cts)
    {
        this.task = task ?? throw new ArgumentNullException(nameof(task));
        this.cts = cts ?? throw new ArgumentNullException(nameof(cts));

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 128);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var messageLabel = new Label
        {
            Text = message,
            AutoEllipsis = true,
            Location = new Point(12, 12),
            Size = new Size(356, 20),
        };
        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(12, 38),
            Size = new Size(356, 18),
        };
        statusLabel = new Label
        {
            Location = new Point(12, 64),
            Size = new Size(356, 20),
        };
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(293, 92),
            Size = new Size(75, 26),
        };
        cancelButton.Click += (_, _) => RequestCancel();

        Controls.Add(messageLabel);
        Controls.Add(bar);
        Controls.Add(statusLabel);
        Controls.Add(cancelButton);

        // Escape clicks Cancel. Assigning CancelButton gives the button
        // DialogResult.Cancel, which would close the window at once, so clear it:
        // closing waits for the worker.
        CancelButton = cancelButton;
        cancelButton.DialogResult = DialogResult.None;

        elapsedTimer = new System.Windows.Forms.Timer { Interval = 500 };
        elapsedTimer.Tick += (_, _) => UpdateStatus();
        UpdateStatus();

        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    }

    internal bool CancelRequested => cts.IsCancellationRequested;

    internal string StatusText => statusLabel.Text;

    internal bool CancelEnabled => cancelButton.Enabled;

    /// <summary>
    /// Runs <paramref name="work"/> on a worker thread and returns its result,
    /// rethrowing its exception. A run still going after
    /// <paramref name="showDelay"/> shows a modal window whose Cancel requests
    /// cancellation through the token passed to <paramref name="work"/>; this
    /// returns only once the worker has stopped, whether or not it honored it.
    /// </summary>
    internal static T Run<T>(
        IWin32Window owner,
        string title,
        string message,
        Func<CancellationToken, T> work,
        TimeSpan? showDelay = null
    )
    {
        ArgumentNullException.ThrowIfNull(work);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var task = Task.Run(() => work(token));

        // A wait handle, unlike Task.Wait, does not throw for a faulted run.
        if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(showDelay ?? DefaultShowDelay))
        {
            using var form = new OperationProgressForm(title, message, task, cts);
            form.ShowDialog(owner);
        }

        // The window closes only after the worker stops. Should it ever return
        // early, stop the worker rather than continue beside it.
        if (!task.IsCompleted)
            cts.Cancel();

        return task.GetAwaiter().GetResult();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        elapsedTimer.Start();
        task.ContinueWith(
            _ => uiContext.Post(_ => Finish(), null),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!finished)
        {
            e.Cancel = true;
            RequestCancel();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The caller owns the token source; only ask a still-running worker to stop.
            if (!finished && !task.IsCompleted)
                cts.Cancel();
            elapsedTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RequestCancel()
    {
        if (finished || cts.IsCancellationRequested)
            return;

        cts.Cancel();
        cancelButton.Enabled = false;
        UpdateStatus();
    }

    private void Finish()
    {
        if (finished || IsDisposed)
            return;

        finished = true;
        elapsedTimer.Stop();
        Close();
    }

    private void UpdateStatus()
    {
        var elapsed = stopwatch.Elapsed;
        var time = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        statusLabel.Text = cts.IsCancellationRequested
            ? $"Cancelling... waiting for the current step to stop ({time})"
            : $"Elapsed {time}";
    }
}

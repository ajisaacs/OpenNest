using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Diagnostics;

namespace OpenNest.Forms;

/// <summary>
/// One posting attempt. The owner must prevent background nest edits before opening
/// this modal form; cancellation keeps it modal until the read-only worker stops.
/// </summary>
public partial class PostVerificationForm : Form
{
    private readonly Nest nest;
    private readonly Func<Nest, CancellationToken, Task<PostVerificationReport>> analyze;
    private readonly CancellationTokenSource cancellation = new();
    private PostVerificationReport report;
    private bool started;
    private bool analyzing;
    private bool closeRequested;

    public PostVerificationForm(Nest nest, IPostProcessor postProcessor = null)
        : this(nest, (value, token) => Task.Run(
            () => PostVerificationAnalyzer.AnalyzeForPost(value, postProcessor, token), token))
    { }

    internal PostVerificationForm(Nest nest,
        Func<Nest, CancellationToken, Task<PostVerificationReport>> analyze)
    {
        this.nest = nest ?? throw new ArgumentNullException(nameof(nest));
        this.analyze = analyze ?? throw new ArgumentNullException(nameof(analyze));
        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await VerifyAsync();
    }

    internal async Task VerifyAsync()
    {
        if (started)
            return;
        started = true;
        analyzing = true;
        try
        {
            var result = await analyze(nest, cancellation.Token);
            if (closeRequested || IsDisposed)
                return;
            report = result;
            reportBox.Text = report.ToDisplayText();
            reportBox.SelectionStart = 0;
            reportBox.SelectionLength = 0;
            summaryLabel.Text = report.HasWarnings
                ? "Warnings found. Review every finding before deciding whether to post."
                : "Verification complete. No warnings found by these checks.";
            acknowledgeBox.Visible = report.HasWarnings;
            acknowledgeBox.Enabled = report.HasWarnings;
            acknowledgeBox.Checked = false;
            postButton.Text = report.HasWarnings ? "Post Anyway" : "Continue Posting";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            closeRequested = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                report = null;
                summaryLabel.Text = "Verification failed. Posting is blocked.";
                reportBox.Text = "The verification could not finish. Cancel, correct the problem, and try again."
                    + Environment.NewLine + Environment.NewLine + ex.Message;
            }
        }
        finally
        {
            analyzing = false;
            if (!IsDisposed)
            {
                UpdatePostPermission();
                if (closeRequested)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            }
        }
    }

    private void UpdatePostPermission() =>
        postButton.Enabled = !analyzing && !closeRequested
            && report?.CanPost(acknowledgeBox.Checked) == true;

    private void AcknowledgeBox_CheckedChanged(object sender, EventArgs e) => UpdatePostPermission();

    private void PostButton_Click(object sender, EventArgs e)
    {
        // Check again here; an enabled button alone is not consent enforcement.
        if (analyzing || closeRequested || report?.CanPost(acknowledgeBox.Checked) != true)
            return;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (analyzing)
        {
            closeRequested = true;
            cancellation.Cancel();
            postButton.Enabled = false;
            cancelButton.Enabled = false;
            summaryLabel.Text = "Canceling verification...";
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
}

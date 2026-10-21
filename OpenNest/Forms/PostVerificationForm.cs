using System;
using System.Drawing;
using System.Linq;
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
    private enum Status { Running, Clear, Warning, Failed }

    // Most severe first; this order drives both the tiles and the list groups.
    private static readonly (PostVerificationKind Kind, string Label)[] Categories =
    {
        (PostVerificationKind.Incomplete, "Incomplete checks"),
        (PostVerificationKind.Overlap, "Overlaps"),
        (PostVerificationKind.RapidCrossing, "Rapid crossings"),
        (PostVerificationKind.MissingLeadIn, "Missing lead-ins"),
        (PostVerificationKind.NestedCutOrder, "Nested part order"),
        (PostVerificationKind.NestedLead, "Nested part leads"),
        (PostVerificationKind.FixedLead, "Fixed leads"),
        (PostVerificationKind.PierceZone, "Pierce zones"),
        (PostVerificationKind.PartSpacing, "Part spacing"),
    };

    // Checks every plate always runs, so their tiles show even at zero.
    private static readonly PostVerificationKind[] CoreChecks =
    {
        PostVerificationKind.Overlap, PostVerificationKind.RapidCrossing, PostVerificationKind.MissingLeadIn,
    };

    private static readonly Color ClearAccent = Color.FromArgb(22, 128, 61);
    private static readonly Color WarningAccent = Color.FromArgb(194, 94, 0);

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
        SetStatus(Status.Running, "Checking nest…",
            "Looking for overlaps, missing lead-ins and rapid crossings on every plate. " +
            "No CNC output has been written.");
        ShowEmpty("Findings will appear here when the checks finish.");
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
            ShowReport();
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
                SetStatus(Status.Failed, "Verification failed — posting is blocked",
                    "The verification could not finish. Cancel, correct the problem, and try again."
                    + Environment.NewLine + ex.Message);
                ShowEmpty("No findings are available because the verification did not finish.");
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

    private void ShowReport()
    {
        var findings = report.Findings;
        if (!report.HasWarnings)
        {
            SetStatus(Status.Clear, "No warnings found",
                "Every plate passed these checks. You can continue posting.");
        }
        else if (findings.Any(finding => finding.Kind == PostVerificationKind.Incomplete))
        {
            SetStatus(Status.Warning, "Verification incomplete",
                "Some checks could not finish. Review every finding before deciding whether to post.");
        }
        else
        {
            var plates = findings.Where(finding => finding.PlateNumber > 0)
                .Select(finding => finding.PlateNumber).Distinct().Count();
            var headline = Plural(findings.Count, "warning")
                + (plates > 0 ? $" on {Plural(plates, "plate")}" : "");
            SetStatus(Status.Warning, headline, "Review every finding before deciding whether to post.");
        }

        BuildTiles();
        BuildList();
        acknowledgePanel.Visible = report.HasWarnings;
        acknowledgeBox.Enabled = report.HasWarnings;
        acknowledgeBox.Checked = false;
        postButton.Text = report.HasWarnings ? "Post Anyway" : "Continue Posting";
        copyButton.Enabled = true;
    }

    private void SetStatus(Status status, string headline, string detail)
    {
        var (back, fore, glyph) = status switch
        {
            Status.Clear => (Color.FromArgb(225, 244, 230), Color.FromArgb(20, 83, 45), ""),
            Status.Warning => (Color.FromArgb(255, 241, 208), Color.FromArgb(124, 63, 0), ""),
            Status.Failed => (Color.FromArgb(253, 228, 228), Color.FromArgb(145, 24, 24), ""),
            _ => (Color.FromArgb(230, 239, 250), Color.FromArgb(28, 70, 128), ""),
        };
        banner.BackColor = back;
        headlineLabel.ForeColor = fore;
        detailLabel.ForeColor = fore;
        statusIcon.ForeColor = fore;
        statusIcon.Text = glyph;
        headlineLabel.Text = headline;
        detailLabel.Text = detail;
        progressBar.Visible = status == Status.Running;
    }

    private void BuildTiles()
    {
        tilesPanel.SuspendLayout();
        tilesPanel.Controls.Clear();
        foreach (var (kind, label) in Categories)
        {
            var count = report.Findings.Count(finding => finding.Kind == kind);
            if (count == 0 && !CoreChecks.Contains(kind))
                continue;
            tilesPanel.Controls.Add(CreateTile(kind, label, count));
        }
        tilesPanel.ResumeLayout();
    }

    private Control CreateTile(PostVerificationKind kind, string label, int count)
    {
        var accent = count == 0 ? ClearAccent : WarningAccent;
        var tile = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            MinimumSize = new Size(118, 0),
            Margin = new Padding(3, 3, 8, 3),
            Padding = new Padding(10, 6, 10, 8),
            BackColor = count == 0 ? Color.FromArgb(242, 248, 244) : Color.FromArgb(255, 246, 228),
            Cursor = count == 0 ? Cursors.Default : Cursors.Hand,
            AccessibleName = $"{label}: {count}",
        };
        var number = new Label
        {
            AutoSize = true,
            Text = count == 0 ? "✓ 0" : count.ToString(),
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = accent,
            Margin = new Padding(0),
        };
        var caption = new Label
        {
            AutoSize = true,
            Text = label,
            ForeColor = Color.FromArgb(70, 70, 70),
            Margin = new Padding(1, 0, 0, 0),
        };
        tile.Controls.Add(number, 0, 0);
        tile.Controls.Add(caption, 0, 1);
        if (count != 0)
        {
            EventHandler jump = (_, _) => SelectFirst(kind);
            tile.Click += jump;
            number.Click += jump;
            caption.Click += jump;
            number.Cursor = caption.Cursor = Cursors.Hand;
        }
        return tile;
    }

    private void BuildList()
    {
        findingsList.BeginUpdate();
        findingsList.Items.Clear();
        findingsList.Groups.Clear();
        foreach (var (kind, label) in Categories)
        {
            var findings = report.Findings.Where(finding => finding.Kind == kind).ToList();
            if (findings.Count == 0)
                continue;
            var group = new ListViewGroup($"{label} ({findings.Count})")
            {
                Name = kind.ToString(),
                CollapsedState = ListViewGroupCollapsedState.Expanded,
            };
            findingsList.Groups.Add(group);
            foreach (var finding in findings)
            {
                var item = new ListViewItem(new[]
                {
                    finding.PlateNumber == 0 ? "Post" : finding.PlateNumber.ToString(),
                    finding.PartNumber?.ToString() ?? "",
                    finding.OtherPartNumber?.ToString() ?? "",
                    finding.Message,
                }, group)
                {
                    Tag = finding,
                    ToolTipText = finding.Message,
                };
                findingsList.Items.Add(item);
            }
        }
        findingsList.EndUpdate();
        FitMessageColumn();

        if (report.HasWarnings)
        {
            findingsList.Visible = true;
            emptyLabel.Visible = false;
            findingDetailLabel.Visible = true;
            findingDetailLabel.Text = "Select a finding to read it in full.";
        }
        else
        {
            ShowEmpty("✓  Nothing to review.");
        }
    }

    private void ShowEmpty(string text)
    {
        findingsList.Visible = false;
        findingDetailLabel.Visible = false;
        emptyLabel.Text = text;
        emptyLabel.Visible = true;
    }

    private void SelectFirst(PostVerificationKind kind)
    {
        var item = findingsList.Items.Cast<ListViewItem>()
            .FirstOrDefault(candidate => ((PostVerificationFinding)candidate.Tag).Kind == kind);
        if (item == null)
            return;
        findingsList.SelectedItems.Clear();
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
        findingsList.Focus();
    }

    private void FindingsList_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (findingsList.SelectedItems.Count == 0 ||
            findingsList.SelectedItems[0].Tag is not PostVerificationFinding finding)
        {
            findingDetailLabel.Text = "Select a finding to read it in full.";
            return;
        }
        var where = finding.PlateNumber == 0 ? "Post processor" : $"Plate {finding.PlateNumber}";
        if (finding.PartNumber is { } part)
            where += $", part {part}";
        if (finding.OtherPartNumber is { } other)
            where += $" and part {other}";
        findingDetailLabel.Text = $"{where}: {finding.Message}";
    }

    private void FindingsList_Resize(object sender, EventArgs e) => FitMessageColumn();

    private void FitMessageColumn()
    {
        var used = plateColumn.Width + partColumn.Width + otherPartColumn.Width;
        messageColumn.Width = System.Math.Max(200,
            findingsList.ClientSize.Width - used - SystemInformation.VerticalScrollBarWidth);
    }

    private void CopyButton_Click(object sender, EventArgs e)
    {
        if (report != null)
            Clipboard.SetText(report.ToDisplayText());
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

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
            SetStatus(Status.Running, "Canceling verification…",
                "Waiting for the running checks to stop. No CNC output has been written.");
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
}

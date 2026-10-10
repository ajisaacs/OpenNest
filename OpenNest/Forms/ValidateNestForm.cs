using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Diagnostics;

namespace OpenNest.Forms;

/// <summary>Modeless inspection; the editor scope blocks edits while leaving canvas navigation available.</summary>
public sealed class ValidateNestForm : Form
{
    private readonly Nest nest;
    private readonly ValidationInspectionScope inspection;
    private readonly Func<Nest, CancellationToken, Task<PostVerificationReport>> analyze;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label summaryLabel;
    private readonly TextBox reportBox;
    private readonly DataGridView warningGrid;
    private readonly EditNestForm editor;
    private readonly int originalPlate;
    private bool populating;
    private bool editorRestored;
    private readonly (float Scale, PointF Origin) originalViewport;
    private readonly OverlapDisplayMode originalOverlapDisplay;
    private readonly Button closeButton;
    private bool started;
    private bool analyzing;
    private bool closeRequested;
    private bool disposedResources;

    public ValidateNestForm(Nest nest)
        : this(nest, (value, token) => Task.Run(() => PostVerificationAnalyzer.Analyze(value, token), token))
    { }

    public ValidateNestForm(EditNestForm editor)
        : this(editor, (value, token) => Task.Run(() => PostVerificationAnalyzer.Analyze(value, token), token))
    { }

    internal ValidateNestForm(EditNestForm editor, Func<Nest, CancellationToken, Task<PostVerificationReport>> analyze)
        : this(editor.Nest, analyze)
    {
        this.editor = editor;
        inspection = new ValidationInspectionScope(editor, this);
        originalPlate = editor.PlateManager.CurrentIndex;
        originalViewport = editor.PlateView.CaptureViewport();
        originalOverlapDisplay = editor.PlateView.OverlapDisplay;
        editor.PlateView.OverlapDisplay = OverlapDisplayMode.Off;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(480, 440);
        ClientSize = new Size(560, 650);
    }

    internal ValidateNestForm(Nest nest, Func<Nest, CancellationToken, Task<PostVerificationReport>> analyze)
    {
        this.nest = nest ?? throw new ArgumentNullException(nameof(nest));
        this.analyze = analyze ?? throw new ArgumentNullException(nameof(analyze));
        Text = "Validate Nest";
        Name = nameof(ValidateNestForm);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(850, 560);
        MinimumSize = new Size(700, 440);
        ShowInTaskbar = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        summaryLabel = new Label
        {
            Name = "summaryLabel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Checking all plates: overlaps, missing lead-ins, and rapid crossings...",
            Margin = new Padding(3, 3, 3, 10),
        };
        var scopeLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Select an issue to locate it in the main plate view. Its highlight pulses; unrelated parts appear gray.\r\n"
                + "Minimum spacing, plate boundaries, and final post-processor output are not checked.",
            Margin = new Padding(3, 3, 3, 10),
        };
        reportBox = new TextBox
        {
            Name = "reportBox",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            MaxLength = 0,
            AccessibleName = "Nest validation findings",
            Text = "Validation is running...",
        };
        warningGrid = new DataGridView
        {
            Name = "warningGrid",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            AccessibleName = "Nest validation findings",
        };
        foreach (var title in new[] { "Plate", "Part", "With", "Issue", "Finding" })
            warningGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                Width = title == "Issue" ? 115 : 48,
                AutoSizeMode = title == "Finding" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        warningGrid.CurrentCellChanged += (_, _) => ShowSelectedFinding();
        warningGrid.CellClick += (_, _) => ShowSelectedFinding();
        var findings = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        findings.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        findings.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        findings.Controls.Add(warningGrid, 0, 0);
        findings.Controls.Add(reportBox, 0, 1);
        closeButton = new Button
        {
            Name = "closeButton",
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        closeButton.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttons.Controls.Add(closeButton);
        layout.Controls.Add(summaryLabel, 0, 0);
        layout.Controls.Add(scopeLabel, 0, 1);
        layout.Controls.Add(findings, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (editor == null)
            return;
        var area = Screen.FromControl(editor).WorkingArea;
        Bounds = new Rectangle(area.Right - Width, area.Top, Width, System.Math.Min(Height, area.Height));
    }

    private void ShowSelectedFinding()
    {
        if (editorRestored || populating || editor == null || editor.IsDisposed)
            return;
        var view = editor.PlateView;
        view.ClearValidationFinding();
        if (warningGrid.CurrentRow?.Tag is not PostVerificationFinding finding
            || finding.PlateNumber < 1 || finding.PlateNumber > nest.Plates.Count)
            return;
        if (editor.PlateManager.CurrentIndex != finding.PlateNumber - 1)
            editor.PlateManager.LoadAt(finding.PlateNumber - 1);
        var location = finding.Location;
        if (location == null && finding.PartNumber is { } part && part > 0 && part <= view.Plate.Parts.Count)
            location = view.Plate.Parts[part - 1]?.BoundingBox?.Center;
        if (location is { } point && double.IsFinite(point.X) && double.IsFinite(point.Y))
        {
            var extent = finding.Overlap?.Bounds;
            var size = System.Math.Max(10, System.Math.Max(extent?.Length ?? 0, extent?.Width ?? 0) * 2);
            if (finding.RapidStart is { } start && finding.RapidEnd is { } end)
                size = System.Math.Max(size, start.DistanceTo(end) * 1.5);
            view.ZoomToArea(point.X - size / 2, point.Y - size / 2, size, size);
            // Keep the marker in the portion of the main canvas left of this floating inspector.
            var screen = view.RectangleToScreen(view.ClientRectangle);
            var available = System.Math.Clamp(Left - screen.Left, 0, view.Width);
            if (available > 100)
                view.CenterValidationPoint(point, new Point(available / 2, view.Height / 2));
        }
        else
            view.ZoomToFit();
        view.ShowValidationFinding(finding with { Location = location });
        ShowFindingDetails(finding);
    }

    private void ShowFindingDetails(PostVerificationFinding finding)
    {
        var hint = finding.Kind switch
        {
            PostVerificationKind.Overlap => "Shaded material is the overlap. The ring marks its area centroid, which can lie between separate patches.",
            PostVerificationKind.RapidCrossing => finding.ContactPoints.Count > 0
                ? "Crosshairs mark contacts with the completed contour. For travel along an edge, they mark the contact span's endpoints. The pulsing dashed orange line shows the rapid."
                : "The pulsing dashed orange line shows the rapid inside the completed contour; there is no boundary contact point to mark.",
            PostVerificationKind.MissingLeadIn => "The ring marks the cutting contour start where a lead-in is missing.",
            _ => "The ring marks the affected part's center when available; no exact problem location is known.",
        };
        reportBox.Text = $"Plate {finding.PlateNumber}, part {finding.PartNumber}: {finding.Message}"
            + Environment.NewLine + Environment.NewLine + hint;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RestoreEditor();
        base.OnFormClosed(e);
    }

    private void RestoreEditor()
    {
        if (editorRestored || editor == null || editor.IsDisposed)
            return;
        editorRestored = true;
        inspection?.Dispose();
        editor.PlateView.ClearValidationFinding();
        if (editor.PlateManager.CurrentIndex != originalPlate)
            editor.PlateManager.LoadAt(originalPlate);
        editor.PlateView.RestoreViewport(originalViewport);
        editor.PlateView.OverlapDisplay = originalOverlapDisplay;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await ValidateAsync();
    }

    internal async Task ValidateAsync()
    {
        if (started)
            return;
        started = true;
        analyzing = true;
        try
        {
            var report = await analyze(nest, cancellation.Token);
            if (closeRequested || IsDisposed)
                return;
            populating = true;
            foreach (var finding in report.Findings)
            {
                var index = warningGrid.Rows.Add(finding.PlateNumber, finding.PartNumber, finding.OtherPartNumber,
                    finding.Kind, finding.Message);
                warningGrid.Rows[index].Tag = finding;
            }
            populating = false;
            ShowSelectedFinding();
            reportBox.Text = report.ToDisplayText("Nest validation");
            if (editor != null && warningGrid.CurrentRow?.Tag is PostVerificationFinding selected)
                ShowFindingDetails(selected);
            reportBox.SelectionStart = 0;
            reportBox.SelectionLength = 0;
            summaryLabel.Text = report.Findings.Any(finding => finding.Kind == PostVerificationKind.Incomplete)
                ? "Validation incomplete. Review the findings; this nest has not passed all checks."
                : report.HasWarnings ? "Warnings found. Review the findings below."
                : "Validation complete. No warnings found by these checks.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            closeRequested = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                summaryLabel.Text = "Validation failed. This nest has not passed the checks.";
                reportBox.Text = "Validation could not finish. Close this window and try again."
                    + Environment.NewLine + Environment.NewLine + ex.Message;
            }
        }
        finally
        {
            analyzing = false;
            if (!IsDisposed)
            {
                closeButton.Text = "Close";
                if (closeRequested)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (analyzing)
        {
            closeRequested = true;
            cancellation.Cancel();
            closeButton.Enabled = false;
            summaryLabel.Text = "Canceling validation...";
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedResources)
        {
            disposedResources = true;
            RestoreEditor();
            inspection?.Dispose();
            cancellation.Cancel();
            cancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}

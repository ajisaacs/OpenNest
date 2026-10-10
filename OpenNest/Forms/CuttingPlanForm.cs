using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Engine.CuttingPlanning;

namespace OpenNest.Forms;

/// <summary>
/// Plans part order and lead-ins for the active plate or every plate, previews the result and
/// applies it all-or-nothing through the cutting planner. Nothing live changes before Apply.
/// </summary>
public partial class CuttingPlanForm : Form
{
    private readonly PlateView plateView;
    private readonly Plate activePlate;
    private readonly Plate[] plates;
    private readonly int[] plateNumbers;
    private readonly Func<bool> isOperationBusy;
    private readonly string unit;
    private readonly PlateView preview;
    private readonly DataGridView warningGrid;
    private bool populatingWarnings;
    private readonly SynchronizationContext uiContext;
    private readonly string previewText;
    private CuttingParameters parameters;
    private CancellationTokenSource planning;
    private CuttingPlanProposal proposal;
    private int generation;
    private bool closeRequested;

    /// <param name="plateView">The editor's view of the active plate.</param>
    /// <param name="nest">The nest that owns the active plate.</param>
    /// <param name="allPlates">Plan every plate that has parts instead of only the active plate.</param>
    /// <param name="parameters">Cutting settings to start from; the form keeps its own copy.</param>
    /// <param name="isOperationBusy">Reports a nesting or plate operation that must finish first.</param>
    public CuttingPlanForm(PlateView plateView, Nest nest, bool allPlates, CuttingParameters parameters,
        Func<bool> isOperationBusy = null)
    {
        this.plateView = plateView ?? throw new ArgumentNullException(nameof(plateView));
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(parameters);
        // An owned copy: later edits to the caller's settings objects cannot change this plan.
        this.parameters = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        activePlate = plateView.Plate ?? throw new ArgumentException("An active plate is required.", nameof(plateView));
        var all = nest.Plates.ToList();
        if (!all.Contains(activePlate))
            throw new ArgumentException("The active plate is not in this nest.", nameof(nest));
        plates = allPlates ? all.Where(p => p.Parts.Count > 0).ToArray() : new[] { activePlate };
        if (plates.Length == 0)
            plates = new[] { activePlate };
        plateNumbers = plates.Select(p => all.IndexOf(p) + 1).ToArray();
        this.isOperationBusy = isOperationBusy;
        unit = UnitsHelper.GetShortString(nest.Units);
        InitializeComponent();
        StartPosition = FormStartPosition.Manual;
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
        };
        warningGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Plate",
            Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        warningGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Part",
            Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        warningGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "With",
            Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        warningGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Warning / finding",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        warningGrid.SelectionChanged += WarningGrid_SelectionChanged;
        var findingsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        findingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        findingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        summaryBox.Dock = DockStyle.Fill;
        findingsLayout.Controls.Add(summaryBox, 0, 0);
        findingsLayout.Controls.Add(warningGrid, 0, 1);
        split.Panel2.Controls.Add(findingsLayout);
        // Keep the batch outcome visible; every individual diagnostic lives in the grid.
        summaryBox.MaxLength = int.MaxValue;
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        preview = new PlateView
        {
            Dock = DockStyle.Fill,
            AllowSelect = false,
            AllowDrop = false,
            DrawOrigin = false,
            DrawRapid = true,
            Cursor = Cursors.Default,
            TabStop = false,
            Visible = false,
        };
        preview.SizeChanged += (_, _) =>
        {
            if (preview.Visible)
                preview.ZoomToFit();
        };
        previewPanel.Controls.Add(preview);

        if (allPlates)
        {
            Text = "Plan Cutting — All Plates";
            applyButton.Text = "&Apply to All Plates";
        }
        // Display-only numbering: the trailing empty new-plate sentinel is excluded from the
        // "of M" total; plateNumbers keep real collection positions for the batch summary.
        var displayCount = PlateDisplayNumbering.DisplayedPlateCount(all);
        var activeNumber = PlateDisplayNumbering.DisplayedPlateNumber(all, all.IndexOf(activePlate));
        previewText = activeNumber == null
            ? "New plate (empty) has no parts; plates with parts are listed on the right."
            : plates.Contains(activePlate)
                ? $"Plate {activeNumber} of {displayCount}, numbered in cutting order:"
                : $"Plate {activeNumber} has no parts; plates with parts are listed on the right.";
        previewLabel.Text = previewText;
        ShowSettings();
    }

    /// <summary>The settings the operator confirmed; meaningful after an OK result.</summary>
    public CuttingParameters ConfirmedParameters => parameters;

    /// <summary>The commit outcome of the last Apply, or null if Apply never ran.</summary>
    public CuttingCommitResult CommitResult { get; private set; }

    internal Task PlanningTask { get; private set; } = Task.CompletedTask;

    internal CuttingPlanProposal Proposal => proposal;

    internal bool IsPlanning => planning != null;

    /// <summary>Test seam: runs on the worker, before planning, with the run's cancellation token.</summary>
    internal Action<CancellationToken> BeforePlan { get; set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Use the database browser's owner-monitor sizing, without maximizing a modal window.
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        var width = System.Math.Min(area.Width, System.Math.Max(MinimumSize.Width, area.Width * 9 / 10));
        var height = System.Math.Min(area.Height, System.Math.Max(MinimumSize.Height, area.Height * 9 / 10));
        Bounds = new Rectangle(area.Left + (area.Width - width) / 2,
            area.Top + (area.Height - height) / 2, width, height);
        // Min sizes and the splitter only fit once the container has its real size.
        split.Panel1MinSize = 200;
        split.Panel2MinSize = 220;
        var widest = split.Width - split.Panel2MinSize - split.SplitterWidth;
        if (widest > split.Panel1MinSize)
            split.SplitterDistance = System.Math.Clamp(split.Width * 3 / 5, split.Panel1MinSize, widest);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        StartPlanning();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (planning != null)
        {
            // Keep the editor disabled until the worker has stopped; then close for real.
            e.Cancel = true;
            closeRequested = true;
            planning.Cancel();
            statusLabel.Text = "Cancelling…";
            cancelButton.Enabled = false;
        }
        base.OnFormClosing(e);
    }

    private bool TryCheckCanChange(out string reason)
    {
        reason = null;
        if (isOperationBusy?.Invoke() == true || plateView.IsFillInProgress)
            reason = "Wait for the current nesting or plate action to finish, then replan.";
        else if (plateView.IsDisposed || !ReferenceEquals(plateView.Plate, activePlate))
            reason = "The active plate changed. Close this dialog and try again.";
        return reason == null;
    }

    private void StartPlanning()
    {
        if (planning != null || IsDisposed)
            return; // One planning run at a time; inputs are disabled while it runs.
        ClearProposal();
        if (!TryCheckCanChange(out var reason))
        {
            ShowMessage(reason);
            return;
        }

        CuttingPlanBatch batch;
        try
        {
            batch = CuttingPlanBatch.Capture(plates, parameters, keepOrderCheckBox.Checked, plateNumbers);
        }
        catch (Exception ex)
        {
            // Capture runs inside the dialog's message loop: report a failure, never crash the editor.
            ShowMessage($"Unable to start planning: {ex.Message}");
            return;
        }

        var source = new CancellationTokenSource();
        var token = source.Token;
        planning = source;
        var run = ++generation;
        SetPlanning(true);
        summaryBox.Text = string.Empty;
        statusLabel.Text = "Planning…";
        var progress = new PostingProgress(uiContext, value =>
        {
            if (run == generation && ReferenceEquals(planning, source) && !IsDisposed)
                statusLabel.Text = ProgressText(value);
        });
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlanningTask = completion.Task;
        var beforePlan = BeforePlan;
        Task.Run(() =>
        {
            beforePlan?.Invoke(token);
            return batch.Plan(progress, token);
        }).ContinueWith(
            work => uiContext.Post(_ => FinishPlanning(work, source, run, completion), null),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // Runs on the dialog's thread once the worker has stopped.
    private void FinishPlanning(Task<CuttingPlanProposal> work, CancellationTokenSource source, int run,
        TaskCompletionSource completion)
    {
        try
        {
            if (ReferenceEquals(planning, source))
                planning = null;
            source.Dispose();

            if (run != generation || IsDisposed)
                return;
            SetPlanning(false);
            if (closeRequested)
            {
                Close();
                return;
            }
            if (work.IsFaulted)
                ShowMessage($"Planning failed: {work.Exception.GetBaseException().Message}");
            else if (work.IsCanceled)
                ShowMessage("Planning was cancelled. Nothing has changed.");
            else
                ShowProposal(work.Result);
        }
        catch (Exception ex) when (!IsDisposed)
        {
            // A plan that cannot be presented must not leave a stale preview or an enabled Apply.
            ShowMessage($"Unable to show the plan: {ex.Message}");
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void ShowProposal(CuttingPlanProposal result)
    {
        proposal = result;
        summaryBox.Text = string.Join(Environment.NewLine, result.Describe(unit)
            .Where(line => !line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                && !line.TrimStart().StartsWith("... and ", StringComparison.Ordinal)));
        var warningRows = result.DiagnosticRows();
        populatingWarnings = true;
        try
        {
            warningGrid.Rows.Clear();
            foreach (var row in warningRows)
            {
                var display = warningGrid.Rows.Add(row.PlateNumber, row.PartNumber, row.OtherPartNumber,
                    row.Message);
                warningGrid.Rows[display].Tag = row;
            }
            warningGrid.ClearSelection();
        }
        finally
        {
            populatingWarnings = false;
        }
        acceptWarningsCheckBox.Checked = false;
        acceptWarningsCheckBox.Visible = result.RequiresWarningAcceptance;
        statusLabel.Text = result.CanApply ? "Review the plan, then apply it."
            : result.RequiresWarningAcceptance ? "Unverified plan: review and accept the warnings to apply."
            : "Nothing can be applied.";
        applyButton.Enabled = result.CanApply;
        var index = Array.IndexOf(plates, activePlate);
        ShowPlate(index);
    }

    private void ShowPlate(int index)
    {
        preview.Visible = false;
        if (proposal == null || index < 0 || index >= proposal.Plates.Count)
            return;
        var plate = proposal.BuildPreview(index);
        if (plate == null)
        {
            previewLabel.Text = $"Plate {plateNumbers[index]}: no current, usable preview. "
                + "Part numbers refer to the editor; no location can be certified here.";
            return;
        }
        previewLabel.Text = proposal.Plates[index].IsReady
            ? $"Plate {plateNumbers[index]} — preview, numbered in proposed cutting order:"
            : $"UNVERIFIED — Plate {plateNumbers[index]} — preview, numbered in proposed cutting order:";
        preview.Plate = plate;
        preview.Visible = true;
        preview.ZoomToFit();
    }

    private void WarningGrid_SelectionChanged(object sender, EventArgs e)
    {
        if (populatingWarnings || proposal == null || warningGrid.SelectedRows.Count == 0)
            return;
        // SelectionChanged precedes CurrentCell's update; SelectedRows is the new selection.
        if (warningGrid.SelectedRows[0].Tag is not CuttingPlanDiagnosticRow row)
            return;
        ShowPlate(row.PlateIndex);
        if (!preview.Visible || row.PartNumber is not int partNumber)
        {
            if (preview.Visible)
                previewLabel.Text += " No part location was provided for this warning.";
            return;
        }
        var plan = proposal.Plates[row.PlateIndex].Result;
        var matched = plan.ProposedOrder.Select((part, position) => (part, position))
            .FirstOrDefault(item => item.part.SourceOrdinal == partNumber - 1);
        if (matched.part == null)
        {
            previewLabel.Text += " This source part has no proposed preview location.";
            return;
        }
        var bounds = preview.Plate.Parts[matched.position].BoundingBox;
        if (row.OtherPartNumber is int otherNumber)
        {
            var other = plan.ProposedOrder.Select((part, position) => (part, position))
                .FirstOrDefault(item => item.part.SourceOrdinal == otherNumber - 1);
            if (other.part != null)
            {
                var second = preview.Plate.Parts[other.position].BoundingBox;
                var left = System.Math.Min(bounds.Left, second.Left);
                var bottom = System.Math.Min(bounds.Bottom, second.Bottom);
                bounds = new OpenNest.Geometry.Box(left, bottom,
                    System.Math.Max(bounds.Right, second.Right) - left,
                    System.Math.Max(bounds.Top, second.Top) - bottom);
            }
        }
        if (double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Length) && double.IsFinite(bounds.Width)
            && bounds.Length > 0 && bounds.Width > 0)
        {
            var pad = System.Math.Max(0.5, System.Math.Max(bounds.Length, bounds.Width) * 0.12);
            preview.ZoomToArea(bounds.X - pad, bounds.Y - pad,
                bounds.Length + 2 * pad, bounds.Width + 2 * pad);
        }
        previewLabel.Text += " Showing affected part extent, not an exact warning point.";
    }

    private void ClearProposal()
    {
        proposal = null;
        populatingWarnings = true;
        try
        {
            warningGrid.Rows.Clear();
        }
        finally
        {
            populatingWarnings = false;
        }
        acceptWarningsCheckBox.Checked = false;
        acceptWarningsCheckBox.Visible = false;
        applyButton.Enabled = false;
        preview.Visible = false;
        previewLabel.Text = previewText;
    }

    private void ShowMessage(string message)
    {
        ClearProposal();
        statusLabel.Text = string.Empty;
        summaryBox.Text = message;
    }

    private void SetPlanning(bool running)
    {
        settingsButton.Enabled = !running;
        keepOrderCheckBox.Enabled = !running;
        acceptWarningsCheckBox.Enabled = !running;
        planButton.Enabled = !running;
        if (running)
            applyButton.Enabled = false;
    }

    private static string ProgressText(CuttingPlanProgress progress)
    {
        var plate = progress.PlateCount == 1
            ? $"plate {progress.PlateNumber}"
            : $"plate {progress.PlateNumber} ({progress.PlateIndex + 1} of {progress.PlateCount})";
        return progress.Phase switch
        {
            CuttingPlanPhase.CheckingOverlap => $"Checking {plate} for overlapping parts…",
            CuttingPlanPhase.Reordering => $"Planning {plate}: searching for a part order…",
            _ => $"Planning {plate} with the current part order…",
        };
    }

    private void ShowSettings()
    {
        var tabs = parameters.TabsEnabled ? "on" : "off";
        settingsLabel.Text = $"Lead-ins: outside {Describe(parameters.ExternalLeadIn)}, holes "
            + $"{Describe(parameters.InternalLeadIn)}, arcs and circles {Describe(parameters.ArcCircleLeadIn)}. "
            + $"Tabs {tabs}.";
    }

    private static string Describe(LeadIn leadIn)
    {
        if (leadIn == null || leadIn is NoLeadIn)
            return "none";
        var name = leadIn.GetType().Name;
        if (name.EndsWith("LeadIn", StringComparison.Ordinal))
            name = name[..^"LeadIn".Length];
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
    }

    private void SettingsButton_Click(object sender, EventArgs e)
    {
        using var dialog = new CuttingParametersDialog();
        dialog.LoadParameters(parameters);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        parameters = dialog.GetParameters();
        ShowSettings();
        StartPlanning();
    }

    private void KeepOrderCheckBox_CheckedChanged(object sender, EventArgs e) => StartPlanning();

    private void PlanButton_Click(object sender, EventArgs e) => StartPlanning();

    private sealed class PostingProgress(SynchronizationContext context, Action<CuttingPlanProgress> handler)
        : IProgress<CuttingPlanProgress>
    {
        public void Report(CuttingPlanProgress value) => context.Post(_ => handler(value), null);
    }

    private bool CanApplyProposal => proposal?.CanApply == true
        || proposal?.CanApplyWithWarnings == true && acceptWarningsCheckBox.Checked;

    private void AcceptWarningsCheckBox_CheckedChanged(object sender, EventArgs e) =>
        applyButton.Enabled = planning == null && CanApplyProposal;

    private void ApplyButton_Click(object sender, EventArgs e)
    {
        if (planning != null || !CanApplyProposal)
            return;
        if (!TryCheckCanChange(out var reason))
        {
            ShowMessage(reason);
            return;
        }

        var commit = proposal.Apply(acceptWarningsCheckBox.Checked);
        CommitResult = commit;
        switch (commit.Status)
        {
            case CuttingCommitStatus.Applied:
                DialogResult = DialogResult.OK;
                Close();
                return;
            case CuttingCommitStatus.Stale:
                ShowMessage("The nest changed after planning, so nothing was applied. Replan to continue.");
                return;
            default:
                ShowMessage($"Nothing was applied: {commit.Message ?? commit.Status.ToString()}");
                return;
        }
    }
}

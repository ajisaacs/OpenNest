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
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;

namespace OpenNest.Forms;

/// <summary>Owner-thread planning and atomic Apply beside the read-only main canvas.</summary>
internal sealed class CuttingInspectionPanel : UserControl
{
    private readonly EditNestForm editor;
    private readonly PlateView view;
    private readonly Plate plate;
    private CuttingParameters parameters;
    private readonly Plate[] plates;
    private readonly int[] plateNumbers;
    private int selectedPlate;
    private Plate preview;
    private bool populatingWarnings;
    private bool invalidated;
    private readonly SynchronizationContext context;
    private readonly (float Scale, PointF Origin) viewport;
    private readonly OverlapDisplayMode overlapDisplay;
    private readonly CuttingProposalInspection inspection = new();
    private readonly Button planButton = new() { Text = "Replan", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Cancel / Close", AutoSize = true };
    private readonly TextBox resultLabel = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        MaxLength = int.MaxValue,
        AccessibleName = "Cutting plan outcome and remedies"
    };
    private readonly Button settingsButton = new() { Text = "Cutting Settings…", AutoSize = true };
    private readonly Button applyButton = new() { Text = "Apply", AutoSize = true, Enabled = false };
    private readonly Button previousButton = new() { Text = "Previous", AutoSize = true };
    private readonly Button nextButton = new() { Text = "Next", AutoSize = true };
    private readonly CheckBox keepOrderCheckBox = new() { Text = "Keep current part order", AutoSize = true };
    private readonly CheckBox acceptWarningsCheckBox = new() { Text = "Accept UNVERIFIED plan warnings (not posting consent)", AutoSize = true, Visible = false };
    private readonly Label settingsLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Label previewLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly DataGridView warningGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoGenerateColumns = false
    };
    private ValidationInspectionScope scope;
    private bool closed;
    private int generation;

    internal CuttingInspectionPanel(EditNestForm editor, CuttingParameters parameters, bool allPlates = false)
    {
        this.editor = editor;
        view = editor.PlateView;
        plate = view.Plate;
        var all = editor.Nest.Plates.ToList();
        plates = allPlates ? all.Where(p => p.Parts.Count > 0).ToArray() : [plate];
        if (plates.Length == 0)
            plates = [plate];
        plateNumbers = plates.Select(p => all.IndexOf(p) + 1).ToArray();
        selectedPlate = System.Math.Max(0, Array.IndexOf(plates, plate));
        this.parameters = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        viewport = view.CaptureViewport();
        overlapDisplay = view.OverlapDisplay;
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([settingsButton, planButton, applyButton, cancelButton]);
        var navigation = new FlowLayoutPanel { AutoSize = true };
        navigation.Controls.AddRange([previousButton, nextButton]);
        header.Controls.AddRange([settingsLabel, keepOrderCheckBox, buttons, acceptWarningsCheckBox, navigation, previewLabel]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(resultLabel, 0, 1);
        layout.Controls.Add(warningGrid, 0, 2);
        Controls.Add(layout);
        foreach (var name in new[] { "Plate", "Part", "With", "Warning / finding" })
            warningGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                Width = 48,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = name == "Warning / finding" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
            });
        Padding = new Padding(10);
        planButton.Click += (_, _) => StartPlanning();
        cancelButton.Click += (_, _) => RequestClose();
        settingsButton.Click += (_, _) => EditSettings();
        keepOrderCheckBox.CheckedChanged += (_, _) => StartPlanning();
        acceptWarningsCheckBox.CheckedChanged += (_, _) => UpdateApply();
        applyButton.Click += (_, _) => Apply();
        previousButton.Click += (_, _) => ShowPlate(selectedPlate - 1);
        nextButton.Click += (_, _) => ShowPlate(selectedPlate + 1);
        warningGrid.SelectionChanged += (_, _) => NavigateFinding();
        ShowSettings();
        resultLabel.Text = "Read-only proposal inspection. Nothing live changes before Apply.";
    }

    internal bool IsPlanning => inspection.IsRunning;
    internal Task PlanningTask { get; private set; } = Task.CompletedTask;
    internal Action<CancellationToken> BeforePlan { get; set; }
    internal Action BeforePresent { get; set; }
    internal Action BeforePostApply { get; set; }
    internal Action<Exception> OnPostApplyError { get; set; }
    internal CuttingPlanProposal Proposal => inspection.Proposal;

    internal void Open()
    {
        scope = new ValidationInspectionScope(editor, this, editor.SidePanelHeader, RequestClose);
        view.InspectionCloseRequested = RequestClose;
        view.OverlapDisplay = OverlapDisplayMode.Off;
        view.ClearValidationFinding();
        StartPlanning();
    }

    internal void StartPlanning()
    {
        if (closed || inspection.IsRunning || inspection.CloseRequested)
            return;
        ClearResult();
        SetPlanning(true);
        resultLabel.Text = "Planning… Read-only; the live plate is unchanged.";
        try
        {
            // Capture on the owner thread; no worker recapture of the live plate or settings.
            var batch = CuttingPlanBatch.Capture(plates, parameters, keepOrderCheckBox.Checked, plateNumbers);
            var run = ++generation;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PlanningTask = completion.Task;
            var progress = new PostingProgress(context, value =>
            {
                if (!closed && !inspection.CloseRequested && inspection.IsRunning && generation == run)
                    resultLabel.Text = $"Planning plate {value.PlateNumber}: {value.Phase}…";
            });
            var work = inspection.BeginPlanning(batch, BeforePlan, progress);
            work.ContinueWith(task => context.Post(_ => FinishPlanning(task, run, completion), null),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Unable to start planning: {ex.Message}. No current preview.";
            SetPlanning(false);
        }
    }

    private void FinishPlanning(Task<CuttingPlanProposal> work, int run, TaskCompletionSource completion)
    {
        try
        {
            inspection.CompletePlanning(work);
            if (inspection.CloseRequested)
            {
                FinishClose();
                return;
            }
            if (closed || run != generation)
                return;
            SetPlanning(false);
            if (work.IsFaulted)
            {
                resultLabel.Text = $"Planning failed: {work.Exception.GetBaseException().Message}. No current preview.";
                return;
            }
            if (work.IsCanceled)
            {
                resultLabel.Text = "Cancelled. No current preview; nothing changed.";
                return;
            }
            invalidated = false;
            resultLabel.Text = string.Join(Environment.NewLine, work.Result.Describe(UnitsHelper.GetShortString(editor.Nest.Units))
                .Where(line => !line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("... and ", StringComparison.Ordinal)));
            populatingWarnings = true;
            try
            {
                foreach (var row in work.Result.DiagnosticRows())
                {
                    var index = warningGrid.Rows.Add(row.PlateNumber, row.PartNumber, row.OtherPartNumber, row.Message);
                    warningGrid.Rows[index].Tag = row;
                }
                warningGrid.ClearSelection();
            }
            finally { populatingWarnings = false; }
            acceptWarningsCheckBox.Visible = Proposal.RequiresWarningAcceptance;
            UpdateApply();
            BeforePresent?.Invoke();
            ShowPlate(selectedPlate);
        }
        catch (Exception ex)
        {
            invalidated = true;
            preview = null;
            view.ClearProposalDisplay();
            acceptWarningsCheckBox.Checked = false;
            acceptWarningsCheckBox.Visible = false;
            applyButton.Enabled = false;
            previewLabel.Text = "No current preview. Replan to continue.";
            resultLabel.Text = $"Unable to show the plan: {ex.Message}. Nothing can be applied until replanning.";
            SetPlanning(false);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void ClearResult()
    {
        invalidated = true;
        preview = null;
        view.ClearProposalDisplay();
        view.ClearValidationFinding();
        acceptWarningsCheckBox.Checked = false;
        acceptWarningsCheckBox.Visible = false;
        applyButton.Enabled = false;
        previewLabel.Text = "No current preview.";
        populatingWarnings = true;
        try { warningGrid.Rows.Clear(); }
        finally { populatingWarnings = false; }
    }

    private void SetPlanning(bool running)
    {
        settingsButton.Enabled = keepOrderCheckBox.Enabled = planButton.Enabled = !running;
        previousButton.Enabled = !running && selectedPlate > 0;
        nextButton.Enabled = !running && selectedPlate < plates.Length - 1;
        acceptWarningsCheckBox.Enabled = !running;
        if (running) applyButton.Enabled = false;
    }

    private void UpdateApply() => applyButton.Enabled = !invalidated && !IsPlanning
        && (Proposal?.CanApply == true || Proposal?.CanApplyWithWarnings == true && acceptWarningsCheckBox.Checked);

    private bool IsPreviewCurrent()
    {
        if (!closed && !invalidated && ReferenceEquals(view.Plate, plate)
            && inspection.RefreshPreview(selectedPlate) != null)
            return true;
        invalidated = true;
        preview = null;
        acceptWarningsCheckBox.Checked = false;
        applyButton.Enabled = false;
        resultLabel.Text = previewLabel.Text = "No current preview: the plate changed. Replan to inspect it. Nothing applied.";
        return false;
    }

    private void ShowPlate(int index)
    {
        if (IsPlanning || invalidated || Proposal == null || index < 0 || index >= Proposal.Plates.Count)
            return;
        selectedPlate = index;
        view.ClearProposalDisplay();
        view.ClearValidationFinding();
        preview = inspection.RefreshPreview(index); // Only the guarded authority may clone programs.
        SetPlanning(false);
        if (preview == null)
        {
            previewLabel.Text = $"Plate {plateNumbers[index]}: no current preview — stale, blocked or unsupported. Live plate shown; source numbers are not canvas locations.";
            return;
        }
        previewLabel.Text = (Proposal.Plates[index].IsReady ? "PROPOSAL" : "UNVERIFIED PROPOSAL")
            + $" — Plate {plateNumbers[index]} ({index + 1} of {plates.Length}), numbered in proposed cutting order; read-only.";
        view.ShowProposalDisplay(preview, IsPreviewCurrent);
        view.ZoomToFit();
    }

    private void NavigateFinding()
    {
        if (populatingWarnings || invalidated || warningGrid.SelectedRows.Count == 0
            || warningGrid.SelectedRows[0].Tag is not CuttingPlanDiagnosticRow row)
            return;
        ShowPlate(row.PlateIndex);
        if (preview == null || row.PartNumber is not int source)
            return;
        var position = CuttingProposalInspection.ProposedPosition(Proposal, row.PlateIndex, source);
        if (position == null)
            return;
        var bounds = preview.Parts[position.Value].BoundingBox;
        if (row.OtherPartNumber is int otherSource
            && CuttingProposalInspection.ProposedPosition(Proposal, row.PlateIndex, otherSource) is int otherPosition)
        {
            var second = preview.Parts[otherPosition].BoundingBox;
            var left = System.Math.Min(bounds.Left, second.Left);
            var bottom = System.Math.Min(bounds.Bottom, second.Bottom);
            bounds = new OpenNest.Geometry.Box(left, bottom,
                System.Math.Max(bounds.Right, second.Right) - left,
                System.Math.Max(bounds.Top, second.Top) - bottom);
        }
        if (double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Length)
            && double.IsFinite(bounds.Width) && bounds.Length > 0 && bounds.Width > 0)
        {
            var pad = System.Math.Max(0.5, System.Math.Max(bounds.Length, bounds.Width) * 0.12);
            view.ZoomToArea(bounds.X - pad, bounds.Y - pad, bounds.Length + 2 * pad, bounds.Width + 2 * pad);
        }
        previewLabel.Text += " Showing affected part extent, not an exact warning point.";
    }

    internal CuttingCommitResult CommitResult { get; private set; }

    private void Apply()
    {
        if (!applyButton.Enabled || IsPlanning || invalidated || Proposal == null)
            return;
        // This handler runs on the owner thread; Apply independently rechecks all captured plates.
        CommitResult = Proposal.Apply(acceptWarningsCheckBox.Checked);
        if (CommitResult.Status == CuttingCommitStatus.Applied)
        {
            Exception postApplyError = null;
            try
            {
                BeforePostApply?.Invoke();
                editor.CompleteCuttingApply(parameters, CommitResult);
            }
            catch (Exception ex)
            {
                postApplyError = ex;
            }
            finally
            {
                RequestClose(); // The commit already succeeded; never strand the inspection lock.
            }
            if (postApplyError != null)
            {
                if (OnPostApplyError != null)
                    OnPostApplyError(postApplyError);
                else
                    MessageBox.Show(editor, "The cutting plan was applied, but settings or the display did not refresh: "
                        + postApplyError.Message, "Plan Cutting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        ClearResult();
        resultLabel.Text = $"Nothing applied: {CommitResult.Message ?? CommitResult.Status.ToString()}. Replan to continue.";
    }

    private void EditSettings()
    {
        if (IsPlanning || closed) return;
        using var dialog = new CuttingParametersDialog();
        dialog.LoadParameters(parameters);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        parameters = dialog.GetParameters();
        ShowSettings();
        StartPlanning();
    }

    private void ShowSettings()
    {
        settingsLabel.Text = $"Lead-ins: outside {Describe(parameters.ExternalLeadIn)}, holes {Describe(parameters.InternalLeadIn)}, arcs/circles {Describe(parameters.ArcCircleLeadIn)}. Tabs {(parameters.TabsEnabled ? "on" : "off")}.";
    }

    private static string Describe(LeadIn leadIn)
    {
        if (leadIn == null || leadIn is NoLeadIn) return "none";
        var name = leadIn.GetType().Name;
        if (name.EndsWith("LeadIn", StringComparison.Ordinal)) name = name[..^"LeadIn".Length];
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
    }

    internal void RequestClose()
    {
        if (closed)
            return;
        ClearResult();
        SetPlanning(true);
        cancelButton.Enabled = false;
        resultLabel.Text = "Cancelling… waiting for the worker. Nothing has changed.";
        if (inspection.RequestClose())
            FinishClose();
    }

    private void FinishClose()
    {
        if (closed)
            return;
        closed = true;
        view.ClearProposalDisplay();
        if (!view.IsDisposed)
        {
            view.InspectionCloseRequested = null;
            if (ReferenceEquals(view.Plate, plate))
            {
                view.OverlapDisplay = overlapDisplay;
            }
        }
        var ownedScope = scope;
        ownedScope?.Dispose();
        scope = null;
        editor.FinishCuttingInspection(this);
        if (!view.IsDisposed && ReferenceEquals(view.Plate, plate))
            view.RestoreViewport(viewport);
        ownedScope?.ResumeHostClose();
        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !closed)
        {
            RequestClose();
            if (!closed)
                return; // The worker still owns the inspection lock and controls.
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            RequestClose();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed class PostingProgress(SynchronizationContext context, Action<CuttingPlanProgress> handler)
        : IProgress<CuttingPlanProgress>
    {
        public void Report(CuttingPlanProgress value) => context.Post(_ => handler(value), null);
    }
}

using System;
using System.Collections.Generic;
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
        // Captured once: when Application.DoEvents ends the outermost message loop, WinForms
        // uninstalls its ambient context, so progress and results must not depend on whichever
        // context is current when planning starts.
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
        var activeNumber = all.IndexOf(activePlate) + 1;
        previewText = plates.Contains(activePlate)
            ? $"Plate {activeNumber} of {all.Count}, numbered in cutting order:"
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
        summaryBox.Text = string.Join(Environment.NewLine, result.Describe(unit));
        statusLabel.Text = result.CanApply ? "Review the plan, then apply it." : "Nothing can be applied.";
        applyButton.Enabled = result.CanApply;
        var index = Array.IndexOf(plates, activePlate);
        var plate = index < 0 ? null : result.BuildPreview(index);
        if (plate == null)
        {
            if (index >= 0)
                previewLabel.Text = "No preview: this plate's plan is not ready. Part numbers in the summary "
                    + "match the editor.";
            return;
        }
        previewLabel.Text = previewText;
        preview.Plate = plate;
        preview.Visible = true;
        preview.ZoomToFit();
    }

    private void ClearProposal()
    {
        proposal = null;
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

    private void ApplyButton_Click(object sender, EventArgs e)
    {
        if (planning != null || proposal?.CanApply != true)
            return;
        if (!TryCheckCanChange(out var reason))
        {
            ShowMessage(reason);
            return;
        }

        var commit = proposal.Apply();
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

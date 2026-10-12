using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;

namespace OpenNest.Forms;

/// <summary>One-plate, read-only main-canvas cutting proposal inspection.</summary>
internal sealed class CuttingInspectionPanel : UserControl
{
    private readonly EditNestForm editor;
    private readonly PlateView view;
    private readonly Plate plate;
    private readonly CuttingParameters parameters;
    private readonly SynchronizationContext context;
    private readonly (float Scale, PointF Origin) viewport;
    private readonly OverlapDisplayMode overlapDisplay;
    private readonly CuttingProposalInspection inspection = new();
    private readonly Button planButton = new() { Text = "Plan", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Cancel / Close", AutoSize = true };
    private readonly Label resultLabel = new() { AutoSize = false, Dock = DockStyle.Fill };
    private ValidationInspectionScope scope;
    private bool closed;
    private int generation;

    internal CuttingInspectionPanel(EditNestForm editor, CuttingParameters parameters)
    {
        this.editor = editor;
        view = editor.PlateView;
        plate = view.Plate;
        this.parameters = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        viewport = view.CaptureViewport();
        overlapDisplay = view.OverlapDisplay;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        buttons.Controls.AddRange([planButton, cancelButton]);
        Controls.Add(resultLabel);
        Controls.Add(buttons);
        Padding = new Padding(10);
        planButton.Click += (_, _) => StartPlanning();
        cancelButton.Click += (_, _) => RequestClose();
        resultLabel.Text = "Read-only proposal inspection. No live changes; Apply is not available in this view.";
    }

    internal bool IsPlanning => inspection.IsRunning;
    internal Task PlanningTask { get; private set; } = Task.CompletedTask;
    internal Action<CancellationToken> BeforePlan { get; set; }
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
        view.ClearProposalDisplay();
        planButton.Enabled = false;
        resultLabel.Text = "Planning… Read-only; the live plate is unchanged.";
        try
        {
            // Capture on the owner thread; no worker recapture of the live plate or settings.
            var batch = CuttingPlanBatch.Capture([plate], parameters, false,
                [editor.Nest.Plates.IndexOf(plate) + 1]);
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
            planButton.Enabled = true;
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
            planButton.Enabled = true;
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
            resultLabel.Text = string.Join(Environment.NewLine, work.Result.Describe(UnitsHelper.GetShortString(editor.Nest.Units)));
            var preview = inspection.RefreshPreview();
            if (preview == null)
            {
                resultLabel.Text += "\nNo current preview: the plate is stale, blocked or unsupported. Live plate shown.";
                return;
            }
            resultLabel.Text = (work.Result.Plates[0].IsReady ? "PROPOSAL" : "UNVERIFIED PROPOSAL")
                + " — numbered in proposed cutting order; read-only.\n" + resultLabel.Text;
            view.ShowProposalDisplay(preview, IsPreviewCurrent);
            view.ZoomToFit();
        }
        catch (Exception ex)
        {
            view.ClearProposalDisplay();
            resultLabel.Text = $"Unable to show the plan: {ex.Message}. No current preview.";
            planButton.Enabled = true;
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private bool IsPreviewCurrent()
    {
        if (!closed && ReferenceEquals(view.Plate, plate) && inspection.RefreshPreview() != null)
            return true;
        resultLabel.Text = "No current preview: the plate changed. Replan to inspect it. Nothing applied.";
        return false;
    }

    internal void RequestClose()
    {
        if (closed)
            return;
        view.ClearProposalDisplay();
        planButton.Enabled = false;
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
                view.RestoreViewport(viewport);
                view.OverlapDisplay = overlapDisplay;
            }
        }
        var ownedScope = scope;
        ownedScope?.Dispose();
        scope = null;
        editor.FinishCuttingInspection(this);
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

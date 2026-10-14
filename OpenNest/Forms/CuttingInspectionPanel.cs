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

internal enum CuttingInspectionMode { Plan, Validate }

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
    private CuttingProposalInspection inspection = new();
    private readonly ComboBox modeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private CancellationTokenSource validationCancellation;
    private bool closeRequested;
    private CuttingInspectionMode? pendingMode;
    private bool configuringMode;
    internal CuttingInspectionMode Mode { get; private set; }
    internal PostVerificationReport ValidationReport { get; private set; }
    internal Func<Nest, CancellationToken, Task<PostVerificationReport>> Analyze { get; set; }
        = (nest, token) => Task.Run(() => PostVerificationAnalyzer.Analyze(nest, token), token);
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
    private readonly CheckBox autoCutOffsCheckBox = new() { AutoSize = true };
    private readonly FlowLayoutPanel existingWorkBanner = new()
    {
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        BackColor = Color.FromArgb(192, 0, 0),
        Padding = new Padding(6),
        Margin = new Padding(3, 3, 3, 6),
        Visible = false
    };
    private readonly Label existingWorkLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(428, 0),
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        AccessibleName = "Existing lead-ins and cut-offs warning"
    };
    private readonly Button clearExistingButton = new()
    {
        Text = "Clear Lead-ins && Cut-offs…",
        AutoSize = true,
        BackColor = SystemColors.Control,
        UseVisualStyleBackColor = true
    };
    private readonly Button cutOffSettingsButton = new() { Text = "Cut-off Settings…", AutoSize = true };
    private readonly Units units;
    private readonly bool allPlates;
    private AutomaticCutOffOptions cutOffOptions;
    // Last cut-off settings chosen here, per unit system, for the rest of this session.
    private static readonly System.Collections.Generic.Dictionary<Units, AutomaticCutOffOptions> lastCutOffOptions = new();
    private AutomaticCutOffStaging stagedCutOffs;
    private readonly CheckBox acceptWarningsCheckBox = new() { Text = "Accept UNVERIFIED plan warnings (post consent happens separately)", AutoSize = true, Visible = false };
    private readonly Label settingsLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Label previewLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Label validationScopeLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(440, 0),
        Visible = false,
        Text = "LIVE NEST only — reviews overlaps, lead-ins and rapid crossings. Confirm minimum spacing, plate boundaries and final post output separately, before posting."
    };
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

    internal CuttingInspectionPanel(EditNestForm editor, CuttingParameters parameters, bool allPlates = false,
        CuttingInspectionMode mode = CuttingInspectionMode.Plan)
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
        modeSelector.Items.AddRange(["Plan Cutting", "Validate Nest (live programs)"]);
        Mode = mode;
        modeSelector.SelectedIndex = (int)mode;
        modeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!configuringMode) SwitchMode((CuttingInspectionMode)modeSelector.SelectedIndex);
        };
        this.allPlates = allPlates;
        units = editor.Nest.Units;
        cutOffOptions = lastCutOffOptions.TryGetValue(units, out var last) ? Copy(last) : DefaultCutOffOptions(units);
        ShowCutOffOptions();
        var cutOffRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        cutOffRow.Controls.AddRange([autoCutOffsCheckBox, cutOffSettingsButton]);
        existingWorkLabel.Font = new Font(Font, FontStyle.Bold);
        existingWorkBanner.Controls.AddRange([existingWorkLabel, clearExistingButton]);
        header.Controls.AddRange([modeSelector, existingWorkBanner, settingsLabel, keepOrderCheckBox, cutOffRow, buttons,
            acceptWarningsCheckBox, navigation, previewLabel, validationScopeLabel]);
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
        planButton.Click += (_, _) => StartCurrentMode();
        cancelButton.Click += (_, _) => RequestClose();
        settingsButton.Click += (_, _) => EditSettings();
        keepOrderCheckBox.CheckedChanged += (_, _) => StartPlanning();
        autoCutOffsCheckBox.CheckedChanged += (_, _) => StartPlanning();
        cutOffSettingsButton.Click += (_, _) => EditCutOffSettings();
        clearExistingButton.Click += (_, _) => ClearExistingWork();
        acceptWarningsCheckBox.CheckedChanged += (_, _) => UpdateApply();
        applyButton.Click += (_, _) => Apply();
        previousButton.Click += (_, _) => ShowPlate(selectedPlate - 1);
        nextButton.Click += (_, _) => ShowPlate(selectedPlate + 1);
        warningGrid.SelectionChanged += (_, _) => NavigateFinding();
        ShowSettings();
        resultLabel.Text = "Read-only proposal inspection. Nothing live changes before Apply, except automatic cut-offs "
            + "you choose to add; those are removed again unless you Apply.";
    }

    internal bool IsPlanning => inspection.IsRunning || validationCancellation != null;
    internal Task PlanningTask { get; private set; } = Task.CompletedTask;
    internal Action<CancellationToken> BeforePlan { get; set; }
    internal Action BeforePresent { get; set; }
    internal Action BeforePostApply { get; set; }
    internal Action<Exception> OnPostApplyError { get; set; }
    internal CuttingPlanProposal Proposal => inspection.Proposal;

    /// <summary>Asks before discarding applied lead-ins and cut-offs; null shows a Yes/No box (tests replace it).</summary>
    internal Func<string, bool> ConfirmClearExisting { get; set; }

    /// <summary>Automatic cut-offs added to the live plates for the current plan, until Apply keeps them.</summary>
    internal AutomaticCutOffStaging StagedCutOffs => stagedCutOffs;

    /// <summary>Shows the cut-off settings dialog; tests replace it. Null uses ShowDialog.</summary>
    internal Func<AutomaticCutOffForm, DialogResult> ShowCutOffSettings { get; set; }

    internal AutomaticCutOffOptions CutOffOptions => Copy(cutOffOptions);

    private static AutomaticCutOffOptions Copy(AutomaticCutOffOptions options) => new()
    {
        Spacing = options.Spacing,
        MinimumStripLength = options.MinimumStripLength,
        MinimumTailLength = options.MinimumTailLength,
    };

    private void ShowCutOffOptions()
    {
        var unit = UnitsHelper.GetShortString(units);
        autoCutOffsCheckBox.Text = $"Add automatic scrap cut-offs ({cutOffOptions.Spacing:0.###} {unit} strips, "
            + $"keep tails of {cutOffOptions.MinimumTailLength:0.###} {unit} or more)";
    }

    /// <summary>
    /// Opens the Automatic Scrap Cut-offs dialog in settings mode for this panel's scope. Unapplied
    /// cut-offs come off first so its preview starts from the plates' real state; OK ticks the box
    /// and replans with the chosen settings, Cancel replans with the previous ones.
    /// </summary>
    private void EditCutOffSettings()
    {
        if (closed || closeRequested || IsPlanning || Mode != CuttingInspectionMode.Plan)
            return;
        ClearResult();
        try
        {
            RevertStagedCutOffs();
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Unable to remove the unapplied automatic cut-offs: {ex.Message}";
            return;
        }
        RefreshExistingWork();
        AutomaticCutOffOptions chosen = null;
        try
        {
            using var form = AutomaticCutOffForm.ForSettings(view, units, allPlates ? editor.Nest : null, cutOffOptions);
            var result = ShowCutOffSettings?.Invoke(form) ?? form.ShowDialog(this);
            if (result == DialogResult.OK)
                chosen = form.SelectedOptions;
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Unable to open the cut-off settings: {ex.Message}";
        }
        finally
        {
            if (!view.IsDisposed)
                view.ClearPreviewParts();
        }
        if (closed || closeRequested)
            return;
        if (chosen != null)
        {
            cutOffOptions = Copy(chosen);
            lastCutOffOptions[units] = Copy(chosen);
            ShowCutOffOptions();
            if (!autoCutOffsCheckBox.Checked)
            {
                autoCutOffsCheckBox.Checked = true; // Replans through CheckedChanged.
                return;
            }
        }
        StartPlanning();
    }

    /// <summary>The automatic cut-off dialog's defaults: 35 in strips, half-strip minimum, 12 in tail.</summary>
    internal static AutomaticCutOffOptions DefaultCutOffOptions(Units units)
    {
        var spacing = units == Units.Millimeters ? 889 : 35;
        return new AutomaticCutOffOptions
        {
            Spacing = spacing,
            MinimumStripLength = spacing / 2.0,
            MinimumTailLength = units == Units.Millimeters ? 304.8 : 12,
        };
    }

    internal void Open()
    {
        scope = new ValidationInspectionScope(editor, this, editor.SidePanelHeader, RequestClose);
        view.InspectionCloseRequested = RequestClose;
        view.OverlapDisplay = OverlapDisplayMode.Off;
        view.ClearValidationFinding();
        ConfigureMode();
        StartCurrentMode();
    }

    private void StartCurrentMode()
    {
        if (Mode == CuttingInspectionMode.Validate) StartValidation();
        else StartPlanning();
    }

    private void ConfigureMode()
    {
        configuringMode = true;
        modeSelector.SelectedIndex = (int)Mode;
        configuringMode = false;
        var planning = Mode == CuttingInspectionMode.Plan;
        settingsButton.Visible = settingsLabel.Visible = keepOrderCheckBox.Visible = applyButton.Visible = planning;
        autoCutOffsCheckBox.Visible = cutOffSettingsButton.Visible = planning;
        RefreshExistingWork();
        previousButton.Visible = nextButton.Visible = planning;
        validationScopeLabel.Visible = !planning;
        planButton.Text = planning ? "Replan" : "Recheck all plates";
        previewLabel.Text = planning ? "No current preview." : "LIVE NEST — read-only validation of all plates.";
    }

    internal void SwitchMode(CuttingInspectionMode mode)
    {
        if (closed || closeRequested || mode == Mode && pendingMode == null) return;
        pendingMode = mode;
        ClearResult();
        SetPlanning(true);
        resultLabel.Text = "Switching modes… waiting for the worker.";
        if (validationCancellation != null) validationCancellation.Cancel();
        if (inspection.IsRunning) inspection.RequestClose();
        if (!IsPlanning) FinishTransition();
    }

    private void FinishTransition()
    {
        if (closeRequested) { FinishClose(); return; }
        if (pendingMode is not { } mode) return;
        pendingMode = null;
        RestoreLiveView();
        inspection = new CuttingProposalInspection();
        Mode = mode;
        ConfigureMode();
        StartCurrentMode();
    }

    private void RestoreLiveView()
    {
        view.ClearProposalDisplay();
        view.ClearValidationFinding();
        if (view.IsDisposed) return;
        var index = editor.Nest.Plates.ToList().IndexOf(plate);
        if (index >= 0 && !ReferenceEquals(view.Plate, plate)) editor.PlateManager.LoadAt(index);
        view.RestoreViewport(viewport);
    }

    private void StartValidation()
    {
        if (closed || closeRequested || IsPlanning) return;
        ClearResult();
        try { RevertStagedCutOffs(); } // Validate what is really on the plates, not an unapplied plan.
        catch (Exception ex)
        {
            resultLabel.Text = $"Unable to remove the unapplied automatic cut-offs: {ex.Message}";
            return;
        }
        ConfigureMode();
        SetPlanning(true);
        resultLabel.Text = "Checking all live plates: overlaps, missing lead-ins and rapid crossings…";
        var cancellation = validationCancellation = new CancellationTokenSource();
        var run = ++generation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlanningTask = completion.Task;
        // The inspection scope keeps editors and posting from mutating these live inputs.
        Task<PostVerificationReport> work;
        try { work = Analyze(editor.Nest, cancellation.Token); }
        catch (Exception ex) { work = Task.FromException<PostVerificationReport>(ex); }
        work.ContinueWith(task => context.Post(_ =>
        {
            try
            {
                validationCancellation = null;
                cancellation.Dispose();
                if (closeRequested || pendingMode != null) { FinishTransition(); return; }
                if (closed || run != generation) return;
                SetPlanning(false);
                if (!task.IsCompletedSuccessfully)
                {
                    resultLabel.Text = task.IsCanceled ? "Validation cancelled. Run it again to review this nest."
                        : $"Validation failed. Run it again to review this nest. {task.Exception?.GetBaseException().Message}";
                    return;
                }
                ValidationReport = task.Result;
                resultLabel.Text = ValidationReport.ToDisplayText("Nest validation (live programs)");
                previewLabel.Text = ValidationReport.Findings.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? "Validation incomplete. Review the findings below."
                    : ValidationReport.HasWarnings ? "Warnings found. Review the findings below."
                    : "Validation complete. No warnings found by these checks.";
                populatingWarnings = true;
                try
                {
                    foreach (var finding in ValidationReport.Findings)
                    {
                        var index = warningGrid.Rows.Add(finding.PlateNumber, finding.PartNumber, finding.OtherPartNumber,
                            $"{finding.Kind}: {finding.Message}");
                        warningGrid.Rows[index].Tag = finding;
                    }
                    warningGrid.ClearSelection();
                }
                finally { populatingWarnings = false; }
            }
            catch (Exception ex)
            {
                view.ClearValidationFinding();
                resultLabel.Text = $"Validation failed. Run it again to review this nest. {ex.Message}";
            }
            finally { completion.TrySetResult(); }
        }, null), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void NavigateValidationFinding()
    {
        if (closed || closeRequested || pendingMode != null || populatingWarnings || IsPlanning || editor.IsDisposed)
            return;
        view.ClearValidationFinding();
        if (warningGrid.SelectedRows.Count == 0 || warningGrid.SelectedRows[0].Tag is not PostVerificationFinding finding
            || finding.PlateNumber < 1 || finding.PlateNumber > editor.Nest.Plates.Count) return;
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
            // A narrow docked canvas can put a contact at its edge after zoom.
            // Centre a typed witness, never infer one from the diagnostic text.
            var focus = finding.Kind == PostVerificationKind.RapidCrossing
                && finding.ContactPoints.Any(contact => double.IsFinite(contact.X) && double.IsFinite(contact.Y))
                ? finding.ContactPoints.First(contact => double.IsFinite(contact.X) && double.IsFinite(contact.Y))
                : point;
            view.CenterValidationPoint(focus, new Point(view.Width / 2, view.Height / 2));
        }
        else view.ZoomToFit();
        view.ShowValidationFinding(finding with { Location = location });
        var hint = finding.Kind switch
        {
            PostVerificationKind.Overlap when finding.ClampId != null =>
                "The ring marks a nominal cut-off contacting a clamp; no material-overlap area is implied.",
            PostVerificationKind.Overlap => "Shaded material is the overlap. The ring marks its area centroid, which can lie between separate patches.",
            PostVerificationKind.RapidCrossing when finding.ClampId != null =>
                "The crosshair marks the rapid's contact with the clamp. Head height is not modeled.",
            PostVerificationKind.RapidCrossing => finding.ContactPoints.Count > 0
                ? "Crosshairs mark contacts with the completed contour. The pulsing dashed orange line shows the rapid."
                : "The pulsing dashed orange line shows the rapid inside the completed contour; there is no boundary contact point to mark.",
            PostVerificationKind.MissingLeadIn => "The ring marks the cutting contour start where a lead-in is missing.",
            _ => "The ring marks the affected part's center when available; no exact problem location is known."
        };
        // Keep the complete report visible; selecting a finding does not hide completeness or other warnings.
        resultLabel.Text = $"Plate {finding.PlateNumber}, part {finding.PartNumber}: {finding.Message}\r\n{hint}\r\n\r\n"
            + ValidationReport.ToDisplayText("Nest validation (live programs)");
    }

    internal void StartPlanning()
    {
        if (Mode != CuttingInspectionMode.Plan || closed || IsPlanning || closeRequested || inspection.CloseRequested)
            return;
        ClearResult();
        if (!SyncStagedCutOffs())
        {
            RefreshExistingWork();
            return;
        }
        RefreshExistingWork();
        SetPlanning(true);
        resultLabel.Text = stagedCutOffs != null
            ? "Planning… Automatic cut-offs were added for this plan; they are removed again unless you Apply."
            : "Planning… Read-only; the live plate is unchanged.";
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
            if (closeRequested || pendingMode != null)
            {
                FinishTransition();
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
        ValidationReport = null;
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
        settingsButton.Enabled = keepOrderCheckBox.Enabled = autoCutOffsCheckBox.Enabled = planButton.Enabled = !running;
        cutOffSettingsButton.Enabled = !running;
        clearExistingButton.Enabled = !running;
        previousButton.Enabled = !running && selectedPlate > 0;
        nextButton.Enabled = !running && selectedPlate < plates.Length - 1;
        acceptWarningsCheckBox.Enabled = !running;
        if (running) applyButton.Enabled = false;
    }

    private void UpdateApply() => applyButton.Enabled = Mode == CuttingInspectionMode.Plan && !invalidated && !IsPlanning
        && plates.Any(item => item.Parts.Count > 0)
        && (Proposal?.CanApply == true || Proposal?.CanApplyWithWarnings == true && acceptWarningsCheckBox.Checked);

    private bool IsPreviewCurrent()
    {
        if (!closed && !invalidated && ReferenceEquals(view.Plate, plates[selectedPlate])
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
        // Keep the authoritative editor plate on the selected live source even when
        // no safe detached proposal can be rendered for that plate.
        var sourceIndex = editor.Nest.Plates.IndexOf(plates[index]);
        if (sourceIndex < 0)
        {
            ClearResult();
            resultLabel.Text = "The selected plate changed after capture. Replan to continue.";
            return;
        }
        if (!ReferenceEquals(view.Plate, plates[index]))
            editor.PlateManager.LoadAt(sourceIndex);
        if (!ReferenceEquals(view.Plate, plates[index]))
        {
            ClearResult();
            resultLabel.Text = "The selected plate is no longer current. Replan to continue.";
            return;
        }
        if (plates[index].Parts.Count == 0)
        {
            preview = null;
            previewLabel.Text = "New plate (empty)";
            SetPlanning(false);
            return;
        }
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
        if (Mode == CuttingInspectionMode.Validate)
        {
            NavigateValidationFinding();
            return;
        }
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
            // The plan was made with these cut-offs in place; applying it keeps them.
            stagedCutOffs?.Keep();
            stagedCutOffs = null;
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

    /// <summary>
    /// Adds or removes this panel's automatic cut-offs to match the check box, on the owner
    /// thread before capture, so the plan sequences them. False when planning cannot start.
    /// </summary>
    private bool SyncStagedCutOffs()
    {
        try
        {
            if (!autoCutOffsCheckBox.Checked)
            {
                RevertStagedCutOffs();
                return true;
            }
            if (stagedCutOffs != null)
                return true;
            var staging = AutomaticCutOffBatch.Stage(plates, cutOffOptions, view.CutOffSettings);
            if (staging.IsBlocked)
            {
                var reasons = staging.Plans.SelectMany((plan, index) => plan.Diagnostics.Where(d => d.IsBlocking)
                    .Select(d => $"Plate {plateNumbers[index]}: {d.Message}"));
                resultLabel.Text = "Automatic cut-offs were not added, so nothing was planned:" + Environment.NewLine
                    + string.Join(Environment.NewLine, reasons) + Environment.NewLine
                    + "Untick \"Add automatic scrap cut-offs\" to plan without them, or clear or fix the existing cut-offs.";
                return false;
            }
            stagedCutOffs = staging.CanRevert ? staging : null;
            view.Invalidate();
            return true;
        }
        catch (Exception ex)
        {
            resultLabel.Text = $"Automatic cut-offs could not be added: {ex.Message} Nothing was planned.";
            return false;
        }
    }

    private void RevertStagedCutOffs()
    {
        var staged = stagedCutOffs;
        stagedCutOffs = null;
        if (staged == null)
            return;
        staged.Revert();
        if (!view.IsDisposed)
            view.Invalidate();
    }

    private void RefreshExistingWork()
    {
        var summary = AppliedCuttingWork.Summarize(plates, plateNumbers, stagedCutOffs?.Definitions);
        existingWorkLabel.Text = summary.Describe();
        existingWorkBanner.Visible = Mode == CuttingInspectionMode.Plan && summary.HasExistingWork;
    }

    private void ClearExistingWork()
    {
        if (closed || closeRequested || IsPlanning || Mode != CuttingInspectionMode.Plan)
            return;
        var summary = AppliedCuttingWork.Summarize(plates, plateNumbers, stagedCutOffs?.Definitions);
        if (!summary.HasExistingWork)
            return;
        var message = $"Remove the applied lead-ins ({summary.LeadInParts} parts, {summary.LockedLeadInParts} locked) and "
            + $"{summary.CutOffs} cut-offs from plate(s) {string.Join(", ", summary.PlateNumbers)}?"
            + Environment.NewLine + Environment.NewLine
            + "Locked and hand-placed lead-ins are removed too, and this cannot be undone. The plan is then recalculated.";
        var confirmed = ConfirmClearExisting?.Invoke(message)
            ?? MessageBox.Show(editor, message, "Clear Lead-ins and Cut-offs", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (!confirmed || closed || closeRequested || IsPlanning)
            return;
        ClearResult();
        try
        {
            RevertStagedCutOffs();
            AppliedCuttingWork.Clear(plates, plateNumbers);
        }
        catch (Exception ex)
        {
            RefreshExistingWork();
            resultLabel.Text = $"Clearing stopped part-way: {ex.Message} Review the plates before planning or posting.";
            return;
        }
        finally
        {
            foreach (var part in view.Parts)
            {
                part.IsDirty = true;
                part.Update();
            }
            view.Invalidate();
            editor.UpdatePlateList();
        }
        StartPlanning();
    }

    internal void RequestClose()
    {
        if (closed)
            return;
        closeRequested = true;
        pendingMode = null;
        ClearResult();
        SetPlanning(true);
        cancelButton.Enabled = false;
        resultLabel.Text = "Cancelling… waiting for the worker. Nothing has changed.";
        validationCancellation?.Cancel();
        inspection.RequestClose();
        if (!IsPlanning)
            FinishClose();
    }

    internal Action<Exception> OnRestoreError { get; set; }

    private void FinishClose()
    {
        if (closed)
            return;
        closed = true;
        Exception restoreError = null;
        var ownedScope = scope;
        scope = null;
        try
        {
            RevertStagedCutOffs(); // Closing without Apply leaves the plates as they were.
        }
        catch (Exception ex)
        {
            restoreError = ex;
        }
        try
        {
            RestoreLiveView();
            if (!view.IsDisposed && ReferenceEquals(view.Plate, plate))
                view.OverlapDisplay = overlapDisplay;
        }
        catch (Exception ex)
        {
            restoreError = ex;
        }
        finally
        {
            // Navigation can invoke arbitrary plate-change observers. Even if one throws,
            // release the scope and panel so a stopped worker never strands editing.
            if (!view.IsDisposed)
                view.InspectionCloseRequested = null;
            try { ownedScope?.Dispose(); }
            catch (Exception ex) { restoreError ??= ex; }
            try { editor.FinishCuttingInspection(this); }
            catch (Exception ex) { restoreError ??= ex; }
            try
            {
                if (!view.IsDisposed && ReferenceEquals(view.Plate, plate))
                    view.RestoreViewport(viewport);
            }
            catch (Exception ex) { restoreError ??= ex; }
            try { ownedScope?.ResumeHostClose(); }
            catch (Exception ex) { restoreError ??= ex; }
            Dispose();
        }
        if (restoreError != null)
        {
            if (OnRestoreError != null)
                OnRestoreError(restoreError);
            else
                MessageBox.Show(editor.IsDisposed ? null : editor,
                    "Inspection closed, but the original view could not be fully restored: " + restoreError.Message,
                    "Cutting Inspection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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

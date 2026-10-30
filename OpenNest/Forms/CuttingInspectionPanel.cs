using System;
using System.Drawing;
using System.Globalization;
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
using OpenNest.Engine.Sequencing;
using OpenNest.Undo;

namespace OpenNest.Forms;

internal enum CuttingInspectionMode { Plan, Validate }

/// <summary>Owner-thread planning and atomic Apply beside the read-only main canvas.</summary>
internal sealed class CuttingInspectionPanel : UserControl
{
    private readonly EditNestForm editor;
    private readonly PlateView view;
    private readonly Plate plate;
    private CuttingParameters parameters;
    private Plate[] plates;
    private int[] plateNumbers;
    private int selectedPlate;
    private Plate preview;
    private bool populatingWarnings;
    private bool invalidated;
    private readonly SynchronizationContext context;
    private readonly (float Scale, PointF Origin) viewport;
    private readonly OverlapDisplayMode overlapDisplay;
    private CuttingProposalInspection inspection = new();
    private readonly ComboBox modeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox scopeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private CancellationTokenSource validationCancellation;
    private bool closeRequested;
    private CuttingInspectionMode? pendingMode;
    private bool configuringMode;
    private bool pendingReplan;
    private bool pendingClear;
    private bool pendingCutOffSettings;
    // A scope chosen while a plan ran; applied once that run's cancellation lands.
    private bool? pendingScope;
    // The running plan's headline, re-shown each second with its elapsed time; null when not ticking.
    private string planningStatus;
    private readonly System.Diagnostics.Stopwatch planningClock = new();
    private readonly System.Windows.Forms.Timer elapsedTimer = new() { Interval = 1000 };
    internal CuttingInspectionMode Mode { get; private set; }
    internal PostVerificationReport ValidationReport { get; private set; }
    internal Func<Nest, CancellationToken, Task<PostVerificationReport>> Analyze { get; set; }
        = (nest, token) => Task.Run(() => PostVerificationAnalyzer.Analyze(nest, token), token);
    private readonly Button planButton = new() { Text = "Replan", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Close", AutoSize = true };
    // One headline the operator reads first; detail holds at most a remedy or a marker hint.
    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(410, 0),
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
        AccessibleName = "Cutting plan outcome"
    };
    private readonly StatusBanner statusBanner = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 8) };
    private readonly Label detailLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(440, 0),
        AccessibleName = "Cutting plan remedy or marker hint"
    };
    private readonly ToolTip toolTip = new();
    private readonly Button settingsButton = new() { Text = "Cutting Settings…", AutoSize = true };
    private readonly Button applyButton = new() { Text = "Apply", AutoSize = true, Enabled = false };
    private readonly Button previousButton = new() { Text = "Previous", AutoSize = true };
    private readonly Button nextButton = new() { Text = "Next", AutoSize = true };
    private readonly CheckBox keepOrderCheckBox = new() { Text = "Keep current part order", AutoSize = true };
    private readonly CheckBox autoCutOffsCheckBox = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly StatusBanner existingWorkBanner = new() { Margin = new Padding(3, 3, 3, 8) };
    private readonly Label existingWorkLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(410, 0),
        Margin = new Padding(0, 0, 0, 6),
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
    private bool allPlates;
    private AutomaticCutOffOptions cutOffOptions;
    // Last cut-off settings chosen here, per unit system, for the rest of this session.
    private static readonly System.Collections.Generic.Dictionary<Units, AutomaticCutOffOptions> lastCutOffOptions = new();
    private AutomaticCutOffStaging stagedCutOffs;

    // Open from staging scrap cut-offs (or the apply) until the plan is applied or the staged
    // cut-offs come back off: they become an edit only when the plan is applied.
    private ChangeScope planChange;
    private readonly CheckBox acceptWarningsCheckBox = new() { Text = "Accept the unverified plan", AutoSize = true, Visible = false };
    private readonly Label settingsLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(300, 0),
        ForeColor = SystemColors.GrayText,
        Anchor = AnchorStyles.Left,
    };
    private readonly Label previewLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly Label validationScopeLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(440, 0),
        Visible = false,
        ForeColor = SystemColors.GrayText,
        Text = "Checks the live nest for overlaps, missing lead-ins and rapid crossings. "
            + "Spacing, plate edges and post output are not checked."
    };
    private readonly DataGridView warningGrid = GridStyle.Apply(new DataGridView
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoGenerateColumns = false
    });
    // Every part in scope, including ones with no finding, so "no rows" never reads as "all clear".
    private readonly DataGridView partsGrid = GridStyle.Apply(new DataGridView
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoGenerateColumns = false
    });
    private readonly TabControl resultTabs = new() { Dock = DockStyle.Fill };
    private ValidationInspectionScope scope;
    private bool closed;
    private int generation;

    internal CuttingInspectionPanel(EditNestForm editor, CuttingParameters parameters, bool allPlates = false,
        CuttingInspectionMode mode = CuttingInspectionMode.Plan)
    {
        this.editor = editor;
        view = editor.PlateView;
        plate = view.Plate;
        SetPlates(allPlates);
        this.parameters = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        viewport = view.CaptureViewport();
        overlapDisplay = view.OverlapDisplay;
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var settingsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        settingsRow.Controls.AddRange([settingsButton, settingsLabel]);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange([planButton, applyButton, cancelButton]);
        var navigation = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        navigation.Controls.AddRange([previousButton, nextButton]);
        statusLabel.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
        toolTip.SetToolTip(acceptWarningsCheckBox, "Some checks did not pass. Posting still runs its own checks and asks separately.");
        toolTip.SetToolTip(keepOrderCheckBox, "Plan lead-ins and cut direction without changing which part is cut first.");
        modeSelector.Items.AddRange(["Plan Cutting", "Validate Nest (live programs)"]);
        Mode = mode;
        modeSelector.SelectedIndex = (int)mode;
        modeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!configuringMode) SwitchMode((CuttingInspectionMode)modeSelector.SelectedIndex);
        };
        toolTip.SetToolTip(scopeSelector, "Plan the plate on screen, or every plate with parts in one plan.");
        scopeSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!configuringMode) ChangeScope(scopeSelector.SelectedIndex == 1);
        };
        units = editor.Nest.Units;
        cutOffOptions = lastCutOffOptions.TryGetValue(units, out var last) ? Copy(last) : DefaultCutOffOptions(units);
        ShowCutOffOptions();
        var cutOffRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        cutOffRow.Controls.AddRange([autoCutOffsCheckBox, cutOffSettingsButton]);
        existingWorkLabel.Font = new Font(Font, FontStyle.Bold);
        existingWorkBanner.Controls.AddRange([existingWorkLabel, clearExistingButton]);
        existingWorkBanner.SetTone(Tone.Bad);
        header.Controls.AddRange([modeSelector, scopeSelector, existingWorkBanner, settingsRow, keepOrderCheckBox, cutOffRow,
            validationScopeLabel, buttons, acceptWarningsCheckBox, navigation, previewLabel, detailLabel]);
        statusBanner.Controls.Add(statusLabel);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(statusBanner, 0, 0);
        layout.Controls.Add(header, 0, 1);
        var issuesTab = new TabPage("Issues");
        issuesTab.Controls.Add(warningGrid);
        var partsTab = new TabPage("Parts");
        partsTab.Controls.Add(partsGrid);
        resultTabs.TabPages.AddRange([issuesTab, partsTab]);
        layout.Controls.Add(resultTabs, 0, 2);
        Controls.Add(layout);
        foreach (var name in new[] { "Plate", "Part", "With", "Issue" })
            warningGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                Width = name is "Issue" or "Status" ? 260 : 48,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        foreach (var name in new[] { "Plate", "Part", "Status" })
            partsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                Width = name is "Issue" or "Status" ? 260 : 48,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        Padding = new Padding(10);
        planButton.Click += (_, _) => StartCurrentMode();
        cancelButton.Click += (_, _) => RequestClose();
        settingsButton.Click += (_, _) => EditSettings();
        keepOrderCheckBox.CheckedChanged += (_, _) => RestartPlanning();
        elapsedTimer.Tick += (_, _) =>
        {
            if (planningStatus != null && !closed) ShowElapsed();
        };
        autoCutOffsCheckBox.CheckedChanged += (_, _) => RestartPlanning();
        cutOffSettingsButton.Click += (_, _) => EditCutOffSettings();
        clearExistingButton.Click += (_, _) => ClearExistingWork();
        acceptWarningsCheckBox.CheckedChanged += (_, _) => UpdateApply();
        applyButton.Click += (_, _) => Apply();
        previousButton.Click += (_, _) => ShowPlate(selectedPlate - 1);
        nextButton.Click += (_, _) => ShowPlate(selectedPlate + 1);
        warningGrid.SelectionChanged += (_, _) => NavigateFinding();
        partsGrid.SelectionChanged += (_, _) => NavigatePart();
        ShowSettings();
    }

    private enum Tone { Neutral, Good, Warning, Bad }

    /// <summary>Sets the headline banner and replaces the detail line (empty hides either).</summary>
    private void SetStatus(Tone tone, string text, string detail = null)
    {
        planningStatus = null; // Any other headline stops the elapsed-time ticker.
        elapsedTimer.Stop();
        statusLabel.Text = text;
        statusBanner.SetTone(tone);
        statusBanner.Visible = !string.IsNullOrEmpty(text);
        SetDetail(detail);
    }

    /// <summary>Shows a running plan's headline with the run's elapsed time, refreshed every second.</summary>
    private void SetPlanningStatus(string text)
    {
        SetStatus(Tone.Neutral, text);
        planningStatus = text;
        ShowElapsed();
        elapsedTimer.Start();
    }

    private void ShowElapsed() => statusLabel.Text = $"{planningStatus} [{FormatElapsed(planningClock.Elapsed)}]";

    internal static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalMinutes >= 1
        ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s"
        : $"{elapsed.Seconds}s";

    private void SetDetail(string text)
    {
        detailLabel.Text = text ?? string.Empty;
        detailLabel.Visible = !string.IsNullOrEmpty(text);
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
        KeepHorizontalRemnant = options.KeepHorizontalRemnant,
    };

    private void ShowCutOffOptions()
    {
        var unit = UnitsHelper.GetShortString(units);
        autoCutOffsCheckBox.Text = $"Add scrap cut-offs ({cutOffOptions.Spacing:0.###} {unit} strips)";
        toolTip.SetToolTip(autoCutOffsCheckBox, $"Keeps tails of {cutOffOptions.MinimumTailLength:0.###} {unit} or more. "
            + "The cut-offs are added for planning and removed again unless you Apply.");
    }

    /// <summary>
    /// Opens the Automatic Scrap Cut-offs dialog in settings mode for this panel's scope. Unapplied
    /// cut-offs come off first so its preview starts from the plates' real state; OK ticks the box
    /// and replans with the chosen settings, Cancel replans with the previous ones.
    /// </summary>
    private void EditCutOffSettings()
    {
        if (closed || closeRequested || pendingMode != null || Mode != CuttingInspectionMode.Plan)
            return;
        if (IsPlanning)
        {
            // The dialog changes the live plates, so it opens once the running plan has canceled.
            if (pendingCutOffSettings || pendingClear)
                return;
            pendingCutOffSettings = true;
            pendingReplan = true;
            ClearResult();
            SetPlanning(true);
            SetStatus(Tone.Neutral, "Canceling the plan to edit the cut-off settings…");
            if (!inspection.CloseRequested)
                inspection.RequestClose();
            return;
        }
        ClearResult();
        try
        {
            RevertStagedCutOffs();
        }
        catch (Exception ex)
        {
            SetStatus(Tone.Bad, "Couldn't remove the unapplied cut-offs.", ex.Message);
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
            SetStatus(Tone.Bad, "Couldn't open the cut-off settings.", ex.Message);
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
            KeepHorizontalRemnant = true,
        };
    }

    internal void Open()
    {
        scope = new ValidationInspectionScope(editor, RequestClose, this, editor.SidePanelHeader);
        view.InspectionCloseRequested = RequestClose;
        view.OverlapDisplay = OverlapDisplayMode.Off;
        view.ClearValidationFinding();
        ConfigureMode();
        StartCurrentMode();
    }

    private void StartCurrentMode()
    {
        if (Mode == CuttingInspectionMode.Validate) StartValidation();
        else RestartPlanning();
    }

    private void ConfigureMode()
    {
        var planning = Mode == CuttingInspectionMode.Plan;
        configuringMode = true;
        modeSelector.SelectedIndex = (int)Mode;
        ShowScope(planning);
        configuringMode = false;
        settingsButton.Visible = settingsLabel.Visible = keepOrderCheckBox.Visible = applyButton.Visible = planning;
        autoCutOffsCheckBox.Visible = cutOffSettingsButton.Visible = planning;
        RefreshExistingWork();
        previousButton.Visible = nextButton.Visible = planning && plates.Length > 1;
        previewLabel.Visible = planning;
        validationScopeLabel.Visible = !planning;
        planButton.Text = planning ? "Replan" : "Recheck all plates";
        previewLabel.Text = "No current preview.";
    }

    /// <summary>True when this panel plans every plate with parts rather than only the plate on screen.</summary>
    internal bool AllPlates => allPlates;

    /// <summary>Saves the scope chosen in the panel for the next toolbar open; tests replace it.</summary>
    internal Action<bool> RememberScope { get; set; } = all =>
    {
        Properties.Settings.Default.CuttingPlanAllPlates = all;
        Properties.Settings.Default.Save();
    };

    /// <summary>Scopes the panel to the plate on screen or to every plate with parts (that plate when none has).</summary>
    private void SetPlates(bool all)
    {
        allPlates = all;
        var nestPlates = editor.Nest.Plates.ToList();
        var current = nestPlates.Contains(view.Plate) ? view.Plate : plate;
        plates = ScopePlates(nestPlates, current, all);
        plateNumbers = plates.Select(p => nestPlates.IndexOf(p) + 1).ToArray();
        selectedPlate = System.Math.Max(0, Array.IndexOf(plates, current));
    }

    private static Plate[] ScopePlates(System.Collections.Generic.List<Plate> nestPlates, Plate current, bool all)
    {
        var scoped = all ? nestPlates.Where(p => p.Parts.Count > 0).ToArray() : [current];
        return scoped.Length == 0 ? [current] : scoped;
    }

    // Shown only when the two scopes would plan different plates.
    private void ShowScope(bool planning)
    {
        var nestPlates = editor.Nest.Plates.ToList();
        var current = nestPlates.Contains(view.Plate) ? view.Plate : plate;
        var everyPlate = ScopePlates(nestPlates, current, true);
        scopeSelector.Items.Clear();
        scopeSelector.Items.AddRange(["Current plate only", $"All plates ({everyPlate.Length})"]);
        scopeSelector.SelectedIndex = allPlates ? 1 : 0;
        scopeSelector.Visible = planning && (allPlates || everyPlate.Length > 1 || !ReferenceEquals(everyPlate[0], current));
    }

    /// <summary>Replans for the chosen scope; a running plan is canceled first so the plates never change under it.</summary>
    internal void ChangeScope(bool all)
    {
        if (closed || closeRequested || pendingMode != null || Mode != CuttingInspectionMode.Plan
            || all == (pendingScope ?? allPlates))
            return;
        RememberScope?.Invoke(all);
        if (IsPlanning)
        {
            pendingScope = all;
            if (inspection.CloseRequested)
                return; // Already finishing; the scope is applied when that lands.
            pendingReplan = true;
            ClearResult();
            SetPlanning(true);
            SetStatus(Tone.Neutral, "Restarting…");
            inspection.RequestClose();
            return;
        }
        if (ApplyScope(all))
            StartPlanning();
    }

    // Takes the unapplied cut-offs off the old scope's plates before rescoping; false when they couldn't come off.
    private bool ApplyScope(bool all)
    {
        ClearResult();
        try
        {
            RevertStagedCutOffs();
        }
        catch (Exception ex)
        {
            SetStatus(Tone.Bad, "Couldn't remove the unapplied cut-offs.", ex.Message);
            configuringMode = true;
            ShowScope(true); // The plates stay in the old scope, so the selector does too.
            configuringMode = false;
            return false;
        }
        SetPlates(all);
        ConfigureMode();
        return true;
    }

    internal void SwitchMode(CuttingInspectionMode mode)
    {
        if (closed || closeRequested || mode == Mode && pendingMode == null) return;
        pendingMode = mode;
        ClearResult();
        SetPlanning(true);
        SetStatus(Tone.Neutral, "Switching…");
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
            SetStatus(Tone.Bad, "Couldn't remove the unapplied cut-offs.", ex.Message);
            return;
        }
        ConfigureMode();
        SetPlanning(true);
        SetStatus(Tone.Neutral, "Checking all plates…");
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
                    if (task.IsCanceled)
                        SetStatus(Tone.Neutral, "Validation canceled.", "Recheck to review this nest.");
                    else
                        SetStatus(Tone.Bad, "Validation failed. Recheck to review this nest.",
                            task.Exception?.GetBaseException().Message);
                    return;
                }
                ValidationReport = task.Result;
                ShowValidationStatus();
                populatingWarnings = true;
                try
                {
                    foreach (var finding in ValidationReport.Findings)
                    {
                        var index = warningGrid.Rows.Add(finding.PlateNumber, finding.PartNumber, finding.OtherPartNumber,
                            $"{KindName(finding.Kind)}: {finding.Message}");
                        warningGrid.Rows[index].Tag = finding;
                    }
                    warningGrid.ClearSelection();
                    PopulatePartsGrid();
                }
                finally { populatingWarnings = false; }
            }
            catch (Exception ex)
            {
                view.ClearValidationFinding();
                SetStatus(Tone.Bad, "Validation failed. Recheck to review this nest.", ex.Message);
            }
            finally { completion.TrySetResult(); }
        }, null), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void ShowValidationStatus()
    {
        var findings = ValidationReport.Findings;
        if (findings.Any(f => f.Kind == PostVerificationKind.Incomplete))
            SetStatus(Tone.Bad, "Validation incomplete: some checks could not run.", SelectIssueHint);
        else if (findings.Count == 0)
            SetStatus(Tone.Good, "No warnings found.");
        else
        {
            var counts = findings.GroupBy(f => f.Kind).OrderBy(g => g.Key)
                .Select(g => Count(g.Count(), KindName(g.Key).ToLowerInvariant()));
            SetStatus(Tone.Warning, $"{Count(findings.Count, "warning")}: {string.Join(", ", counts)}.", SelectIssueHint);
        }
    }

    private const string SelectIssueHint = "Select an issue below to see it on the plate.";

    private static string KindName(PostVerificationKind kind) => kind switch
    {
        PostVerificationKind.Overlap => "Overlap",
        PostVerificationKind.MissingLeadIn => "Missing lead-in",
        PostVerificationKind.RapidCrossing => "Rapid crossing",
        PostVerificationKind.Incomplete => "Not checked",
        PostVerificationKind.FixedLead => "Fixed lead",
        PostVerificationKind.NestedCutOrder => "Nested part order",
        PostVerificationKind.NestedLead => "Nested part lead",
        PostVerificationKind.PierceZone => "Pierce zone",
        PostVerificationKind.PartSpacing => "Part spacing",
        PostVerificationKind.PartSupport => "Part support",
        _ => kind.ToString(),
    };

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

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
        if (!ShowMarker(finding with { Location = location }))
        {
            view.ZoomToFit();
            view.ShowValidationFinding(finding with { Location = location });
        }
        // The headline keeps completeness and the warning count; the grid keeps every finding.
        SetDetail(MarkerHint(finding));
    }

    /// <summary>
    /// Zooms to a finding's world-coordinate witness and marks it with the validation overlay.
    /// False, with nothing shown, when the finding has no usable location.
    /// </summary>
    private bool ShowMarker(PostVerificationFinding finding)
    {
        var location = finding.Location ?? finding.Overlap?.Marker;
        if (location is not { } point || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return false;
        var extent = finding.Overlap?.Bounds;
        var size = System.Math.Max(10, System.Math.Max(extent?.Length ?? 0, extent?.Width ?? 0) * 2);
        if (finding.RapidStart is { } start && finding.RapidEnd is { } end)
            size = System.Math.Max(size, start.DistanceTo(end) * 1.5);
        view.ZoomToArea(point.X - size / 2, point.Y - size / 2, size, size);
        // A narrow docked canvas can put a contact at its edge after zoom.
        // Center a typed witness, never infer one from the diagnostic text.
        var focus = finding.Kind == PostVerificationKind.RapidCrossing
            && finding.ContactPoints.Any(contact => double.IsFinite(contact.X) && double.IsFinite(contact.Y))
            ? finding.ContactPoints.First(contact => double.IsFinite(contact.X) && double.IsFinite(contact.Y))
            : point;
        view.CenterValidationPoint(focus, new Point(view.Width / 2, view.Height / 2));
        view.ShowValidationFinding(finding with { Location = point });
        return true;
    }

    private static string MarkerHint(PostVerificationFinding finding) => finding.Kind switch
    {
        PostVerificationKind.Overlap when finding.ClampId != null => "The ring marks where a cut-off touches a clamp.",
        PostVerificationKind.Overlap => "Red shading is the overlapping material.",
        PostVerificationKind.RapidCrossing when finding.ClampId != null =>
            "The crosshair marks where the rapid hits the clamp. Head height is not modeled.",
        PostVerificationKind.RapidCrossing => finding.ContactPoints.Count > 0
            ? "Orange dashes show the rapid; crosshairs mark where it crosses a cut contour."
            : "Orange dashes show the rapid running inside a cut contour.",
        PostVerificationKind.MissingLeadIn => "The ring marks the contour start that has no lead-in.",
        PostVerificationKind.NestedCutOrder => "The ring marks the nested part that is cut after its host.",
        PostVerificationKind.NestedLead => "The ring marks the middle of the lead that meets the other part.",
        PostVerificationKind.PierceZone => "The ring marks the pierce that is too close to part material.",
        PostVerificationKind.PartSpacing => "The ring marks where the two parts come closest.",
        PostVerificationKind.PartSupport => "The ring marks the part's center of gravity.",
        _ => "The ring marks the affected part; the exact spot is not known."
    };

    /// <summary>
    /// Starts planning with the current settings. The planning inputs stay enabled while planning
    /// runs, so changing one mid-plan cancels the run in progress and starts a fresh one once that
    /// cancellation lands.
    /// </summary>
    private void RestartPlanning()
    {
        if (Mode != CuttingInspectionMode.Plan || closed || closeRequested || pendingMode != null)
            return;
        if (!IsPlanning)
        {
            StartPlanning();
            return;
        }
        if (inspection.CloseRequested)
            return; // Already finishing; the checkbox's latest value is picked up when that lands.
        pendingReplan = true;
        ClearResult();
        SetPlanning(true);
        SetStatus(Tone.Neutral, "Restarting…");
        inspection.RequestClose();
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
        planningClock.Restart();
        SetPlanningStatus("Planning…");
        try
        {
            // Capture on the owner thread; no worker recapture of the live plate or settings.
            var batch = CuttingPlanBatch.Capture(plates, parameters, keepOrderCheckBox.Checked, plateNumbers,
                budgetMultiplier: CuttingPlanBatch.ClampBudgetMultiplier(Properties.Settings.Default.CuttingPlanEffort));
            var run = ++generation;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PlanningTask = completion.Task;
            var progress = new PostingProgress(context, value =>
            {
                if (!closed && !inspection.CloseRequested && inspection.IsRunning && generation == run)
                    SetPlanningStatus((plates.Length > 1 ? $"Planning plate {value.PlateNumber}: " : "Planning: ")
                        + PhaseText(value.Phase) + "…");
            });
            var work = inspection.BeginPlanning(batch, BeforePlan, progress);
            work.ContinueWith(task => context.Post(_ => FinishPlanning(task, run, completion), null),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            SetStatus(Tone.Bad, "Couldn't start planning.", ex.Message);
            SetPlanning(false);
        }
    }

    private static string PhaseText(CuttingPlanPhase phase) => phase switch
    {
        CuttingPlanPhase.Reordering => "finding a part order",
        CuttingPlanPhase.KeepingOrder => "keeping the current order",
        CuttingPlanPhase.SafestRoute => "finding the safest route",
        CuttingPlanPhase.CheckingOverlap => "checking for overlaps",
        _ => phase.ToString(),
    };

    private void FinishPlanning(Task<CuttingPlanProposal> work, int run, TaskCompletionSource completion)
    {
        try
        {
            inspection.CompletePlanning(work);
            if (closeRequested || pendingMode != null)
            {
                pendingReplan = pendingClear = pendingCutOffSettings = false;
                pendingScope = null;
                FinishTransition();
                return;
            }
            if (pendingReplan)
            {
                pendingReplan = false;
                if (closed) return;
                inspection = new CuttingProposalInspection();
                if (pendingClear)
                {
                    pendingClear = false; // The canceled run no longer reads the plates.
                    if (!ClearAppliedWork())
                    {
                        pendingScope = null;
                        SetPlanning(false);
                        return;
                    }
                }
                if (pendingScope is { } scoped)
                {
                    pendingScope = null; // The clear above, if any, covered the scope it was confirmed for.
                    if (!ApplyScope(scoped))
                    {
                        pendingCutOffSettings = false;
                        SetPlanning(false);
                        return;
                    }
                }
                if (pendingCutOffSettings)
                {
                    pendingCutOffSettings = false; // The canceled run no longer reads the plates.
                    SetPlanning(false);
                    EditCutOffSettings(); // Replans when the dialog closes.
                    return;
                }
                StartPlanning();
                return;
            }
            if (closed || run != generation)
                return;
            SetPlanning(false);
            if (work.IsFaulted)
            {
                SetStatus(Tone.Bad, "Planning failed. Replan to try again.", work.Exception.GetBaseException().Message);
                return;
            }
            if (work.IsCanceled)
            {
                SetStatus(Tone.Neutral, "Planning canceled. Nothing changed.");
                return;
            }
            invalidated = false;
            populatingWarnings = true;
            try
            {
                foreach (var row in work.Result.DiagnosticRows())
                {
                    var index = warningGrid.Rows.Add(row.PlateNumber, row.PartNumber, row.OtherPartNumber, row.Message);
                    warningGrid.Rows[index].Tag = row;
                }
                warningGrid.ClearSelection();
                PopulatePartsGrid();
            }
            finally { populatingWarnings = false; }
            ShowPlanStatus();
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
            previewLabel.Text = "No current preview.";
            SetStatus(Tone.Bad, "Couldn't show the plan. Nothing can be applied until you replan.", ex.Message);
            SetPlanning(false);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    /// <summary>One headline for the whole proposal; per-plate state goes on the plate line.</summary>
    private void ShowPlanStatus()
    {
        var issues = warningGrid.Rows.Count;
        if (Proposal.IsCanceled)
            SetStatus(Tone.Neutral, "Planning canceled. Nothing changed.");
        else if (Proposal.CanApply)
            SetStatus(Tone.Good, plates.Length > 1 ? $"Ready to apply to {Count(plates.Length, "plate")}." : "Ready to apply.");
        else if (Proposal.RequiresWarningAcceptance)
            SetStatus(Tone.Warning, $"Unverified plan: review {Count(issues, "issue")}, then accept it to apply.",
                issues > 0 ? SelectIssueHint : null);
        else
        {
            var blocked = Proposal.Plates.Count(p => !p.CanApplyWithWarnings);
            SetStatus(Tone.Bad, plates.Length > 1
                ? $"Can't apply: {blocked} of {Count(plates.Length, "plate")} blocked."
                : "Can't apply: the plate is blocked.", issues > 0 ? SelectIssueHint : null);
        }
    }

    /// <summary>What the operator needs to know about one planned plate, in one line.</summary>
    private string DescribePlate(int index)
    {
        var planned = Proposal.Plates[index];
        var name = plates.Length > 1 ? $"Plate {plateNumbers[index]} ({index + 1} of {plates.Length})" : $"Plate {plateNumbers[index]}";
        if (planned.IsReady)
        {
            var order = planned.KeptCurrentOrder ? "current order kept (no better order found)"
                : planned.OrderChanged ? "new part order" : "part order unchanged";
            var rapid = planned.Result.RapidDistance.ToString("0.#", CultureInfo.CurrentCulture);
            var raises = planned.Result.HeadRaises == 0 ? ""
                : $" {Count(planned.Result.HeadRaises, "rapid")} must raise the head over cut parts (listed below).";
            return $"{name}: {Count(planned.PartCount, "part")}, {order}, {rapid} {UnitsHelper.GetShortString(units)} of rapids."
                + raises;
        }
        if (planned.CanApplyWithWarnings)
            return $"{name}: UNVERIFIED. Check its lead-ins and cut order before applying.";
        return $"{name}: blocked — {BlockReason(planned)}.";
    }

    private static string BlockReason(CuttingPlanPlateResult plate)
    {
        if (plate.Overlap is { } overlap && overlap.Pairs.Count > 0)
            return "parts overlap";
        if (plate.IsRouteReady)
            return "the overlap check could not finish";
        if (plate.Result.Findings.Any(f => f.Kind == PostVerificationKind.MissingLeadIn))
            return "missing or zero-length lead-in";
        return plate.Result.Status switch
        {
            CuttingPlanStatus.Ready => "the plan could not be verified",
            CuttingPlanStatus.ConstraintConflict => "the required cut order conflicts",
            CuttingPlanStatus.UnsupportedGeometry => "unsupported geometry",
            CuttingPlanStatus.InvalidInput => "invalid input",
            CuttingPlanStatus.NoSolutionWithinBudget => "no plan found within the search limit",
            CuttingPlanStatus.Canceled => "canceled",
            var status => status.ToString(),
        };
    }

    /// <summary>The fix for a blocked plate's main problem, or null when there is no simple one.</summary>
    private static string Remedy(CuttingPlanPlateResult plate)
    {
        if (plate.CanApplyWithWarnings)
            return null;
        if (plate.Overlap is { } overlap && overlap.Pairs.Count > 0)
            return "Move the overlapping parts apart, then Replan.";
        if (!plate.IsRouteReady && plate.Result.Findings.Any(f => f.Kind == PostVerificationKind.MissingLeadIn))
            return "In Cutting Settings, choose a lead-in with a nonzero length (or unlock the part), then Replan.";
        if (plate.SearchLimitReached)
            return "Raise Cut planning effort in Tools > Options to search further, then Replan; it may still find no plan.";
        return null;
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
        SetStatus(Tone.Neutral, string.Empty);
        populatingWarnings = true;
        try { warningGrid.Rows.Clear(); partsGrid.Rows.Clear(); }
        finally { populatingWarnings = false; }
    }

    private void SetPlanning(bool running)
    {
        // Planning inputs change mid-plan by canceling the run and replanning, so they stay enabled
        // unless closing or switching; validation still locks them while it runs.
        var replannable = !running || Mode == CuttingInspectionMode.Plan && !closeRequested && pendingMode == null;
        settingsButton.Enabled = keepOrderCheckBox.Enabled = planButton.Enabled = replannable;
        cutOffSettingsButton.Enabled = clearExistingButton.Enabled = scopeSelector.Enabled = replannable;
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
        previewLabel.Text = "No current preview.";
        SetStatus(Tone.Bad, "The plate changed. Replan to continue; nothing was applied.");
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
            SetStatus(Tone.Bad, "The selected plate changed after planning. Replan to continue.");
            return;
        }
        if (!ReferenceEquals(view.Plate, plates[index]))
            editor.PlateManager.LoadAt(sourceIndex);
        if (!ReferenceEquals(view.Plate, plates[index]))
        {
            ClearResult();
            SetStatus(Tone.Bad, "The selected plate is no longer current. Replan to continue.");
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
        previewLabel.Text = DescribePlate(index);
        SetDetail(Remedy(Proposal.Plates[index]) ?? (warningGrid.Rows.Count > 0 ? SelectIssueHint : null));
        if (preview == null)
        {
            // Blocked or stale: the live plate stays on screen, numbered in its current order.
            if (Proposal.Plates[index].CanApplyWithWarnings)
                previewLabel.Text += " No current preview: the plate changed since planning.";
            return;
        }
        view.ShowProposalDisplay(preview, IsPreviewCurrent);
        view.ZoomToFit();
    }

    /// <summary>A source part number as numbered on the canvas: proposed order on a preview, else unchanged.</summary>
    private int? ShownPartNumber(int plateIndex, int? sourceNumber) =>
        preview == null || sourceNumber is not int source ? sourceNumber
            : CuttingProposalInspection.ProposedPosition(Proposal, plateIndex, source) + 1;

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
        // Witnesses are world coordinates, so they mark the proposal and a blocked plate's live view alike.
        // Their part numbers drive which parts stay highlighted: use the shown plate's numbering.
        if (row.Witness is { } witness && ShowMarker(witness with
        {
            PartNumber = ShownPartNumber(row.PlateIndex, row.PartNumber),
            OtherPartNumber = ShownPartNumber(row.PlateIndex, row.OtherPartNumber),
        }))
        {
            SetDetail(MarkerHint(witness));
            return;
        }
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
        SetDetail("Zoomed to the affected part; this issue has no exact location.");
    }

    /// <summary>
    /// Every part in scope, so a part with nothing wrong still shows as Passed instead of
    /// simply not appearing. Validate always covers the whole nest (matching the Issues tab);
    /// Plan covers only the plates this instance planned.
    /// </summary>
    private void PopulatePartsGrid()
    {
        partsGrid.Rows.Clear();
        if (Mode == CuttingInspectionMode.Validate)
        {
            var all = editor.Nest.Plates;
            for (var i = 0; i < all.Count; i++)
                AddPartRows(i + 1, all[i].Parts.Count);
        }
        else
            for (var i = 0; i < plates.Length; i++)
                AddPartRows(plateNumbers[i], plates[i].Parts.Count);
    }

    private void AddPartRows(int plateNumber, int partCount)
    {
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var partNumber = partIndex + 1;
            var tone = Tone.Good;
            var matches = 0;
            foreach (DataGridViewRow row in warningGrid.Rows)
            {
                if (!Refers(row.Tag, plateNumber, partNumber)) continue;
                matches++;
                if (KindOf(row.Tag) == PostVerificationKind.Incomplete) tone = Tone.Bad;
                else if (tone != Tone.Bad) tone = Tone.Warning;
            }
            var status = tone switch
            {
                Tone.Bad => "Not checked",
                Tone.Warning => Count(matches, "issue"),
                _ => "Passed",
            };
            var addedRow = partsGrid.Rows[partsGrid.Rows.Add(plateNumber, partNumber, status)];
            addedRow.Tag = (plateNumber, partNumber);
            addedRow.DefaultCellStyle.ForeColor = StatusInk(tone);
        }
    }

    private static Color StatusInk(Tone tone) => tone switch
    {
        Tone.Good => Color.FromArgb(28, 88, 28),
        Tone.Warning => Color.FromArgb(118, 78, 0),
        Tone.Bad => Color.FromArgb(132, 20, 20),
        _ => SystemColors.ControlText,
    };

    /// <summary>Whether a warning/issue row's tag names this plate and part, either as the subject or the other part.</summary>
    private static bool Refers(object tag, int plateNumber, int partNumber) => tag switch
    {
        PostVerificationFinding f => f.PlateNumber == plateNumber && (f.PartNumber == partNumber || f.OtherPartNumber == partNumber),
        CuttingPlanDiagnosticRow r => r.PlateNumber == plateNumber && (r.PartNumber == partNumber || r.OtherPartNumber == partNumber),
        _ => false,
    };

    private static PostVerificationKind? KindOf(object tag) => tag switch
    {
        PostVerificationFinding f => f.Kind,
        CuttingPlanDiagnosticRow { Witness: { } witness } => witness.Kind,
        _ => null,
    };

    /// <summary>Selecting a part jumps to its first issue (reusing that marker) or just zooms to it when clear.</summary>
    private void NavigatePart()
    {
        if (closed || closeRequested || pendingMode != null || populatingWarnings || IsPlanning || editor.IsDisposed
            || partsGrid.SelectedRows.Count == 0 || partsGrid.SelectedRows[0].Tag is not ValueTuple<int, int> target)
            return;
        var (plateNumber, partNumber) = target;
        foreach (DataGridViewRow row in warningGrid.Rows)
            if (Refers(row.Tag, plateNumber, partNumber))
            {
                warningGrid.ClearSelection();
                row.Selected = true;
                warningGrid.CurrentCell = row.Cells[0];
                return; // warningGrid.SelectionChanged reuses NavigateFinding/NavigateValidationFinding.
            }
        view.ClearValidationFinding();
        if (Mode == CuttingInspectionMode.Validate)
        {
            if (plateNumber < 1 || plateNumber > editor.Nest.Plates.Count) return;
            if (editor.PlateManager.CurrentIndex != plateNumber - 1)
                editor.PlateManager.LoadAt(plateNumber - 1);
            if (partNumber > 0 && partNumber <= view.Plate.Parts.Count
                && view.Plate.Parts[partNumber - 1]?.BoundingBox is { } box
                && double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.Length) && double.IsFinite(box.Width))
            {
                var pad = System.Math.Max(0.5, System.Math.Max(box.Length, box.Width) * 0.12);
                view.ZoomToArea(box.X - pad, box.Y - pad, box.Length + 2 * pad, box.Width + 2 * pad);
            }
            else
                view.ZoomToFit();
            SetDetail("No issue found for this part.");
        }
        else
        {
            var index = Array.IndexOf(plateNumbers, plateNumber);
            if (index >= 0) ShowPlate(index);
        }
    }

    internal CuttingCommitResult CommitResult { get; private set; }

    private void Apply()
    {
        if (!applyButton.Enabled || IsPlanning || invalidated || Proposal == null)
            return;
        // This handler runs on the owner thread; Apply independently rechecks all captured plates.
        BeginPlanChange();
        CommitResult = Proposal.Apply(acceptWarningsCheckBox.Checked);
        if (CommitResult.Status == CuttingCommitStatus.Applied)
        {
            // The plan was made with these cut-offs in place; applying it keeps them.
            stagedCutOffs?.Keep();
            stagedCutOffs = null;
            planChange.MarkChanged();
            EndPlanChange();
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
        SetStatus(Tone.Bad, "Nothing applied. Replan to continue.", CommitResult.Message ?? CommitResult.Status.ToString());
    }

    private void EditSettings()
    {
        // A running plan keeps going behind the dialog; OK cancels it and replans with the new settings.
        if (closed || closeRequested || pendingMode != null || Mode != CuttingInspectionMode.Plan) return;
        using var dialog = new CuttingParametersDialog();
        dialog.LoadParameters(parameters);
        if (dialog.ShowDialog(this) != DialogResult.OK || closed || closeRequested) return;
        parameters = dialog.GetParameters();
        // Saved now, not only on Apply: a plate that will not plan must not lose the edit.
        EditNestForm.SaveCuttingParameters(parameters);
        ShowSettings();
        RestartPlanning();
    }

    private void ShowSettings()
    {
        settingsLabel.Text = $"Outside: {Describe(parameters.ExternalLeadIn)} · Holes: {Describe(parameters.InternalLeadIn)} · "
            + $"Tabs: {(parameters.TabsEnabled ? "on" : "off")}";
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
            BeginPlanChange();
            var staging = AutomaticCutOffBatch.Stage(plates, cutOffOptions, view.CutOffSettings);
            if (staging.IsBlocked)
            {
                var reasons = staging.Plans.SelectMany((plan, index) => plan.Diagnostics.Where(d => d.IsBlocking)
                    .Select(d => $"Plate {plateNumbers[index]}: {d.Message}"));
                SetStatus(Tone.Bad, "Scrap cut-offs couldn't be added, so nothing was planned.",
                    string.Join(Environment.NewLine, reasons) + Environment.NewLine
                    + "Untick scrap cut-offs to plan without them, or clear the existing cut-offs.");
                return false;
            }
            try
            {
                // Appended last, a crossing cut-off breaks any kept order; move it ahead of what it crosses.
                for (var index = 0; index < plates.Length; index++)
                    PlateSequencing.PlaceCutOffs(plates[index], staging.Plans[index].Definitions);
            }
            catch
            {
                staging.Revert();
                throw;
            }
            stagedCutOffs = staging.CanRevert ? staging : null;
            view.Invalidate();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Tone.Bad, "Scrap cut-offs couldn't be added, so nothing was planned.", ex.Message);
            return false;
        }
    }

    private void RevertStagedCutOffs()
    {
        var staged = stagedCutOffs;
        stagedCutOffs = null;
        if (staged != null)
        {
            staged.Revert();
            if (!view.IsDisposed)
                view.Invalidate();
        }
        EndPlanChange();
    }

    private void BeginPlanChange() => planChange ??= editor.BeginChange("Plan Cutting");

    // Records the plan once it was marked applied; otherwise the plates are as they were.
    private void EndPlanChange()
    {
        var change = planChange;
        planChange = null;
        change?.Complete();
        change?.Dispose();
    }

    private void RefreshExistingWork()
    {
        var summary = AppliedCuttingWork.Summarize(plates, plateNumbers, stagedCutOffs?.Definitions);
        existingWorkLabel.Text = summary.Describe();
        existingWorkBanner.Visible = Mode == CuttingInspectionMode.Plan && summary.HasExistingWork;
    }

    /// <summary>
    /// Asks, then clears the applied lead-ins and cut-offs and replans. While a plan is running the
    /// clear waits for that run's cancellation to land, so the live plates never change under it.
    /// </summary>
    private void ClearExistingWork()
    {
        if (closed || closeRequested || pendingMode != null || pendingClear || Mode != CuttingInspectionMode.Plan)
            return;
        var summary = AppliedCuttingWork.Summarize(plates, plateNumbers, stagedCutOffs?.Definitions);
        if (!summary.HasExistingWork)
            return;
        var message = $"Remove the applied lead-ins ({summary.LeadInParts} parts, {summary.LockedLeadInParts} locked) and "
            + $"{summary.CutOffs} cut-offs from plate(s) {string.Join(", ", summary.PlateNumbers)}?"
            + Environment.NewLine + Environment.NewLine
            + "Locked and hand-placed lead-ins are removed too. The plan is then recalculated.";
        var confirmed = ConfirmClearExisting?.Invoke(message)
            ?? MessageBox.Show(editor, message, "Clear Lead-ins and Cut-offs", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (!confirmed || closed || closeRequested || pendingMode != null || pendingClear)
            return;
        if (IsPlanning)
        {
            pendingClear = true;
            pendingReplan = true;
            ClearResult();
            SetPlanning(true);
            SetStatus(Tone.Neutral, "Canceling the plan to clear lead-ins and cut-offs…");
            if (!inspection.CloseRequested)
                inspection.RequestClose();
            return;
        }
        if (ClearAppliedWork())
            StartPlanning();
    }

    /// <summary>Removes the applied lead-ins and cut-offs from the plates in scope; false when it stopped part-way.</summary>
    private bool ClearAppliedWork()
    {
        ClearResult();
        ChangeScope change = null;
        try
        {
            RevertStagedCutOffs();
            // Recorded even when it stops part-way: the plates keep what was cleared.
            change = editor.BeginChange("Clear Lead-ins and Cut-offs");
            change.MarkChanged();
            AppliedCuttingWork.Clear(plates, plateNumbers);
        }
        catch (Exception ex)
        {
            RefreshExistingWork();
            SetStatus(Tone.Bad, "Clearing stopped part-way. Review the plates before planning or posting.", ex.Message);
            return false;
        }
        finally
        {
            change?.Complete();
            change?.Dispose();
            foreach (var part in view.Parts)
            {
                part.IsDirty = true;
                part.Update();
            }
            view.Invalidate();
            editor.UpdatePlateList();
        }
        return true;
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
        SetStatus(Tone.Neutral, "Closing… the preview was not applied.");
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
            try { EndPlanChange(); }
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
        if (disposing)
        {
            elapsedTimer.Dispose();
            toolTip.Dispose();
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

    /// <summary>
    /// One alert style for the panel: a pale tint with dark text and border in the same hue.
    /// </summary>
    private sealed class StatusBanner : FlowLayoutPanel
    {
        private Color ink = SystemColors.ControlText;

        public StatusBanner()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            Padding = new Padding(12, 10, 12, 10);
            Visible = false;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void SetTone(Tone tone)
        {
            (BackColor, ink) = tone switch
            {
                Tone.Good => (Color.FromArgb(221, 240, 214), Color.FromArgb(28, 88, 28)),
                Tone.Warning => (Color.FromArgb(252, 241, 200), Color.FromArgb(118, 78, 0)),
                Tone.Bad => (Color.FromArgb(248, 215, 215), Color.FromArgb(132, 20, 20)),
                _ => (Color.FromArgb(236, 236, 236), Color.FromArgb(60, 60, 60)),
            };
            foreach (var label in Controls.OfType<Label>())
                label.ForeColor = ink;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            const int border = 2;
            const ButtonBorderStyle style = ButtonBorderStyle.Solid;
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, ink, border, style, ink, border, style,
                ink, border, style, ink, border, style);
        }
    }

    private sealed class PostingProgress(SynchronizationContext context, Action<CuttingPlanProgress> handler)
        : IProgress<CuttingPlanProgress>
    {
        public void Report(CuttingPlanProgress value) => context.Post(_ => handler(value), null);
    }
}

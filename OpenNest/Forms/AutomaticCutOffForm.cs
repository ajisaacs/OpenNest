using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Forms;

public partial class AutomaticCutOffForm : Form
{
    private static readonly Color ReadyColor = Color.FromArgb(0, 102, 0);
    private static readonly Color BlockedColor = Color.FromArgb(192, 0, 0);
    private static readonly Color NeutralColor = SystemColors.ControlText;
    internal static readonly Color WarningColor = Color.FromArgb(120, 70, 0);

    private readonly PlateView plateView;
    private readonly Plate plate;
    private readonly Nest nest;
    private readonly Func<bool> isOperationBusy;
    private readonly Units units;
    private readonly string unit;
    private bool minimumStripEdited;
    private bool settingMinimumStrip;
    private bool settingsOnly;

    /// <summary>In settings mode, the options chosen with OK; null otherwise or when cancelled.</summary>
    internal AutomaticCutOffOptions SelectedOptions { get; private set; }

    /// <summary>
    /// Opens the same dialog to choose settings for another workflow (Plan Cutting): it previews
    /// on the active plate exactly as usual, but OK only returns the options and adds nothing.
    /// </summary>
    internal static AutomaticCutOffForm ForSettings(PlateView plateView, Units units, Nest nest,
        AutomaticCutOffOptions initial)
    {
        var form = new AutomaticCutOffForm(plateView, units, nest, null) { settingsOnly = true };
        form.Text = "Automatic Scrap Cut-offs — Settings for Plan Cutting";
        form.applyButton.Text = "&Use These Settings";
        if (initial != null)
        {
            form.spacingBox.Text = initial.Spacing.ToString(CultureInfo.CurrentCulture);
            form.minimumStripBox.Text = (initial.MinimumStripLength ?? initial.Spacing).ToString(CultureInfo.CurrentCulture);
            form.minimumTailBox.Text = initial.MinimumTailLength.ToString(CultureInfo.CurrentCulture);
            form.minimumStripEdited = true; // Keep the given minimum when the spacing is edited.
        }
        return form;
    }

    public AutomaticCutOffForm(PlateView plateView, Units units, Func<bool> isOperationBusy = null)
        : this(plateView, units, null, isOperationBusy) { }

    public AutomaticCutOffForm(PlateView plateView, Nest nest, Func<bool> isOperationBusy = null)
        : this(plateView, (nest ?? throw new ArgumentNullException(nameof(nest))).Units, nest, isOperationBusy) { }

    private AutomaticCutOffForm(PlateView plateView, Units units, Nest nest, Func<bool> isOperationBusy)
    {
        this.plateView = plateView ?? throw new ArgumentNullException(nameof(plateView));
        plate = plateView.Plate ?? throw new ArgumentException("An active plate is required.", nameof(plateView));
        this.nest = nest;
        this.isOperationBusy = isOperationBusy;
        this.units = units;
        unit = UnitsHelper.GetShortString(units);
        InitializeComponent();
        spacingLabel.Text = $"Nominal spacing along sheet length ({unit}):";
        spacingBox.Text = units == Units.Millimeters ? "889" : "35";
        minimumStripLabel.Text = $"Minimum strip ({unit}):";
        minimumTailLabel.Text = $"Minimum tail to keep ({unit}):";
        minimumTailBox.Text = (units == Units.Millimeters ? 304.8 : 12).ToString(CultureInfo.CurrentCulture);
        headLockBox.Text = $"Lock head height and overcut the far edge by ({unit}):";
        headLockBox.Checked = plateView.CutOffSettings.LockHeadHeight;
        headLockOvercutBox.Text = plateView.CutOffSettings.HeadLockOvercut.ToString(CultureInfo.CurrentCulture);
        headLockOvercutBox.Enabled = headLockBox.Checked;
        sheetLabel.Text = $"Sheet: {Measure(plate.Size.Length)} length × {Measure(plate.Size.Width)} width";
        if (nest != null)
        {
            Text = "Automatic Scrap Cutoffs — All Plates";
            applyButton.Text = "&Apply to All Plates";
            sheetLabel.Text = $"All {nest.Plates.Count} plates — active sheet shown below";
            diagnosticsLabel.Text = "Details — issues and notices (the table above lists every plate):";
        }
        else
        {
            plateGrid.Visible = false;
            plateGridRow.Height = 0F;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowPreview();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ClearPreview();
        base.OnFormClosed(e);
    }

    private void ClearPreview()
    {
        if (plateView != null && !plateView.IsDisposed)
            plateView.ClearPreviewParts();
    }

    private string Measure(double value) => $"{value:0.####} {unit}";

    private string SignedPosition(double value) => $"{value:+0.####;-0.####;0} {unit}";

    private (Plate[] Plates, IReadOnlyList<AutomaticCutOffPlan> Plans) Calculate(bool apply)
    {
        if (plateView.IsFillInProgress || isOperationBusy?.Invoke() == true)
            throw new InvalidOperationException("Wait for the current nesting or plate action to finish before creating cut-offs.");

        if (plateView.IsDisposed || !ReferenceEquals(plateView.Plate, plate))
            throw new InvalidOperationException("The active plate changed. Close this dialog and try again.");

        var plates = nest?.Plates.ToArray() ?? new[] { plate };
        if (!plates.Contains(plate))
            throw new InvalidOperationException("The active plate is no longer in this nest. Close this dialog and try again.");

        var options = ParseOptions();
        var settings = WithHeadLock(plateView.CutOffSettings);
        var plans = apply
            ? AutomaticCutOffBatch.Apply(plates, options, settings)
            : AutomaticCutOffBatch.Create(plates, options, settings);
        return (plates, plans);
    }

    private AutomaticCutOffOptions ParseOptions()
    {
        if (!double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var spacing))
            throw new ArgumentException($"Enter a numeric spacing in {unit}.");
        if (!double.TryParse(minimumStripBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minimumStrip))
            throw new ArgumentException($"Enter a numeric minimum strip in {unit}.");
        if (!double.TryParse(minimumTailBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minimumTail))
            throw new ArgumentException($"Enter a numeric minimum tail length in {unit}.");

        return new AutomaticCutOffOptions
        {
            Spacing = spacing,
            MinimumStripLength = minimumStrip,
            MinimumTailLength = minimumTail,
        };
    }

    // The view's settings with this dialog's head-lock choice; the view keeps its own until OK.
    private CutOffSettings WithHeadLock(CutOffSettings current)
    {
        var overcut = current.HeadLockOvercut;
        if (headLockBox.Checked && (!double.TryParse(headLockOvercutBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture,
                out overcut) || !double.IsFinite(overcut) || overcut <= 0))
            throw new ArgumentException($"Enter a positive head-locked overcut in {unit}.");
        return new CutOffSettings
        {
            PartClearance = current.PartClearance,
            Overtravel = current.Overtravel,
            MinSegmentLength = current.MinSegmentLength,
            CutDirection = current.CutDirection,
            LockHeadHeight = headLockBox.Checked,
            HeadLockOvercut = overcut,
        };
    }

    // New cut-offs from here on, including Plan Cutting's and the manual tool's, use the choice.
    private void KeepHeadLock()
    {
        var chosen = WithHeadLock(plateView.CutOffSettings);
        plateView.CutOffSettings.LockHeadHeight = chosen.LockHeadHeight;
        plateView.CutOffSettings.HeadLockOvercut = chosen.HeadLockOvercut;
        CutOffHeadLockDefaults.Save(chosen.LockHeadHeight, chosen.HeadLockOvercut, units);
    }

    private void HeadLockBox_CheckedChanged(object sender, EventArgs e)
    {
        headLockOvercutBox.Enabled = headLockBox.Checked;
        SettingsBox_TextChanged(sender, e);
    }

    private string DiagnosticLine(AutomaticCutOffPlan plan) => string.Join(Environment.NewLine,
        plan.Diagnostics.Select(d =>
            $"{(d.IsBlocking ? "BLOCKING" : "Notice")} [{d.Code}]"
            + (d.X.HasValue ? $" X = {SignedPosition(d.X.Value)}" : string.Empty)
            + $": {d.Message}"));

    private void SetStatus(string message, Color color)
    {
        statusLabel.Text = message;
        statusLabel.ForeColor = color;
    }

    private void DisplayPlans(Plate[] plates, IReadOnlyList<AutomaticCutOffPlan> plans)
    {
        var currentIndex = Array.IndexOf(plates, plate);
        var plan = plans[currentIndex];
        occupiedLabel.Text = Measure(plan.OccupiedSpan);
        usedLabel.Text = Measure(plan.UsedSpan);
        separatorLabel.Text = plan.HasSeparatedTail ? SignedPosition(plan.TailSeparatorX.Value) : "none";
        tailLabel.Text = plan.HasSeparatedTail ? Measure(plan.TailLength) : "none";
        var saving = plan.FixedGridCutLength - plan.StripCutLength;
        stripCutsLabel.Text = $"Strip cuts: {Measure(plan.StripCutLength)} ("
            + (saving > 0 ? $"{Measure(saving)} less than the fixed grid" :
                saving < 0 ? $"{Measure(-saving)} more than the fixed grid" : "same as the fixed grid")
            + ")";

        var blocked = plans.Any(p => p.HasBlockingDiagnostics);
        var definitionCount = plans.Sum(p => p.Definitions.Count);
        // Settings may be chosen even when this layout needs no new lines or is blocked:
        // Plan Cutting reports the blocked plate itself.
        applyButton.Enabled = settingsOnly || !blocked && definitionCount > 0;

        var issues = new List<string>();
        for (var index = 0; index < plans.Count; index++)
        {
            var lines = DiagnosticLine(plans[index]);
            if (lines.Length == 0)
                continue;
            issues.Add(nest == null ? lines : $"Plate {index + 1}: {lines}");
        }
        diagnosticsBox.Text = issues.Count == 0 ? "No issues detected." : string.Join(Environment.NewLine, issues);

        if (blocked)
        {
            var issueCount = plans.Sum(p => p.Diagnostics.Count(d => d.IsBlocking));
            SetStatus($"Blocked — {issueCount} blocking issue{(issueCount == 1 ? "" : "s")}; nothing will be applied.", BlockedColor);
        }
        else if (definitionCount == 0)
        {
            SetStatus("No new cut-offs to apply.", NeutralColor);
        }
        else if (nest == null)
        {
            SetStatus($"Ready — {definitionCount} new cut-off{(definitionCount == 1 ? "" : "s")} will be added.", ReadyColor);
        }
        else
        {
            var touched = plans.Count(p => p.Definitions.Count > 0);
            SetStatus($"Ready — {definitionCount} cut-off{(definitionCount == 1 ? "" : "s")} across {touched} of {plans.Count} plates. Unchanged plates stay untouched.", ReadyColor);
        }

        if (nest == null)
            return;

        sheetLabel.Text = $"All {plates.Length} plates — preview shows plate {currentIndex + 1} "
            + $"({Measure(plate.Size.Length)} length × {Measure(plate.Size.Width)} width)";
        FillPlateGrid(plans, currentIndex);
    }

    private void FillPlateGrid(IReadOnlyList<AutomaticCutOffPlan> plans, int activeIndex)
    {
        plateGrid.BeginUpdate();
        plateGrid.Items.Clear();
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var label = (index + 1).ToString(CultureInfo.CurrentCulture);
            if (index == activeIndex)
                label += " (active)";
            var row = new ListViewItem(label);
            row.SubItems.Add(plan.Definitions.Count == 0 ? "—" : plan.Definitions.Count.ToString(CultureInfo.CurrentCulture));
            row.SubItems.Add(Measure(plan.OccupiedSpan));
            row.SubItems.Add(Measure(plan.UsedSpan));
            row.SubItems.Add(plan.HasSeparatedTail ? Measure(plan.TailLength) : "—");
            row.SubItems.Add(plan.HasBlockingDiagnostics ? "Blocked" : plan.Definitions.Count > 0 ? "Ready" : "No change");
            if (plan.HasBlockingDiagnostics)
                row.ForeColor = BlockedColor;
            else if (plan.Definitions.Count > 0)
                row.ForeColor = ReadyColor;
            else
                row.ForeColor = SystemColors.GrayText;
            plateGrid.Items.Add(row);
        }
        plateGrid.EndUpdate();
    }

    private void ResetSummary()
    {
        occupiedLabel.Text = "—";
        usedLabel.Text = "—";
        separatorLabel.Text = "—";
        tailLabel.Text = "—";
        stripCutsLabel.Text = "—";
        plateGrid.Items.Clear();
    }

    private void ShowError(Exception error)
    {
        ClearPreview();
        ResetSummary();
        applyButton.Enabled = false;
        SetStatus($"Error — {error.Message}", BlockedColor);
        diagnosticsBox.Text = error.Message;
    }

    private void SpacingBox_TextChanged(object sender, EventArgs e)
    {
        if (!minimumStripEdited &&
            double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var spacing) &&
            double.IsFinite(spacing))
        {
            settingMinimumStrip = true;
            try
            {
                minimumStripBox.Text = (spacing / 2).ToString(CultureInfo.CurrentCulture);
            }
            finally
            {
                settingMinimumStrip = false;
            }
        }
        SettingsBox_TextChanged(sender, e);
    }

    private void MinimumStripBox_TextChanged(object sender, EventArgs e)
    {
        if (!settingMinimumStrip)
            minimumStripEdited = true;
        SettingsBox_TextChanged(sender, e);
    }

    private void SettingsBox_TextChanged(object sender, EventArgs e)
    {
        ClearPreview();
        ResetSummary();
        SetStatus("Settings changed — press Preview.", NeutralColor);
        diagnosticsBox.Text = string.Empty;
        applyButton.Enabled = true;
    }

    private void PreviewButton_Click(object sender, EventArgs e) => ShowPreview();

    private void ShowPreview()
    {
        ClearPreview();
        try
        {
            var (plates, plans) = Calculate(apply: false);
            DisplayPlans(plates, plans);
            if (!plans.Any(p => p.HasBlockingDiagnostics))
                plateView.SetActiveParts(plans[Array.IndexOf(plates, plate)].PreviewParts.ToList());
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyButton_Click(object sender, EventArgs e)
    {
        ClearPreview();
        if (settingsOnly)
        {
            try
            {
                // Validate exactly as planning will, without changing any plate.
                Calculate(apply: false);
                SelectedOptions = ParseOptions();
                KeepHeadLock();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                ClearPreview();
            }
            return;
        }
        try
        {
            // The batch service replans every plate before mutating any; never accept previews.
            var (plates, plans) = Calculate(apply: true);
            DisplayPlans(plates, plans);
            if (plans.Any(p => p.HasBlockingDiagnostics) || plans.All(p => p.Definitions.Count == 0))
                return;

            KeepHeadLock();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            ClearPreview();
        }
    }
}

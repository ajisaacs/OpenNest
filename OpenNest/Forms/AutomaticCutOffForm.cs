using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Forms;

public partial class AutomaticCutOffForm : Form
{
    private readonly PlateView plateView;
    private readonly Plate plate;
    private readonly string unit;

    public AutomaticCutOffForm(PlateView plateView, Units units)
    {
        this.plateView = plateView ?? throw new ArgumentNullException(nameof(plateView));
        plate = plateView.Plate ?? throw new ArgumentException("An active plate is required.", nameof(plateView));
        unit = UnitsHelper.GetShortString(units);
        InitializeComponent();
        spacingLabel.Text = $"Nominal spacing along sheet length ({unit}):";
        spacingBox.Text = units == Units.Millimeters ? "889" : "35";
        sheetLabel.Text = $"Sheet: {Measure(plate.Size.Length)} length × {Measure(plate.Size.Width)} width";
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

    private AutomaticCutOffPlan CreatePlan(CutOffSettings settings)
    {
        if (plateView.IsFillInProgress)
            throw new InvalidOperationException("Wait for the current fill to finish before creating cut-offs.");

        if (plateView.IsDisposed || !ReferenceEquals(plateView.Plate, plate))
            throw new InvalidOperationException("The active plate changed. Close this dialog and try again.");

        if (!double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var spacing))
            throw new ArgumentException($"Enter a numeric spacing in {unit}.");

        return AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = spacing }, settings);
    }

    private void DisplayPlan(AutomaticCutOffPlan plan)
    {
        occupiedLabel.Text = $"Occupied span from origin: {Measure(plan.OccupiedSpan)}";
        usedLabel.Text = $"Proposed used span from origin: {Measure(plan.UsedSpan)}";
        separatorLabel.Text = plan.HasSeparatedTail
            ? $"Verified separator program at signed X: {SignedPosition(plan.TailSeparatorX.Value)}"
            : "Verified separator program: none";
        tailLabel.Text = plan.HasSeparatedTail
            ? $"Proposed retained tail: {Measure(plan.TailLength)} length × {Measure(plate.Size.Width)} width"
            : "Proposed retained tail: none verified";

        var status = plan.HasBlockingDiagnostics
            ? "Blocked: resolve the diagnostics before applying."
            : plan.Definitions.Count == 0
                ? "No new usable cut-offs to apply."
                : $"{plan.Definitions.Count} new cut-off definition(s) ready to apply.";
        var diagnostics = plan.Diagnostics.Select(d =>
            $"{(d.IsBlocking ? "BLOCKING" : "Notice")} [{d.Code}]"
            + (d.X.HasValue ? $" X = {SignedPosition(d.X.Value)}" : string.Empty)
            + $": {d.Message}");
        diagnosticsBox.Text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { status }.Concat(diagnostics));
        applyButton.Enabled = !plan.HasBlockingDiagnostics && plan.Definitions.Count > 0;
    }

    private void ResetSummary()
    {
        occupiedLabel.Text = "Occupied span from origin: —";
        usedLabel.Text = "Proposed used span from origin: —";
        separatorLabel.Text = "Verified separator program: —";
        tailLabel.Text = "Proposed retained tail: —";
    }

    private void ShowError(Exception error)
    {
        ClearPreview();
        ResetSummary();
        applyButton.Enabled = false;
        diagnosticsBox.Text = $"Unable to complete the cut-off operation: {error.Message}";
    }

    private void SpacingBox_TextChanged(object sender, EventArgs e)
    {
        ClearPreview();
        ResetSummary();
        diagnosticsBox.Text = "Spacing changed. Preview or Apply will calculate a fresh plan.";
        applyButton.Enabled = true;
    }

    private void PreviewButton_Click(object sender, EventArgs e) => ShowPreview();

    private void ShowPreview()
    {
        ClearPreview();
        try
        {
            var plan = CreatePlan(plateView.CutOffSettings);
            DisplayPlan(plan);
            if (!plan.HasBlockingDiagnostics)
                plateView.SetActiveParts(plan.PreviewParts.ToList());
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyButton_Click(object sender, EventArgs e)
    {
        ClearPreview();
        try
        {
            // Never accept cached preview parts: recompute with the current model/settings.
            var settings = plateView.CutOffSettings;
            var plan = CreatePlan(settings);
            DisplayPlan(plan);
            if (plan.HasBlockingDiagnostics || plan.Definitions.Count == 0)
                return;

            ApplyDefinitions(plan, settings);
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

    private void ApplyDefinitions(AutomaticCutOffPlan plan, CutOffSettings settings)
    {
        // Regeneration replaces drawing programs and removes/reinserts cut-off parts.
        // Keep only the state it can change; leave real parts and quantities untouched.
        var programs = plate.CutOffs.Select(c => (CutOff: c, Program: c.Drawing.Program)).ToList();
        var parts = plate.Parts.Select((part, index) => (Part: part, Index: index))
            .Where(p => p.Part.BaseDrawing.IsCutOff).ToList();
        try
        {
            plate.CutOffs.AddRange(plan.Definitions);
            plate.RegenerateCutOffs(settings);
        }
        catch (Exception applyError)
        {
            try
            {
                foreach (var definition in plan.Definitions)
                    plate.CutOffs.Remove(definition);
                for (var index = plate.Parts.Count - 1; index >= 0; index--)
                {
                    if (plate.Parts[index].BaseDrawing.IsCutOff)
                        plate.Parts.RemoveAt(index);
                }
                foreach (var saved in programs)
                    saved.CutOff.Drawing.Program = saved.Program;
                foreach (var saved in parts)
                    plate.Parts.Insert(saved.Index, saved.Part);
            }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException(
                    $"Apply failed: {applyError.Message}\nRestoring cut-offs also failed: {rollbackError.Message}\n"
                    + "The plate may be incomplete; review it before saving or cutting.", rollbackError);
            }

            throw new InvalidOperationException(
                $"No new cut-offs were retained; original cut-offs were restored. {applyError.Message}", applyError);
        }
    }
}

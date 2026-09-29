using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Forms;

public partial class AutomaticCutOffForm : Form
{
    private readonly PlateView plateView;
    private readonly Plate plate;
    private readonly Nest nest;
    private readonly Func<bool> isOperationBusy;
    private readonly string unit;

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
        unit = UnitsHelper.GetShortString(units);
        InitializeComponent();
        spacingLabel.Text = $"Nominal spacing along sheet length ({unit}):";
        spacingBox.Text = units == Units.Millimeters ? "889" : "35";
        minimumTailLabel.Text = $"Minimum tail to keep ({unit}):";
        minimumTailBox.Text = (units == Units.Millimeters ? 304.8 : 12).ToString(CultureInfo.CurrentCulture);
        sheetLabel.Text = $"Sheet: {Measure(plate.Size.Length)} length × {Measure(plate.Size.Width)} width";
        if (nest != null)
        {
            Text = "Automatic Scrap Cutoffs — All Plates";
            applyButton.Text = "&Apply to All Plates";
            sheetLabel.Text = $"All {nest.Plates.Count} plates — active sheet shown below";
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

        if (!double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var spacing))
            throw new ArgumentException($"Enter a numeric spacing in {unit}.");
        if (!double.TryParse(minimumTailBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minimumTail))
            throw new ArgumentException($"Enter a numeric minimum tail length in {unit}.");

        var options = new AutomaticCutOffOptions { Spacing = spacing, MinimumTailLength = minimumTail };
        var settings = plateView.CutOffSettings;
        var plans = apply
            ? AutomaticCutOffBatch.Apply(plates, options, settings)
            : AutomaticCutOffBatch.Create(plates, options, settings);
        return (plates, plans);
    }

    private IEnumerable<string> Diagnostics(AutomaticCutOffPlan plan) => plan.Diagnostics.Select(d =>
        $"{(d.IsBlocking ? "BLOCKING" : "Notice")} [{d.Code}]"
        + (d.X.HasValue ? $" X = {SignedPosition(d.X.Value)}" : string.Empty)
        + $": {d.Message}");

    private static string PlanStatus(AutomaticCutOffPlan plan) => plan.HasBlockingDiagnostics
        ? "Blocked: resolve the diagnostics before applying."
        : plan.Definitions.Count == 0
            ? "No new usable cut-offs to apply."
            : $"{plan.Definitions.Count} new cut-off definition(s) ready to apply.";

    private void DisplayPlans(Plate[] plates, IReadOnlyList<AutomaticCutOffPlan> plans)
    {
        var currentIndex = Array.IndexOf(plates, plate);
        var plan = plans[currentIndex];
        occupiedLabel.Text = $"Occupied span from origin: {Measure(plan.OccupiedSpan)}";
        usedLabel.Text = $"Proposed used span from origin: {Measure(plan.UsedSpan)}";
        separatorLabel.Text = plan.HasSeparatedTail
            ? $"Verified separator program at signed X: {SignedPosition(plan.TailSeparatorX.Value)}"
            : "Verified separator program: none";
        tailLabel.Text = plan.HasSeparatedTail
            ? $"Proposed retained tail: {Measure(plan.TailLength)} length × {Measure(plate.Size.Width)} width"
            : "Proposed retained tail: none verified";

        var blocked = plans.Any(p => p.HasBlockingDiagnostics);
        var definitionCount = plans.Sum(p => p.Definitions.Count);
        applyButton.Enabled = !blocked && definitionCount > 0;
        if (nest == null)
        {
            diagnosticsBox.Text = string.Join(Environment.NewLine + Environment.NewLine,
                new[] { PlanStatus(plan) }.Concat(Diagnostics(plan)));
            return;
        }

        sheetLabel.Text = $"All {plates.Length} plates — active plate {currentIndex + 1}: "
            + $"{Measure(plate.Size.Length)} length × {Measure(plate.Size.Width)} width";
        var status = blocked
            ? "BLOCKED: no plates will be changed until every blocking issue is resolved."
            : $"{definitionCount} new cut-off definition(s) across {plans.Count(p => p.Definitions.Count > 0)} plate(s). "
                + "Empty/unchanged plates will be left untouched.";
        var summaries = plans.Select((p, index) =>
        {
            var sheet = plates[index];
            var tail = p.HasSeparatedTail
                ? $"Retained tail: {Measure(p.TailLength)} length × {Measure(sheet.Size.Width)} width; "
                    + $"separator X = {SignedPosition(p.TailSeparatorX.Value)}."
                : "Retained tail: none verified.";
            return string.Join(Environment.NewLine, new[]
            {
                $"Plate {index + 1} ({Measure(sheet.Size.Length)} length × {Measure(sheet.Size.Width)} width): {PlanStatus(p)}",
                $"Occupied: {Measure(p.OccupiedSpan)}; used span: {Measure(p.UsedSpan)}. {tail}",
            }.Concat(Diagnostics(p)));
        });
        diagnosticsBox.Text = string.Join(Environment.NewLine + Environment.NewLine, new[] { status }.Concat(summaries));
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

    private void SettingsBox_TextChanged(object sender, EventArgs e)
    {
        ClearPreview();
        ResetSummary();
        diagnosticsBox.Text = "Settings changed. Preview or Apply will calculate a fresh plan.";
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
        try
        {
            // The batch service replans every plate before mutating any; never accept previews.
            var (plates, plans) = Calculate(apply: true);
            DisplayPlans(plates, plans);
            if (plans.Any(p => p.HasBlockingDiagnostics) || plans.All(p => p.Definitions.Count == 0))
                return;

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

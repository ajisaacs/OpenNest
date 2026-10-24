using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Support;

namespace OpenNest.Forms;

/// <summary>
/// Nest &gt; Support Bed...: edits what every sheet of the current nest rests on, a laser grid of
/// contact points or a plasma table's slats, used to judge whether cut parts stay level and where
/// support tabs go (see
/// docs/support-tabs.md). OK applies it to the nest; Cancel discards the edits. Save as Machine
/// Defaults writes the grid to the nest's machine configuration immediately and is not undone by Cancel.
/// </summary>
public sealed class SupportGridForm : Form
{
    private readonly Units units;
    private readonly SupportGridSettings machineDefaults;
    private readonly Action<SupportGridSettings> saveMachineDefaults;
    private readonly CheckBox useGridBox;
    private readonly NumericUpDown pitchXBox;
    private readonly NumericUpDown pitchYBox;
    private readonly NumericUpDown offsetXBox;
    private readonly NumericUpDown offsetYBox;
    private readonly RadioButton gridButton;
    private readonly RadioButton slatsButton;
    private readonly ComboBox slatAxisBox;
    private readonly NumericUpDown slatPitchBox;
    private readonly NumericUpDown slatThicknessBox;
    private readonly NumericUpDown slatOffsetBox;
    private readonly NumericUpDown toleranceBox;
    private readonly NumericUpDown marginBox;
    private readonly CheckBox applyToNewNestsBox;
    private readonly Label savedLabel;

    /// <param name="machineName">The nest's machine, or null when none is assigned.</param>
    /// <param name="saveMachineDefaults">
    /// Saves the grid as that machine's; null disables saving, when the nest has no machine on file.
    /// </param>
    public SupportGridForm(SupportBed bed, Units units, SupportGridSettings machineDefaults, string machineName,
        Action<SupportGridSettings> saveMachineDefaults)
    {
        this.units = units;
        this.machineDefaults = machineDefaults ?? new SupportGridSettings();
        this.saveMachineDefaults = saveMachineDefaults;
        // A nest without a bed starts from the machine defaults, so enabling it is one click; the
        // layout it does not use keeps the defaults' values.
        var initial = bed != null
            ? SupportGridSettings.FromBed(bed, units, this.machineDefaults.Enabled, this.machineDefaults)
            : this.machineDefaults.ConvertedTo(units);
        var unit = UnitsHelper.GetShortString(units);

        Text = "Support Bed";
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
        };
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Padding = new Padding(0, 0, 0, 8),
            Text = "What cut parts rest on: a grid of contact points, one every pitch along X and Y, the "
                + "first at the offset from the plate origin into the sheet; or slats, each a strip of "
                + "the slat thickness running the full length or width of the table, one every pitch, "
                + "the first centered at the offset. A part whose center of gravity is not inside its "
                + "contacts by the stability margin can tip; with \"Tab parts the support grid cannot "
                + "hold level\" in Cutting Settings, Plan Cutting tabs it where the tab steadies it. "
                + "The registration tolerance allows for a sheet loaded off its place.",
        });

        useGridBox = new CheckBox { Text = "This nest's sheets rest on a support bed", AutoSize = true, Checked = bed != null };
        layout.Controls.Add(useGridBox);

        var kinds = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(16, 0, 0, 0) };
        gridButton = new RadioButton { Text = "Grid of points", AutoSize = true, Checked = !initial.UseSlats };
        slatsButton = new RadioButton { Text = "Slats", AutoSize = true, Checked = initial.UseSlats };
        kinds.Controls.Add(gridButton);
        kinds.Controls.Add(slatsButton);
        layout.Controls.Add(kinds);

        var gridValues = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        pitchXBox = NewLengthBox(initial.PitchX, 0.0001m);
        pitchYBox = NewLengthBox(initial.PitchY, 0.0001m);
        offsetXBox = NewLengthBox(initial.OffsetX, -100000m);
        offsetYBox = NewLengthBox(initial.OffsetY, -100000m);
        AddRow(gridValues, "Pitch along X:", pitchXBox, unit);
        AddRow(gridValues, "Pitch along Y:", pitchYBox, unit);
        AddRow(gridValues, "First point, X from the origin:", offsetXBox, unit);
        AddRow(gridValues, "First point, Y from the origin:", offsetYBox, unit);
        layout.Controls.Add(gridValues);

        var slatValues = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        slatAxisBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        slatAxisBox.Items.AddRange(new object[] { "Along X (the full length)", "Along Y (the full width)" });
        slatAxisBox.SelectedIndex = initial.SlatAxis == SlatAxis.X ? 0 : 1;
        slatPitchBox = NewLengthBox(initial.SlatPitch, 0.0001m);
        slatThicknessBox = NewLengthBox(initial.SlatThickness, 0m);
        slatOffsetBox = NewLengthBox(initial.SlatOffset, -100000m);
        AddRow(slatValues, "Slats run:", slatAxisBox, "");
        AddRow(slatValues, "Pitch between slats:", slatPitchBox, unit);
        AddRow(slatValues, "Slat thickness:", slatThicknessBox, unit);
        AddRow(slatValues, "First slat's center from the origin:", slatOffsetBox, unit);
        layout.Controls.Add(slatValues);

        var sharedValues = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        toleranceBox = NewLengthBox(initial.RegistrationTolerance, 0m);
        marginBox = NewLengthBox(initial.StabilityMargin, 0m);
        AddRow(sharedValues, "Registration tolerance:", toleranceBox, unit);
        AddRow(sharedValues, "Stability margin:", marginBox, unit);
        layout.Controls.Add(sharedValues);

        var defaultsGroup = new GroupBox
        {
            Text = string.IsNullOrEmpty(machineName) ? "Machine defaults (no machine assigned)" : $"Machine defaults ({machineName})",
            AutoSize = true,
            Padding = new Padding(8),
        };
        var defaultsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        applyToNewNestsBox = new CheckBox
        {
            Text = "Give nests on this machine this bed",
            AutoSize = true,
            Checked = this.machineDefaults.Enabled,
        };
        var defaultsButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var loadDefaultsButton = new Button { Text = "Load Machine Defaults", AutoSize = true };
        var saveDefaultsButton = new Button { Text = "Save as Machine Defaults", AutoSize = true };
        defaultsButtons.Controls.Add(loadDefaultsButton);
        defaultsButtons.Controls.Add(saveDefaultsButton);
        savedLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            Text = saveMachineDefaults == null
                ? "Assign this nest a machine on file in Nest > Machine to save its grid."
                : "Save as Machine Defaults writes the machine configuration immediately; Cancel below does not undo it.",
        };
        applyToNewNestsBox.Enabled = saveMachineDefaults != null;
        saveDefaultsButton.Enabled = saveMachineDefaults != null;
        defaultsLayout.Controls.Add(applyToNewNestsBox);
        defaultsLayout.Controls.Add(defaultsButtons);
        defaultsLayout.Controls.Add(savedLabel);
        defaultsGroup.Controls.Add(defaultsLayout);
        layout.Controls.Add(defaultsGroup);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        useGridBox.CheckedChanged += (_, _) => UpdateEnabled();
        slatsButton.CheckedChanged += (_, _) => UpdateEnabled();
        loadDefaultsButton.Click += (_, _) => ShowValues(this.machineDefaults.ConvertedTo(units));
        saveDefaultsButton.Click += (_, _) => SaveMachineDefaults();
        ok.Click += (_, _) => Accept();
        UpdateEnabled();
    }

    /// <summary>The bed the nest's sheets rest on after OK, or null for none.</summary>
    public SupportBed Bed { get; private set; }

    private SupportGridSettings Read() => new()
    {
        Enabled = applyToNewNestsBox.Checked,
        Units = units,
        PitchX = (double)pitchXBox.Value,
        PitchY = (double)pitchYBox.Value,
        OffsetX = (double)offsetXBox.Value,
        OffsetY = (double)offsetYBox.Value,
        UseSlats = slatsButton.Checked,
        SlatAxis = slatAxisBox.SelectedIndex == 0 ? SlatAxis.X : SlatAxis.Y,
        SlatPitch = (double)slatPitchBox.Value,
        SlatThickness = (double)slatThicknessBox.Value,
        SlatOffset = (double)slatOffsetBox.Value,
        RegistrationTolerance = (double)toleranceBox.Value,
        StabilityMargin = (double)marginBox.Value,
    };

    private void ShowValues(SupportGridSettings settings)
    {
        pitchXBox.Value = ToBoxValue(pitchXBox, settings.PitchX);
        pitchYBox.Value = ToBoxValue(pitchYBox, settings.PitchY);
        offsetXBox.Value = ToBoxValue(offsetXBox, settings.OffsetX);
        offsetYBox.Value = ToBoxValue(offsetYBox, settings.OffsetY);
        gridButton.Checked = !settings.UseSlats;
        slatsButton.Checked = settings.UseSlats;
        slatAxisBox.SelectedIndex = settings.SlatAxis == SlatAxis.X ? 0 : 1;
        slatPitchBox.Value = ToBoxValue(slatPitchBox, settings.SlatPitch);
        slatThicknessBox.Value = ToBoxValue(slatThicknessBox, settings.SlatThickness);
        slatOffsetBox.Value = ToBoxValue(slatOffsetBox, settings.SlatOffset);
        toleranceBox.Value = ToBoxValue(toleranceBox, settings.RegistrationTolerance);
        marginBox.Value = ToBoxValue(marginBox, settings.StabilityMargin);
        applyToNewNestsBox.Checked = settings.Enabled;
    }

    private void UpdateEnabled()
    {
        var on = useGridBox.Checked;
        gridButton.Enabled = slatsButton.Enabled = on;
        foreach (var box in new Control[] { pitchXBox, pitchYBox, offsetXBox, offsetYBox })
            box.Enabled = on && !slatsButton.Checked;
        foreach (var box in new Control[] { slatAxisBox, slatPitchBox, slatThicknessBox, slatOffsetBox })
            box.Enabled = on && slatsButton.Checked;
        toleranceBox.Enabled = marginBox.Enabled = on;
    }

    private void SaveMachineDefaults()
    {
        var settings = Read();
        try
        {
            saveMachineDefaults(settings);
            savedLabel.Text = settings.Enabled
                ? $"Saved {DateTime.Now:t}: nests on this machine get this bed. Cancel does not undo this."
                : $"Saved {DateTime.Now:t}: nests on this machine get no bed. Cancel does not undo this.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, $"The machine defaults could not be saved:\n\n{ex.Message}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Accept()
    {
        Bed = useGridBox.Checked ? Read().BedFor(units) : null;
        if (useGridBox.Checked && Bed == null)
        {
            MessageBox.Show(this, slatsButton.Checked ? "The slat pitch must be greater than zero."
                : "Both pitches must be greater than zero.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private static NumericUpDown NewLengthBox(double value, decimal minimum)
    {
        var box = new NumericUpDown
        {
            DecimalPlaces = 4,
            Minimum = minimum,
            Maximum = 100000,
            Increment = 0.125m,
            Width = 110,
        };
        box.Value = ToBoxValue(box, value);
        return box;
    }

    private static decimal ToBoxValue(NumericUpDown box, double value)
    {
        if (!double.IsFinite(value) || value < (double)box.Minimum)
            return box.Minimum;
        return value > (double)box.Maximum ? box.Maximum : (decimal)value;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control box, string unit)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        table.Controls.Add(box);
        table.Controls.Add(new Label { Text = unit, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
    }
}

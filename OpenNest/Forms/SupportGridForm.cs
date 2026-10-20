using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Support;

namespace OpenNest.Forms;

/// <summary>
/// Nest &gt; Support Grid...: edits the laser grid of contact points every sheet of the current
/// nest rests on, used to judge whether cut parts stay level and where support tabs go (see
/// docs/support-tabs.md). OK applies it to the nest; Cancel discards the edits. Save as Machine
/// Defaults writes support-grid.json immediately and is not undone by Cancel.
/// </summary>
public sealed class SupportGridForm : Form
{
    private readonly Units units;
    private readonly SupportGridSettings machineDefaults;
    private readonly string settingsPath;
    private readonly CheckBox useGridBox;
    private readonly NumericUpDown pitchXBox;
    private readonly NumericUpDown pitchYBox;
    private readonly NumericUpDown offsetXBox;
    private readonly NumericUpDown offsetYBox;
    private readonly NumericUpDown toleranceBox;
    private readonly NumericUpDown marginBox;
    private readonly CheckBox applyToNewNestsBox;
    private readonly Label savedLabel;

    public SupportGridForm(SupportBed bed, Units units, SupportGridSettings machineDefaults, string settingsPath = null)
    {
        this.units = units;
        this.machineDefaults = machineDefaults ?? new SupportGridSettings();
        this.settingsPath = settingsPath ?? SupportGridSettings.DefaultPath;
        var grid = bed as SupportGrid;
        // A nest without a grid starts from the machine defaults, so enabling it is one click.
        var initial = grid != null
            ? SupportGridSettings.FromGrid(grid, units, this.machineDefaults.Enabled)
            : this.machineDefaults.ConvertedTo(units);
        var unit = UnitsHelper.GetShortString(units);

        Text = "Support Grid";
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
            Text = "The grid of contact points cut parts rest on, one every pitch along X and Y, the first "
                + "at the offset from the plate origin into the sheet. A part whose center of gravity "
                + "is not inside its contacts by the stability margin can tip; with \"Tab parts the "
                + "grid cannot support\" in Cutting Settings, Plan Cutting tabs it where the tab "
                + "steadies it. The registration tolerance allows for a sheet loaded off its place.",
        });

        useGridBox = new CheckBox { Text = "This nest's sheets rest on a support grid", AutoSize = true, Checked = grid != null };
        layout.Controls.Add(useGridBox);

        var values = new TableLayoutPanel { AutoSize = true, ColumnCount = 3 };
        pitchXBox = NewLengthBox(initial.PitchX, 0.0001m);
        pitchYBox = NewLengthBox(initial.PitchY, 0.0001m);
        offsetXBox = NewLengthBox(initial.OffsetX, -100000m);
        offsetYBox = NewLengthBox(initial.OffsetY, -100000m);
        toleranceBox = NewLengthBox(initial.RegistrationTolerance, 0m);
        marginBox = NewLengthBox(initial.StabilityMargin, 0m);
        AddRow(values, "Pitch along X:", pitchXBox, unit);
        AddRow(values, "Pitch along Y:", pitchYBox, unit);
        AddRow(values, "First point, X from the origin:", offsetXBox, unit);
        AddRow(values, "First point, Y from the origin:", offsetYBox, unit);
        AddRow(values, "Registration tolerance:", toleranceBox, unit);
        AddRow(values, "Stability margin:", marginBox, unit);
        layout.Controls.Add(values);

        var defaultsGroup = new GroupBox { Text = "Machine defaults (support-grid.json)", AutoSize = true, Padding = new Padding(8) };
        var defaultsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        applyToNewNestsBox = new CheckBox
        {
            Text = "Give new nests this grid",
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
            Text = "Save as Machine Defaults writes the file immediately; Cancel below does not undo it.",
        };
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
        loadDefaultsButton.Click += (_, _) => ShowValues(this.machineDefaults.ConvertedTo(units));
        saveDefaultsButton.Click += (_, _) => SaveMachineDefaults();
        ok.Click += (_, _) => Accept();
        UpdateEnabled();
    }

    /// <summary>The grid the nest's sheets rest on after OK, or null for none.</summary>
    public SupportGrid Grid { get; private set; }

    private SupportGridSettings Read() => new()
    {
        Enabled = applyToNewNestsBox.Checked,
        Units = units,
        PitchX = (double)pitchXBox.Value,
        PitchY = (double)pitchYBox.Value,
        OffsetX = (double)offsetXBox.Value,
        OffsetY = (double)offsetYBox.Value,
        RegistrationTolerance = (double)toleranceBox.Value,
        StabilityMargin = (double)marginBox.Value,
    };

    private void ShowValues(SupportGridSettings settings)
    {
        pitchXBox.Value = ToBoxValue(pitchXBox, settings.PitchX);
        pitchYBox.Value = ToBoxValue(pitchYBox, settings.PitchY);
        offsetXBox.Value = ToBoxValue(offsetXBox, settings.OffsetX);
        offsetYBox.Value = ToBoxValue(offsetYBox, settings.OffsetY);
        toleranceBox.Value = ToBoxValue(toleranceBox, settings.RegistrationTolerance);
        marginBox.Value = ToBoxValue(marginBox, settings.StabilityMargin);
        applyToNewNestsBox.Checked = settings.Enabled;
    }

    private void UpdateEnabled()
    {
        foreach (var box in new[] { pitchXBox, pitchYBox, offsetXBox, offsetYBox, toleranceBox, marginBox })
            box.Enabled = useGridBox.Checked;
    }

    private void SaveMachineDefaults()
    {
        var settings = Read();
        try
        {
            settings.Save(settingsPath);
            savedLabel.Text = settings.Enabled
                ? $"Saved {DateTime.Now:t}: new nests get this grid. Cancel does not undo this."
                : $"Saved {DateTime.Now:t}: new nests get no grid. Cancel does not undo this.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, $"The machine defaults could not be saved:\n{settingsPath}\n\n{ex.Message}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Accept()
    {
        Grid = useGridBox.Checked ? Read().GridFor(units) : null;
        if (useGridBox.Checked && Grid == null)
        {
            MessageBox.Show(this, "Both pitches must be greater than zero.", Text,
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

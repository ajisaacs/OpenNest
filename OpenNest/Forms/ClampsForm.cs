using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms;

/// <summary>
/// Nest &gt; Clamps...: edits the current nest's machine clamp rail (identical clamps, each on
/// one sheet edge with its center measured from the plate origin along it). OK applies the rail
/// to every plate of this nest; Cancel discards those edits. Save as Machine Defaults writes
/// clamps.json immediately and is not undone by Cancel. All decisions live in
/// <see cref="ClampRailEditor"/>; this form only renders and forwards input.
/// </summary>
public sealed class ClampsForm : Form
{
    private readonly ClampSettings machineDefaults;
    private readonly Action<ClampSettings> saveDefaults;
    private readonly NumericUpDown widthBox;
    private readonly NumericUpDown depthBox;
    private readonly NumericUpDown removalMinutesBox;
    private readonly NumericUpDown centerBox;
    private readonly ComboBox edgeBox;
    private readonly ListBox centersList;
    private readonly Button addButton;
    private readonly Button removeButton;
    private readonly Button okButton;
    private readonly CheckBox applyToNewNestsBox;
    private readonly Label savedLabel;

    /// <param name="saveDefaults">Saves the machine defaults; this PC's clamp settings file when omitted.</param>
    public ClampsForm(ClampRail rail, Units units, ClampSettings machineDefaults, Action<ClampSettings> saveDefaults = null)
    {
        this.machineDefaults = machineDefaults ?? ClampSettings.Fallback;
        this.saveDefaults = saveDefaults ?? (settings => settings.Save(ClampSettings.DefaultPath));
        Editor = new ClampRailEditor(rail, units, this.machineDefaults.Enabled);
        var unit = UnitsHelper.GetShortString(units);

        Text = "Clamps";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 470);
        MinimumSize = new Size(440, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.Sizable;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(
            new Label
            {
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                Padding = new Padding(0, 0, 0, 8),
                Text =
                    "Fixed clamps on the plate edges, each centered a distance from the origin "
                    + "along its edge. Auto Nest keeps parts clear of each clamp by the plate's "
                    + "bottom edge spacing; clamps that do not reach a smaller plate are left off "
                    + "it. Plate > Edit Clamps moves and adds clamps on the plate view.",
            }
        );

        var size = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        widthBox = NewLengthBox(Editor.Width, minimum: 0.0001m);
        depthBox = NewLengthBox(Editor.Depth, minimum: 0.0001m);
        removalMinutesBox = NewLengthBox(Editor.RemovalMinutes, minimum: 0m);
        AddRow(size, "Clamp width (along the edge):", widthBox, unit);
        AddRow(size, "Clamp depth (into the plate):", depthBox, unit);
        AddRow(size, "Minutes to remove and replace one clamp:", removalMinutesBox, "min");
        layout.Controls.Add(size);

        var centersGroup = new GroupBox
        {
            Text = $"Clamp centers, measured from the plate origin ({unit})",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };
        centersList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var centerEntry = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            WrapContents = false,
        };
        centerBox = NewLengthBox(0, minimum: 0m);
        edgeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        foreach (var edge in ClampEdgeText.All)
            edgeBox.Items.Add(ClampEdgeText.Name(edge));
        edgeBox.SelectedIndex = 0;
        addButton = new Button { Text = "Add", AutoSize = true };
        removeButton = new Button { Text = "Remove", AutoSize = true, Enabled = false };
        centerEntry.Controls.Add(edgeBox);
        centerEntry.Controls.Add(new Label { Text = "Center:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        centerEntry.Controls.Add(centerBox);
        centerEntry.Controls.Add(addButton);
        centerEntry.Controls.Add(removeButton);
        centersGroup.Controls.Add(centersList);
        centersGroup.Controls.Add(centerEntry);
        layout.Controls.Add(centersGroup);

        var defaultsGroup = new GroupBox
        {
            Text = "Machine defaults (clamps.json)",
            Dock = DockStyle.Top,
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
            Text = "Give new nests these clamps",
            AutoSize = true,
            Checked = Editor.ApplyToNewNests,
        };
        var defaultsButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var loadDefaultsButton = new Button { Text = "Load Machine Defaults", AutoSize = true };
        var saveDefaultsButton = new Button { Text = "Save as Machine Defaults", AutoSize = true };
        defaultsButtons.Controls.Add(loadDefaultsButton);
        defaultsButtons.Controls.Add(saveDefaultsButton);
        savedLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Text =
                "Save as Machine Defaults writes the file immediately; Cancel below does not undo "
                + "it. OK and Cancel only affect this nest.",
        };
        defaultsLayout.Controls.Add(applyToNewNestsBox);
        defaultsLayout.Controls.Add(defaultsButtons);
        defaultsLayout.Controls.Add(savedLabel);
        defaultsGroup.Controls.Add(defaultsLayout);
        layout.Controls.Add(defaultsGroup);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        okButton = new Button { Text = "OK", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(okButton);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancel;

        addButton.Click += (_, _) => AddCenter();
        removeButton.Click += (_, _) => RemoveCenter();
        centersList.SelectedIndexChanged += (_, _) =>
            removeButton.Enabled = centersList.SelectedIndex >= 0;
        // Enter in the center box adds that center instead of closing the dialog.
        centerBox.Enter += (_, _) => AcceptButton = addButton;
        centerBox.Leave += (_, _) => AcceptButton = okButton;
        loadDefaultsButton.Click += (_, _) => LoadMachineDefaults();
        saveDefaultsButton.Click += (_, _) => SaveMachineDefaults();
        okButton.Click += (_, _) => Accept();

        RefreshCenters();
    }

    /// <summary>The edited rail; the caller applies it to the nest after OK.</summary>
    internal ClampRailEditor Editor { get; }

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

    private void ReadSizes()
    {
        Editor.Width = (double)widthBox.Value;
        Editor.Depth = (double)depthBox.Value;
        Editor.RemovalMinutes = (double)removalMinutesBox.Value;
        Editor.ApplyToNewNests = applyToNewNestsBox.Checked;
    }

    private void AddCenter()
    {
        var clamp = new RailClamp(ClampEdgeText.All[edgeBox.SelectedIndex], (double)centerBox.Value);
        if (!Editor.TryAddClamp(clamp, out var error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RefreshCenters();
        centerBox.Select(0, centerBox.Text.Length);
    }

    private void RemoveCenter()
    {
        var index = centersList.SelectedIndex;
        Editor.RemoveCenterAt(index);
        RefreshCenters();
        if (centersList.Items.Count > 0)
            centersList.SelectedIndex = System.Math.Min(index, centersList.Items.Count - 1);
    }

    private void RefreshCenters()
    {
        centersList.BeginUpdate();
        centersList.Items.Clear();
        foreach (var clamp in Editor.Clamps)
            centersList.Items.Add(
                $"{ClampEdgeText.Name(clamp.Edge)}:  {clamp.Center.ToString("0.####", CultureInfo.CurrentCulture)}"
            );
        centersList.EndUpdate();
        removeButton.Enabled = centersList.SelectedIndex >= 0;
    }

    private void LoadMachineDefaults()
    {
        Editor.LoadMachineDefaults(machineDefaults);
        widthBox.Value = ToBoxValue(widthBox, Editor.Width);
        depthBox.Value = ToBoxValue(depthBox, Editor.Depth);
        removalMinutesBox.Value = ToBoxValue(removalMinutesBox, Editor.RemovalMinutes);
        applyToNewNestsBox.Checked = Editor.ApplyToNewNests;
        RefreshCenters();
    }

    private void SaveMachineDefaults()
    {
        ReadSizes();
        try
        {
            var settings = Editor.ToMachineDefaults();
            saveDefaults(settings);
            savedLabel.Text = settings.Enabled
                ? $"Saved {DateTime.Now:t}: new nests get these {settings.ClampCount} clamps. Cancel does not undo this."
                : $"Saved {DateTime.Now:t}: new nests get no clamps. Cancel does not undo this.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                $"The machine defaults could not be saved.\n\n{ex.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void Accept()
    {
        ReadSizes();
        try
        {
            Editor.BuildRail();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
    }
}

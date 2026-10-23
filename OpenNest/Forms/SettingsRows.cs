using System.Windows.Forms;

namespace OpenNest.Forms;

/// <summary>
/// Label / value box / unit rows shared by the machine-settings dialogs built in code
/// (Clamps, Support Grid).
/// </summary>
internal static class SettingsRows
{
    public static NumericUpDown NewLengthBox(double value, decimal minimum)
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

    /// <summary>The value clamped to the box's range; non-finite values become the minimum.</summary>
    public static decimal ToBoxValue(NumericUpDown box, double value)
    {
        if (!double.IsFinite(value) || value < (double)box.Minimum)
            return box.Minimum;
        return value > (double)box.Maximum ? box.Maximum : (decimal)value;
    }

    public static void AddRow(TableLayoutPanel table, string label, Control box, string unit)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        table.Controls.Add(box);
        table.Controls.Add(new Label { Text = unit, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
    }
}

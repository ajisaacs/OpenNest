using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// The drawing library's Filter dialog: a customer and optional, inclusive ranges of
    /// thickness, size (longer and shorter side), holes and saved date. Thickness and sizes are
    /// inches; a box left blank sets no limit. OK is refused, with the reason shown, until every
    /// box reads and each range runs low to high. Clear empties every box.
    /// </summary>
    public partial class DrawingLibraryFilterForm : Form
    {
        /// <summary>The filter read when OK was pressed.</summary>
        public DrawingLibraryFilter Filter { get; private set; }

        public DrawingLibraryFilterForm(DrawingLibraryFilter filter, IEnumerable<string> customers = null)
        {
            InitializeComponent();
            Filter = filter ?? DrawingLibraryFilter.None;
            customerBox.Items.AddRange((customers ?? Array.Empty<string>()).Cast<object>().ToArray());
            ShowFilter(Filter);
        }

        /// <summary>Reads every box; false with the first problem when one does not read or a range is reversed.</summary>
        internal bool TryReadFilter(out DrawingLibraryFilter filter, out string error)
        {
            filter = null;
            if (!TryThickness(minThicknessBox, "lowest thickness", out var minThickness, out error)
                || !TryThickness(maxThicknessBox, "highest thickness", out var maxThickness, out error)
                || !TryLength(minLengthBox.Text, "lowest longer side", out var minLength, out error)
                || !TryLength(maxLengthBox.Text, "highest longer side", out var maxLength, out error)
                || !TryLength(minWidthBox.Text, "lowest shorter side", out var minWidth, out error)
                || !TryLength(maxWidthBox.Text, "highest shorter side", out var maxWidth, out error)
                || !TryCount(minHolesBox.Text, "fewest holes", out var minHoles, out error)
                || !TryCount(maxHolesBox.Text, "most holes", out var maxHoles, out error))
                return false;

            var read = new DrawingLibraryFilter
            {
                Customer = customerBox.Text.Trim(),
                MinThickness = minThickness,
                MaxThickness = maxThickness,
                IncludeBlankThickness = blankThicknessCheckBox.Checked,
                MinLength = minLength,
                MaxLength = maxLength,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                MinHoles = minHoles,
                MaxHoles = maxHoles,
                SavedFrom = savedFromPicker.Checked ? DateOnly.FromDateTime(savedFromPicker.Value) : null,
                SavedTo = savedToPicker.Checked ? DateOnly.FromDateTime(savedToPicker.Value) : null,
            };
            error = read.GetValidationError();
            if (error != null)
                return false;
            filter = read;
            return true;
        }

        /// <summary>
        /// Reads a length in inches: a number or a fraction ("3/16", "1-1/2"), with an optional
        /// inch mark. Blank is no limit. Gauges are thicknesses, not lengths, so they are refused.
        /// </summary>
        internal static bool TryParseLength(string text, out double? value)
        {
            value = null;
            var trimmed = (text ?? "").Trim().TrimEnd('"').Trim();
            if (trimmed.EndsWith("in", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^2].Trim();
            if (trimmed.Length == 0)
                return true;
            if (trimmed.Contains("ga", StringComparison.OrdinalIgnoreCase) || trimmed.Contains('#'))
                return false;
            if (!StandardThicknesses.TryParse(trimmed, Units.Inches, out var parsed))
                return false;
            value = parsed;
            return true;
        }

        private void ShowFilter(DrawingLibraryFilter filter)
        {
            customerBox.Text = filter.Customer;
            minThicknessBox.Text = filter.MinThickness is { } minThickness ? StandardThicknesses.Format(minThickness, Units.Inches) : "";
            maxThicknessBox.Text = filter.MaxThickness is { } maxThickness ? StandardThicknesses.Format(maxThickness, Units.Inches) : "";
            blankThicknessCheckBox.Checked = filter.IncludeBlankThickness;
            minLengthBox.Text = Number(filter.MinLength);
            maxLengthBox.Text = Number(filter.MaxLength);
            minWidthBox.Text = Number(filter.MinWidth);
            maxWidthBox.Text = Number(filter.MaxWidth);
            minHolesBox.Text = filter.MinHoles?.ToString(CultureInfo.CurrentCulture) ?? "";
            maxHolesBox.Text = filter.MaxHoles?.ToString(CultureInfo.CurrentCulture) ?? "";
            ShowDate(savedFromPicker, filter.SavedFrom);
            ShowDate(savedToPicker, filter.SavedTo);
            errorLabel.Text = "";
        }

        private static void ShowDate(DateTimePicker picker, DateOnly? date)
        {
            picker.Value = (date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            picker.Checked = date != null;
        }

        private static string Number(double? value) =>
            value?.ToString("0.####", CultureInfo.CurrentCulture) ?? "";

        private static bool TryThickness(Controls.ThicknessComboBox box, string name, out double? value, out string error)
        {
            value = null;
            error = null;
            if (box.Text.Trim().Length == 0)
                return true;
            if (box.TryGetValue(out var parsed))
            {
                value = parsed;
                return true;
            }
            error = $"The {name} must be a number, a fraction such as 3/16 or a gauge such as 16 ga.";
            return false;
        }

        private static bool TryLength(string text, string name, out double? value, out string error)
        {
            error = TryParseLength(text, out value) ? null : $"The {name} must be inches: a number or a fraction such as 3-1/2.";
            return error == null;
        }

        private static bool TryCount(string text, string name, out int? value, out string error)
        {
            value = null;
            error = null;
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0)
                return true;
            if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.CurrentCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
            error = $"The {name} must be a whole number, zero or greater.";
            return false;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (!TryReadFilter(out var filter, out var error))
            {
                errorLabel.Text = error;
                return;
            }
            Filter = filter;
            DialogResult = DialogResult.OK;
        }

        private void ClearButton_Click(object sender, EventArgs e) => ShowFilter(DrawingLibraryFilter.None);
    }
}

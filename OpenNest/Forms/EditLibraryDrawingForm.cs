using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Math;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing library &gt; Edit Drawing. Edits one library drawing's details (name, customer,
    /// material, grade, thickness, cut type; units are shown, not edited), notes and nesting
    /// settings (Cut first, priority, rotation constraints, color) through
    /// <see cref="DrawingLibraryEditSession"/>. Saving adds a revision on the server that
    /// keeps the same package; nests that already hold the drawing keep their revision. When
    /// the new details would duplicate another active library drawing (D4), the operator is
    /// warned before saving.
    /// <para>
    /// Customer, material, grade and thickness are drop-downs of the values the library and
    /// the machine material catalog already use (<see cref="DrawingLibraryChoices"/>); any
    /// value may still be typed. The rotation range is shown and dragged on a
    /// <see cref="OpenNest.Controls.RotationRangeDial"/> kept in step with the angle boxes. The color is either
    /// custom or automatic, where each nest's color scheme picks it.
    /// </para>
    /// </summary>
    public partial class EditLibraryDrawingForm : Form
    {
        private const int CollisionNames = 5;

        private readonly DrawingLibraryEditSession session;
        private readonly decimal initialStep;
        private readonly decimal initialStart;
        private readonly decimal initialEnd;
        private readonly string initialThickness;
        private readonly Color initialColor;
        private DrawingLibraryChoices choices = DrawingLibraryChoices.Empty;
        private Color color;
        private bool saving;

        /// <summary>Shows a message; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>The materials and grades on file for the drop-downs; replaced by tests.</summary>
        internal Func<IEnumerable<MaterialConfig>> LoadMaterialCatalog { get; set; } = MachineSettings.LoadMaterials;

        /// <summary>The library's drop-down choices once loaded; replaced by tests.</summary>
        internal Task ChoicesLoaded { get; private set; } = Task.CompletedTask;

        /// <summary>The saved revision once the dialog closes with OK; otherwise null.</summary>
        public DrawingLibraryRecord Saved { get; private set; }

        public EditLibraryDrawingForm(DrawingLibraryEditSession session)
        {
            InitializeComponent();
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);

            var listed = session.Listed;
            nameBox.Text = listed.Name;
            customerBox.Text = listed.Customer;
            materialBox.Text = listed.MaterialName;
            gradeBox.Text = listed.MaterialGrade;
            thicknessBox.Units = listed.Units;
            thicknessBox.Value = DrawingLibraryRules.NormalizeThickness(listed.Thickness);
            initialThickness = thicknessBox.Text;
            unitsLabel.Text = UnitsHelper.GetShortString(listed.Units);
            cutTypeBox.Items.AddRange(Enum.GetNames<CutType>());
            cutTypeBox.SelectedItem = listed.CutType.ToString();
            var settings = listed.Settings ?? new DrawingNestingSettings();
            cutFirstCheckBox.Checked = settings.CutFirst;
            priorityBox.Value = settings.Priority;
            initialStep = SetDegrees(stepBox, settings.StepAngle);
            initialStart = SetDegrees(startBox, settings.StartAngle);
            initialEnd = SetDegrees(endBox, settings.EndAngle);
            allow180CheckBox.Checked = settings.Allow180Equivalent;
            initialColor = Color.FromArgb(settings.ColorArgb);
            color = initialColor;
            notesBox.Text = listed.Notes;

            Text = $"Edit '{listed.Name}' — library revision {listed.Revision}";
            UpdateRotation();
            UpdateColor();
            UpdateCommands();
        }

        private void EditLibraryDrawingForm_Shown(object sender, EventArgs e) => ChoicesLoaded = LoadChoicesAsync();

        /// <summary>
        /// Fills the drop-downs from the material catalog at once, then from the library;
        /// a library that cannot be read leaves the catalog's lists.
        /// </summary>
        internal async Task LoadChoicesAsync()
        {
            var catalog = (LoadMaterialCatalog?.Invoke() ?? Enumerable.Empty<MaterialConfig>())
                .Concat(PostProcessorMaterials.Names.Select(name => new MaterialConfig { Name = name }))
                .ToList();
            ApplyChoices(DrawingLibraryChoices.From(new[] { session.Listed }, session.Listed.Units, catalog));
            try
            {
                var loaded = await session.LoadChoicesAsync(catalog);
                if (!IsDisposed)
                    ApplyChoices(loaded);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is ArgumentException)
            {
                // The drop-downs are suggestions; typing still works without them.
            }
        }

        private void ApplyChoices(DrawingLibraryChoices loaded)
        {
            choices = loaded;
            SetItems(customerBox, choices.Customers);
            SetItems(materialBox, choices.Materials);
            RefreshGrades();
        }

        private void Material_Changed(object sender, EventArgs e)
        {
            RefreshGrades();
            UpdateCommands();
        }

        private void Grade_Changed(object sender, EventArgs e)
        {
            thicknessBox.SetUsedValues(choices.ThicknessesOf(materialBox.Text, gradeBox.Text));
            UpdateCommands();
        }

        /// <summary>Lists the grades and thicknesses used for the material as typed.</summary>
        private void RefreshGrades()
        {
            SetItems(gradeBox, choices.GradesOf(materialBox.Text));
            thicknessBox.SetUsedValues(choices.ThicknessesOf(materialBox.Text, gradeBox.Text));
        }

        // Replaces a drop-down's list, keeping what is typed and where the caret is.
        private static void SetItems(ComboBox box, IReadOnlyList<string> items)
        {
            if (box.Items.Cast<string>().SequenceEqual(items))
                return;
            var text = box.Text;
            var (start, length) = (box.SelectionStart, box.SelectionLength);
            box.BeginUpdate();
            box.Items.Clear();
            box.Items.AddRange(items.ToArray<object>());
            box.EndUpdate();
            if (box.Text != text)
            {
                box.Text = text;
                box.Select(start, length);
            }
        }

        /// <summary>The details as currently entered.</summary>
        internal DrawingLibraryDetails ReadDetails() => new(
            nameBox.Text,
            customerBox.Text,
            materialBox.Text,
            gradeBox.Text,
            ReadThickness(),
            Enum.TryParse<CutType>(cutTypeBox.SelectedItem as string, out var cutType) ? cutType : session.Listed.CutType);

        // Untouched text keeps the stored thickness exactly; unreadable text fails the rules instead of reading as 0.
        private double ReadThickness() =>
            thicknessBox.Text == initialThickness ? session.Listed.Thickness
            : thicknessBox.TryGetValue(out var thickness) ? thickness
            : double.NaN;

        /// <summary>The settings as currently entered; unchanged angles keep their exact stored radians.</summary>
        internal DrawingNestingSettings ReadSettings()
        {
            var listed = session.Listed.Settings ?? new DrawingNestingSettings();
            return new DrawingNestingSettings
            {
                CutFirst = cutFirstCheckBox.Checked,
                Priority = (int)priorityBox.Value,
                StepAngle = ReadDegrees(stepBox, initialStep, listed.StepAngle),
                StartAngle = ReadDegrees(startBox, initialStart, listed.StartAngle),
                EndAngle = ReadDegrees(endBox, initialEnd, listed.EndAngle),
                Allow180Equivalent = allow180CheckBox.Checked,
                ColorArgb = color.ToArgb(),
            };
        }

        private void Value_Changed(object sender, EventArgs e) => UpdateCommands();

        private void Rotation_Changed(object sender, EventArgs e)
        {
            UpdateRotation();
            UpdateCommands();
        }

        private void RotationDial_AnglesChanged(object sender, EventArgs e)
        {
            // Rotation_Changed redraws the dial from the boxes, which round to their decimal places.
            startBox.Value = System.Math.Clamp((decimal)rotationDial.StartAngle, startBox.Minimum, startBox.Maximum);
            endBox.Value = System.Math.Clamp((decimal)rotationDial.EndAngle, endBox.Minimum, endBox.Maximum);
        }

        private void UpdateRotation()
        {
            rotationDial.StepAngle = (double)stepBox.Value;
            rotationDial.Allow180Equivalent = allow180CheckBox.Checked;
            rotationDial.StartAngle = (double)startBox.Value;
            rotationDial.EndAngle = (double)endBox.Value;
            rotationSummaryLabel.Text = DescribeRotation((double)startBox.Value, (double)endBox.Value,
                (double)stepBox.Value, allow180CheckBox.Checked);
        }

        /// <summary>What the nesting engines do with these rotation settings, in degrees.</summary>
        internal static string DescribeRotation(double start, double end, double step, bool allow180)
        {
            if (step <= 0)
                return "Automatic: the nesting engines try 0°, 90°, 180° and 270°. Set a step to limit rotation to the range.";
            if (end < start)
                return "The end is before the start, so the nesting engines cannot use this range.";

            var count = OpenNest.Controls.RotationRangeDial.TriedAngles(start, end, step, allow180).Count;
            var angles = count == 1 ? "1 angle" : $"{count.ToString(CultureInfo.CurrentCulture)} angles";
            var range = start == end || step >= end - start
                ? $"Fixed at {Degrees(start)}"
                : $"{Degrees(start)} to {Degrees(end)} every {Degrees(step)}";
            return $"{range}{(allow180 ? ", and 180° from each" : "")}: tries {angles}.";
        }

        private static string Degrees(double degrees) => $"{degrees.ToString("0.##", CultureInfo.CurrentCulture)}°";

        private void ColorButton_Click(object sender, EventArgs e)
        {
            using var dialog = new ColorDialog { Color = color.A == 0 ? Drawing.GetPartColor(0) : color, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            // Opaque, as nest drawings are.
            color = Color.FromArgb(255, dialog.Color);
            UpdateColor();
            UpdateCommands();
        }

        private void AutomaticColorButton_Click(object sender, EventArgs e)
        {
            // Any transparent value means automatic; keep the stored one so the edit stays unchanged.
            color = initialColor.A == 0 ? initialColor : Color.FromArgb(0);
            UpdateColor();
            UpdateCommands();
        }

        private void ResetColorButton_Click(object sender, EventArgs e)
        {
            color = initialColor;
            UpdateColor();
            UpdateCommands();
        }

        private void UpdateColor()
        {
            var automatic = color.A == 0;
            colorButton.BackColor = automatic ? SystemColors.Window : color;
            colorButton.Invalidate();
            colorModeLabel.Text = automatic
                ? "Automatic: each nest's color scheme picks the part color."
                : $"Custom: #{color.R:X2}{color.G:X2}{color.B:X2} in every color scheme.";
            automaticColorButton.Enabled = !automatic;
            resetColorButton.Enabled = color.ToArgb() != initialColor.ToArgb();
        }

        // An automatic color shows a sample of the current scheme's part colors.
        private void ColorButton_Paint(object sender, PaintEventArgs e)
        {
            if (color.A != 0)
                return;
            var area = colorButton.ClientRectangle;
            area.Inflate(-3, -3);
            const int samples = 4;
            var width = area.Width / (float)samples;
            for (var i = 0; i < samples; i++)
            {
                using var brush = new SolidBrush(Drawing.GetPartColor(i));
                e.Graphics.FillRectangle(brush, area.X + i * width, area.Y, width + 1, area.Height);
            }
            TextRenderer.DrawText(e.Graphics, "Auto", colorButton.Font, area, Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private async void SaveButton_Click(object sender, EventArgs e) => await SaveAsync();

        /// <summary>Saves the edit as a new library revision; closes with OK on success.</summary>
        internal async Task SaveAsync()
        {
            if (saving || !CanSave())
                return;

            saving = true;
            UpdateCommands();
            var details = ReadDetails();
            try
            {
                if (session.ChangesIdentity(details) && !await ConfirmIdentityAsync(details))
                {
                    if (!IsDisposed)
                        statusLabel.Text = "";
                    return;
                }

                if (IsDisposed)
                    return;
                statusLabel.Text = "Saving...";
                Saved = await session.SaveAsync(details, notesBox.Text, ReadSettings());
                if (IsDisposed)
                    return;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is ArgumentException)
            {
                // A conflict (someone else saved first) is an IOException too; nothing was changed.
                if (IsDisposed)
                    return;
                statusLabel.Text = "";
                ShowMessage(ex is DrawingLibraryConflictException
                    ? ex.Message + Environment.NewLine + Environment.NewLine + "Close this dialog to reload the library, then edit again."
                    : $"Could not save the library drawing: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                saving = false;
                if (!IsDisposed)
                    UpdateCommands();
            }
        }

        /// <summary>
        /// Looks up the active library drawings the new details would duplicate (D4) and, when
        /// there are any or the lookup fails, asks whether to save anyway.
        /// </summary>
        private async Task<bool> ConfirmIdentityAsync(DrawingLibraryDetails details)
        {
            statusLabel.Text = "Checking the library for drawings with these details...";
            IReadOnlyList<DrawingLibraryRecord> collisions;
            try
            {
                collisions = await session.FindCollisionsAsync(details);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (IsDisposed)
                    return false;
                return ShowMessage($"Could not check the library for drawings with these details: {ex.Message}"
                    + Environment.NewLine + Environment.NewLine + "Save anyway?",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
            }

            if (IsDisposed)
                return false;
            if (collisions.Count == 0)
                return true;
            return ShowMessage(DescribeCollisions(collisions), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
                == DialogResult.OK;
        }

        internal static string DescribeCollisions(IReadOnlyList<DrawingLibraryRecord> collisions)
        {
            var lines = collisions.Take(CollisionNames).Select(r =>
                $"- '{r.Name}'" + (string.IsNullOrWhiteSpace(r.Customer) ? "" : $" ({r.Customer})")
                + $", {$"{r.MaterialName} {r.MaterialGrade}".Trim()} "
                + $"{r.Thickness.ToString("0.####", CultureInfo.CurrentCulture)} {UnitsHelper.GetShortString(r.Units)} {r.CutType}"
                + $", revision {r.Revision.ToString(CultureInfo.CurrentCulture)}");
            var more = collisions.Count > CollisionNames ? $"{Environment.NewLine}...and {collisions.Count - CollisionNames} more." : "";
            var count = collisions.Count == 1 ? "Another library drawing already has" : $"{collisions.Count} other library drawings already have";
            return $"{count} this name, customer, material, grade, thickness and cut type:"
                + Environment.NewLine + string.Join(Environment.NewLine, lines) + more
                + Environment.NewLine + Environment.NewLine
                + "Saving makes them hard to tell apart, and Save to Drawing Library will offer to revise either. Save anyway?";
        }

        private bool CanSave()
        {
            var details = ReadDetails();
            var settings = ReadSettings();
            return session.GetError(details, notesBox.Text, settings) == null
                && !session.IsUnchanged(details, notesBox.Text, settings);
        }

        private void UpdateCommands()
        {
            var error = session.GetError(ReadDetails(), notesBox.Text, ReadSettings());
            saveButton.Enabled = !saving && CanSave();
            if (!saving)
                statusLabel.Text = error == null ? "" : char.ToUpper(error[0], CultureInfo.CurrentCulture) + error[1..];
        }

        private static decimal SetDegrees(NumericUpDown box, double radians)
        {
            var degrees = (decimal)System.Math.Round(Angle.ToDegrees(radians), box.DecimalPlaces);
            box.Value = System.Math.Clamp(degrees, box.Minimum, box.Maximum);
            return box.Value;
        }

        private static double ReadDegrees(NumericUpDown box, decimal initial, double original) =>
            box.Value == initial ? original : Angle.ToRadians((double)box.Value);
    }
}

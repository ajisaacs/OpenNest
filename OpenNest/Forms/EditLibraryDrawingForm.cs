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
    /// </summary>
    public partial class EditLibraryDrawingForm : Form
    {
        private const int CollisionNames = 5;

        private readonly DrawingLibraryEditSession session;
        private readonly decimal initialStep;
        private readonly decimal initialStart;
        private readonly decimal initialEnd;
        private Color color;
        private bool saving;

        /// <summary>Shows a message; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

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
            thicknessBox.Value = System.Math.Clamp((decimal)DrawingLibraryRules.NormalizeThickness(listed.Thickness),
                thicknessBox.Minimum, thicknessBox.Maximum);
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
            color = Color.FromArgb(settings.ColorArgb);
            colorButton.BackColor = color.A == 0 ? SystemColors.Control : color;
            notesBox.Text = listed.Notes;

            Text = $"Edit '{listed.Name}' — library revision {listed.Revision}";
            UpdateCommands();
        }

        /// <summary>The details as currently entered.</summary>
        internal DrawingLibraryDetails ReadDetails() => new(
            nameBox.Text,
            customerBox.Text,
            materialBox.Text,
            gradeBox.Text,
            (double)thicknessBox.Value,
            Enum.TryParse<CutType>(cutTypeBox.SelectedItem as string, out var cutType) ? cutType : session.Listed.CutType);

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

        private void ColorButton_Click(object sender, EventArgs e)
        {
            using var dialog = new ColorDialog { Color = color, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            // Opaque, as nest drawings are.
            color = Color.FromArgb(255, dialog.Color);
            colorButton.BackColor = color;
            UpdateCommands();
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

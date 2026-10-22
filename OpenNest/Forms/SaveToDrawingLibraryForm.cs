using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing list &gt; Save to Drawing Library (Database mode). Edits the library details,
    /// looks up library drawings with the same details (D4) whenever they change, and saves
    /// the nest drawing as the next revision of the chosen one or as a separate drawing
    /// through <see cref="DrawingLibrarySaveSession"/>. On success the drawing's library link
    /// points at the saved revision; the nest's copy is otherwise unchanged.
    /// </summary>
    public partial class SaveToDrawingLibraryForm : Form
    {
        private readonly DrawingLibrarySaveSession session;
        private readonly string savedBy;
        private readonly DrawingNestingSettings settings;
        private DrawingLibraryDuplicates duplicates;
        private int lookupGeneration;
        private bool loading;
        private bool saving;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>The saved revision once the dialog closes with OK; otherwise null.</summary>
        public DrawingLibrarySaveResult Result { get; private set; }

        public SaveToDrawingLibraryForm(IDrawingLibraryStore library, string serverUrl, Nest nest, Drawing drawing,
            CutType lastCutType, string savedBy)
        {
            InitializeComponent();
            session = new DrawingLibrarySaveSession(library, serverUrl, nest, drawing);
            this.savedBy = savedBy ?? "";
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);

            foreach (var name in PostProcessorMaterials.Names)
                materialBox.Items.Add(name);
            foreach (var cutType in Enum.GetValues<CutType>())
                cutTypeBox.Items.Add(cutType);

            var metadata = session.Prefill(lastCutType, this.savedBy);
            loading = true;
            nameBox.Text = metadata.Name;
            customerBox.Text = metadata.Customer;
            materialBox.Text = metadata.MaterialName;
            gradeBox.Text = metadata.MaterialGrade;
            thicknessBox.Units = metadata.Units;
            thicknessBox.Value = metadata.Thickness;
            thicknessLabel.Text = $"Thickness ({UnitsHelper.GetShortString(metadata.Units)}):";
            cutTypeBox.SelectedItem = metadata.CutType;
            notesBox.Text = metadata.Notes;
            settings = metadata.Settings;
            settingsLabel.Text = DescribeSettings(settings);
            loading = false;

            Text = $"Save '{drawing.Name}' to Drawing Library — {serverUrl}";
            UpdateCommands();
        }

        private async void SaveToDrawingLibraryForm_Shown(object sender, EventArgs e) => await LookUpAsync();

        private void Details_Changed(object sender, EventArgs e)
        {
            if (loading)
                return;

            // The choice must be made against the matches for the details as they now are.
            if (duplicates != null && !duplicates.IsFor(ReadMetadata()))
            {
                duplicates = null;
                saveAsBox.Items.Clear();
                duplicateLabel.Text = "Checking the library...";
            }

            lookupTimer.Stop();
            lookupTimer.Start();
            UpdateCommands();
        }

        private void Notes_Changed(object sender, EventArgs e) => UpdateCommands();

        private async void LookupTimer_Tick(object sender, EventArgs e)
        {
            lookupTimer.Stop();
            await LookUpAsync();
        }

        private void SaveAsBox_SelectedIndexChanged(object sender, EventArgs e) => UpdateCommands();

        private async void SaveButton_Click(object sender, EventArgs e) => await SaveAsync();

        /// <summary>Finds the library drawings the current details duplicate and offers them; latest lookup wins.</summary>
        internal async Task LookUpAsync()
        {
            var generation = ++lookupGeneration;
            var metadata = ReadMetadata();
            duplicateLabel.Text = "Checking the library...";
            UpdateCommands();
            try
            {
                var found = await session.FindDuplicatesAsync(metadata);
                if (IsDisposed || generation != lookupGeneration)
                    return;
                duplicates = found;
                ShowChoices(found);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is ArgumentException)
            {
                if (IsDisposed || generation != lookupGeneration)
                    return;
                duplicates = null;
                saveAsBox.Items.Clear();
                duplicateLabel.Text = ex is ServerTooOldException
                    ? ex.Message
                    : $"Could not check the library: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed)
                    UpdateCommands();
            }
        }

        /// <summary>Packages the drawing and saves it as the selected choice; closes with OK on success.</summary>
        internal async Task SaveAsync()
        {
            if (saving || !CanSave())
                return;

            var metadata = ReadMetadata();
            var choice = (SaveChoice)saveAsBox.SelectedItem;
            saving = true;
            UpdateCommands();
            statusLabel.Text = "Saving...";
            try
            {
                // Modal, so the nest and drawing cannot change while they are packaged and sent.
                var package = DrawingPackage.Write(session.Drawing, session.Nest.Units);
                var result = await session.SaveAsync(metadata, package, choice.Revise);
                if (IsDisposed)
                    return;

                if (result.RecoveredEarlierSave)
                {
                    ShowMessage($"An earlier save that seemed to fail did reach the library as revision {result.Saved.Revision} "
                        + $"of '{result.Saved.Name}'. Your changes since then were not saved; save again to store them.",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                Result = result;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (DrawingLibraryConflictException ex)
            {
                Fail(ex.Message);
                await LookUpAsync();
            }
            catch (ServerTooOldException ex)
            {
                Fail(ex.Message);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is ArgumentException)
            {
                Fail(session.HasPendingSave
                    ? $"Could not confirm the save: {ex.Message}{Environment.NewLine}{Environment.NewLine}"
                        + "Save again to check whether it reached the library; it will not be stored twice."
                    : $"Could not save to the library: {ex.Message}");
            }
            finally
            {
                saving = false;
                if (!IsDisposed)
                {
                    if (statusLabel.Text == "Saving...")
                        statusLabel.Text = "";
                    UpdateCommands();
                }
            }
        }

        private void Fail(string message)
        {
            if (IsDisposed)
                return;
            statusLabel.Text = "";
            ShowMessage(message, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowChoices(DrawingLibraryDuplicates found)
        {
            saveAsBox.BeginUpdate();
            saveAsBox.Items.Clear();
            SaveChoice selected = null;
            foreach (var record in found.Choices)
            {
                var choice = new SaveChoice(record, Describe(record, linked: found.Linked?.Id == record.Id));
                saveAsBox.Items.Add(choice);
                if (found.Default?.Id == record.Id)
                    selected = choice;
            }

            var separate = new SaveChoice(null, "Create a separate library drawing");
            saveAsBox.Items.Add(separate);
            if (selected == null && !found.RequiresChoice && found.Matches.Count == 0)
                selected = separate;
            saveAsBox.SelectedItem = selected;
            saveAsBox.EndUpdate();

            duplicateLabel.Text = found.Matches.Count switch
            {
                0 => "No library drawing has these details.",
                1 => "A library drawing has these details; saving adds a revision to it.",
                _ when found.RequiresChoice => $"{found.Matches.Count} library drawings have these details. Choose one, or a separate drawing.",
                _ => $"{found.Matches.Count} library drawings have these details.",
            };
        }

        private static string Describe(DrawingLibraryRecord record, bool linked)
        {
            var customer = string.IsNullOrWhiteSpace(record.Customer) ? "" : $" ({record.Customer})";
            var saved = record.SavedAt == default ? "" : $", saved {record.SavedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";
            var link = linked ? " — this drawing's library copy" : "";
            return $"Revision {record.Revision + 1} of '{record.Name}'{customer}, {record.MaterialName} {record.MaterialGrade} "
                + $"{record.Thickness.ToString("0.####", CultureInfo.CurrentCulture)} {record.CutType}{saved}{link}";
        }

        private DrawingLibraryRecord ReadMetadata() => new()
        {
            Name = nameBox.Text.Trim(),
            Customer = customerBox.Text.Trim(),
            MaterialName = materialBox.Text.Trim(),
            MaterialGrade = gradeBox.Text.Trim(),
            // Unreadable text fails the metadata rules instead of reading as 0.
            Thickness = thicknessBox.TryGetValue(out var thickness)
                ? DrawingLibraryRules.NormalizeThickness(thickness)
                : double.NaN,
            Units = session.Nest.Units,
            CutType = cutTypeBox.SelectedItem is CutType cutType ? cutType : CutType.Laser,
            Notes = notesBox.Text,
            Settings = settings,
            SavedBy = savedBy,
        };

        /// <summary>A one-line summary of the drawing's nesting settings, which are saved with it.</summary>
        internal static string DescribeSettings(DrawingNestingSettings settings)
        {
            var parts = new List<string>();
            if (settings.CutFirst)
                parts.Add("Cut first");
            parts.Add($"priority {settings.Priority.ToString(CultureInfo.CurrentCulture)}");
            var start = Angle.ToDegrees(settings.StartAngle);
            var end = Angle.ToDegrees(settings.EndAngle);
            parts.Add($"rotation {start.ToString("0.##", CultureInfo.CurrentCulture)}°–{end.ToString("0.##", CultureInfo.CurrentCulture)}°");
            if (settings.StepAngle > 0)
                parts.Add($"step {Angle.ToDegrees(settings.StepAngle).ToString("0.##", CultureInfo.CurrentCulture)}°");
            if (settings.Allow180Equivalent)
                parts.Add("180° equivalent");
            return string.Join(", ", parts) + " (from the drawing; edit later in the library)";
        }

        private bool CanSave()
        {
            var metadata = ReadMetadata();
            return duplicates != null && duplicates.IsFor(metadata) && saveAsBox.SelectedItem is SaveChoice
                && DrawingLibraryRules.GetMetadataError(metadata) == null;
        }

        private void UpdateCommands()
        {
            var error = DrawingLibraryRules.GetMetadataError(ReadMetadata());
            saveButton.Enabled = !saving && CanSave();
            if (!saving)
                statusLabel.Text = error == null ? "" : char.ToUpper(error[0], CultureInfo.CurrentCulture) + error[1..];
        }

        /// <summary>One "Save as" option: a revision of a listed library drawing, or a separate drawing.</summary>
        private sealed class SaveChoice
        {
            public SaveChoice(DrawingLibraryRecord revise, string text)
            {
                Revise = revise;
                Text = text;
            }

            /// <summary>The library drawing to revise, or null for a separate drawing.</summary>
            public DrawingLibraryRecord Revise { get; }

            public string Text { get; }

            public override string ToString() => Text;
        }
    }
}

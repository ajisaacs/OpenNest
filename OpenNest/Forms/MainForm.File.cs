using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        private Nest CreateDefaultNest()
        {
            var nest = new Nest();
            LoadNestDefaults().ApplyTo(nest);
            // After the defaults so the rail is converted to the new nest's units.
            LoadClampSettings(this).ApplyToNewNest(nest);
            return nest;
        }

        /// <summary>
        /// Loads the machine clamp settings for creating new nests (New, BOM import); an
        /// unreadable or partly invalid file warns once per session, because the new nest's
        /// clamps may not match the machine.
        /// </summary>
        internal static ClampSettings LoadClampSettings(IWin32Window owner)
        {
            var settings = LoadSavedClampSettings(out var status);

            if (
                status is ClampSettingsStatus.Invalid or ClampSettingsStatus.Partial
                && !clampSettingsWarned
            )
            {
                clampSettingsWarned = true;
                var problem =
                    status == ClampSettingsStatus.Invalid
                        ? "could not be read, so new nests have no clamps"
                        : "has invalid values that were ignored, so new nests may be missing clamps";
                MessageBox.Show(
                    owner,
                    $"The clamp settings file {problem}:\n{ClampSettings.DefaultPath}\n\nCheck the clamps in Nest > Clamps and use Save as Machine Defaults to rewrite the file.",
                    "Clamps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            return settings;
        }

        private static bool clampSettingsWarned;

        /// <summary>The single desktop entry point for the persisted machine clamp settings.</summary>
        internal static ClampSettings LoadSavedClampSettings(out ClampSettingsStatus status) =>
            ClampSettings.Load(ClampSettings.DefaultPath, out status);

        /// <summary>
        /// Loads the persisted nest defaults (see <see cref="LoadSavedNestDefaults"/>);
        /// a corrupt or unreadable file warns once per session.
        /// </summary>
        private NestDefaults LoadNestDefaults()
        {
            var defaults = LoadSavedNestDefaults(out var status);

            if (status == NestDefaultsStatus.Invalid && !defaultsWarned)
            {
                defaultsWarned = true;
                MessageBox.Show(
                    $"The nest defaults file could not be read:\n{NestDefaults.DefaultPath}\n\nBuilt-in defaults will be used. Re-save your defaults from Tools > Nest Defaults.",
                    "Nest Defaults",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            return defaults;
        }

        private bool defaultsWarned;

        /// <summary>
        /// The single desktop entry point for persisted nest defaults. Units
        /// fall back to the legacy DefaultUnit setting whenever the file is
        /// missing or unusable or has no valid unit, so New, BOM import and
        /// the defaults dialog always agree.
        /// </summary>
        internal static NestDefaults LoadSavedNestDefaults(out NestDefaultsStatus status) =>
            NestDefaults.Load(
                NestDefaults.DefaultPath,
                Properties.Settings.Default.DefaultUnit,
                out status
            );

        private string GetNestName(DateTime date, int id)
        {
            var year = (date.Year % 100).ToString("D2");
            var seq = ToBase36(id).PadLeft(3, '2');

            return $"N{year}-{seq}";
        }

        private static string ToBase36(int value)
        {
            const string chars = "2345679ACDEFGHJKLMNPQRSTUVWXYZ";
            if (value == 0)
                return chars[0].ToString();

            var result = "";
            while (value > 0)
            {
                result = chars[value % chars.Length] + result;
                value /= chars.Length;
            }
            return result;
        }

        private void LoadNest(Nest nest, FormWindowState windowState = FormWindowState.Maximized,
            Guid remoteId = default, string serverUrl = null, int remoteRevision = 0)
        {
            var editForm = new EditNestForm(nest);
            if (remoteId != Guid.Empty)
                editForm.Document.BindRemote(remoteId, serverUrl, remoteRevision);
            editForm.MdiParent = this;
            editForm.PlateView.FilesDropped += (sender, paths) => OpenDroppedNestFiles(paths);
            editForm.PlateChanged += (sender, e) =>
            {
                NavigationEnableCheck();
                UpdatePlateStatus();
                mnuPlateRemove.Enabled = activeForm?.PlateManager.CanRemoveCurrent ?? false;
            };
            editForm.WindowState = windowState;
            editForm.Show();
            editForm.PlateView.ZoomToFit();
        }

        #region File Menu Events

        private void New_Click(object sender, EventArgs e)
        {
            var windowState =
                ActiveMdiChild != null ? ActiveMdiChild.WindowState : FormWindowState.Maximized;

            var nest = CreateDefaultNest();

            nest.DateCreated = DateTime.Now;
            nest.DateLastModified = DateTime.Now;

            if (DateTime.Now.Date != Settings.Default.LastNestCreatedDate.Date)
            {
                Settings.Default.NestNumber = 1;
                Settings.Default.LastNestCreatedDate = DateTime.Now.Date;
            }

            nest.Name = GetNestName(DateTime.Now, Settings.Default.NestNumber++);
            LoadNest(nest, windowState);
        }

        private async void Open_Click(object sender, EventArgs e)
        {
            if (NestStorage.Settings.Mode == NestStorageMode.File)
            {
                using var dlg = new OpenFileDialog { Filter = NestFormat.FileFilter, Multiselect = true };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var reader = new NestReader(dlg.FileName) { BestFitStore = BestFitCacheStore.Shared };
                    var nest = reader.Read();
                    LoadNest(nest);
                    ShowNestWarnings(reader);
                }
                return;
            }

            try
            {
                var repository = NestStorage.CreateRepository();
                var serverUrl = NestStorage.Settings.ServerUrl;
                using var dlg = new SavedNestsForm(repository, serverUrl);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                databaseOpenInProgress = true;
                storageModeMenu.Enabled = false;
                mnuToolsOptions.Enabled = false;
                var snapshot = await NestHistorySession.ReadCurrentAsync(repository, dlg.SelectedId, serverUrl);
                using var stream = new MemoryStream(snapshot.Archive);
                var reader = new NestReader(stream) { BestFitStore = BestFitCacheStore.Shared };
                var nest = reader.Read();
                // Metadata edits create revisions without rewriting the archive. Use their
                // current editable fields rather than silently reverting them on the next save.
                nest.Name = snapshot.Metadata.Name;
                nest.Customer = snapshot.Metadata.Customer;
                nest.Notes = snapshot.Metadata.Comments;
                nest.Status = snapshot.Metadata.Status;
                nest.MadeBy = snapshot.Metadata.MadeBy;
                nest.DateLastModified = snapshot.Metadata.DateModified;
                LoadNest(nest, remoteId: snapshot.Metadata.Id, serverUrl: snapshot.ServerUrl,
                    remoteRevision: snapshot.Metadata.Revision);
                ShowNestWarnings(reader);
            }
            catch (Exception ex)
            {
                ShowStorageError("open", ex);
            }
            finally
            {
                databaseOpenInProgress = false;
                if (!IsDisposed)
                {
                    storageModeMenu.Enabled = !databaseSaveInProgress;
                    mnuToolsOptions.Enabled = !databaseSaveInProgress;
                }
            }
        }

        private void ShowNestWarnings(NestReader reader)
        {
            if (reader.Warnings.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, reader.Warnings),
                    "Nest Load Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ImportBom_Click(object sender, EventArgs e)
        {
            var form = new BomImportForm();
            form.MdiParentForm = this;
            form.ShowDialog(this);
        }

        private async void Save_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || databaseSaveInProgress)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.Save();
            else
                await SaveDatabaseAsync(form, saveCopy: false);
        }

        private async void SaveAs_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || databaseSaveInProgress)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
            {
                form.SaveAs();
                return;
            }

            // A pending upload is reconciled without writing, so only ask for a name
            // when this click will actually create the copy.
            string copyName = null;
            if (!form.Document.PendingCreateRequiresReconciliation)
            {
                using var dlg = new NestNameForm(form.Nest.Name);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                copyName = dlg.NestName;
            }
            await SaveDatabaseAsync(form, saveCopy: true, copyName);
        }

        private async Task SaveDatabaseAsync(EditNestForm form, bool saveCopy, string copyName = null)
        {
            var originalName = form.Nest.Name;
            databaseSaveInProgress = true;
            storageModeMenu.Enabled = false;
            mnuToolsOptions.Enabled = false;
            EnableCheck();
            try
            {
                var serverUrl = NestStorage.Settings.ServerUrl;
                var repository = NestStorage.CreateRepository();
                if (form.Document.PendingCreateRequiresReconciliation)
                {
                    var reconciled = await form.Document.ReconcilePendingCreateAsync(repository, serverUrl);
                    if (!IsDisposed)
                        statusLabel1.Text = reconciled is null
                            ? $"No pending nest was found. Review your work, then {(form.Document.PendingCreateWasCopy ? "Save As" : "Save")} again to retry the same ID."
                            : $"Found saved version {reconciled.Revision}. Review the current nest before saving again.";
                    return; // Never combine reconciliation with a write in the same click.
                }
                if (copyName != null)
                    form.Nest.Name = copyName;
                var saved = await form.Document.SaveToDatabaseAsync(
                    repository, serverUrl, saveCopy);
                if (!form.IsDisposed)
                    form.Text = form.Document.Name;
                if (!IsDisposed)
                    statusLabel1.Text = $"Saved {saved.Name} to nest database";
            }
            catch (Exception ex)
            {
                // Keep the new name only while its copy may still be pending on the server;
                // otherwise a later Save would rename the original nest.
                if (copyName != null && !form.Document.PendingCreateWasCopy)
                    form.Nest.Name = originalName;
                if (!IsDisposed)
                    ShowStorageError("save", ex);
            }
            finally
            {
                databaseSaveInProgress = false;
                if (!IsDisposed)
                {
                    storageModeMenu.Enabled = !databaseOpenInProgress;
                    mnuToolsOptions.Enabled = !databaseOpenInProgress;
                    EnableCheck();
                }
            }
        }

        private void StorageMode_Click(object sender, EventArgs e)
        {
            using var dlg = new StorageModeForm();
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                NestStorage.Save(dlg.SelectedMode, dlg.ServerUrl);
                statusLabel1.Text = $"Nest storage: {dlg.SelectedMode}";
                foreach (var form in MdiChildren.OfType<EditNestForm>())
                    form.UpdateDrawingLibraryCommand();
            }
            catch (Exception ex)
            {
                ShowStorageError("change storage mode", ex);
            }
        }

        private void ExportNest_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null)
                return;
            using var dlg = new SaveFileDialog { Filter = NestFormat.FileFilter, FileName = form.Nest.Name };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                // Export never changes the document's save path, remote id, or nest name.
                new NestWriter(form.Nest) { BestFitStore = BestFitCacheStore.Shared }.Write(dlg.FileName);
                statusLabel1.Text = $"Exported .nest to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                ShowStorageError("export", ex);
            }
        }

        private void ShowStorageError(string action, Exception ex) =>
            MessageBox.Show(this, $"Could not {action} nest: {ex.Message}",
                "Nest Storage", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private void Export_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Export();
        }

        private void ExportAll_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ExportAll();
        }

        private void ExportNestReport_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null)
                return;

            // The report never depends on a selected post or NC output; it only needs a
            // quiescent nest so the snapshot is coherent.
            Func<bool> isJobBusy = IsNestJobBusy;
            EditNestForm.NestReportTargets targets;
            try
            {
                targets = form.CaptureReportTargets(isJobBusy);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                ReportFailure(null, ex);
                return;
            }

            var destination = ShowReportSaveDialog(targets.SuggestedFileName);
            if (destination == null)
                return; // Cancel: nothing written, job untouched.

            try
            {
                form.WriteNestReport(targets, destination, isJobBusy);
            }
            catch (Exception ex)
                when (ex is InvalidOperationException
                    or NotSupportedException
                    or InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                // Failures describe the captured target; an active-document switch is irrelevant.
                ReportFailure(targets, ex);
                return;
            }
            statusLabel1.Text = $"Saved nest report to {destination}";
            ReportSuccess(destination);
        }

        /// <summary>Returns the chosen path, or null on cancel. Overwrite consent is the dialog prompt.</summary>
        internal virtual string ShowReportSaveDialog(string suggestedFileName)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PDF report (*.pdf)|*.pdf",
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true,
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        /// <summary>Failure notice naming the fixed target; internal so tests observe it without dialogs.</summary>
        internal virtual void ReportFailure(
            EditNestForm.NestReportTargets targets,
            Exception error) =>
            MessageBox.Show(this,
                $"Could not export the nest report{(targets == null ? "" : $" for '{targets.Nest.Name}'")}: {error.Message}",
                "Export Nest Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        /// <summary>Success notice; internal so tests observe completion without dialogs.</summary>
        internal virtual void ReportSuccess(string destination) =>
            MessageBox.Show(this, $"Nest report saved to:\n\n{destination}", "Export Nest Report",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        #endregion File Menu Events
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.Forms.Commands;
using OpenNest.IO;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
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

            LoadNest(NewNestFactory.Create(this), windowState);
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

            await storage.OpenAsync((nest, reader, snapshot) =>
            {
                LoadNest(nest, remoteId: snapshot.Metadata.Id, serverUrl: snapshot.ServerUrl,
                    remoteRevision: snapshot.Metadata.Revision);
                ShowNestWarnings(reader);
            });
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
            if (form == null || storage.IsSaving)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.Save();
            else
                await storage.SaveAsync(form);
        }

        private async void SaveAs_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || storage.IsSaving)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.SaveAs();
            else
                await storage.SaveAsAsync(form);
        }

        private void Storage_StateChanged(object sender, EventArgs e)
        {
            storageModeMenu.Enabled = !storage.IsBusy;
            mnuToolsOptions.Enabled = !storage.IsBusy;
            EnableCheck();
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
                storage.ShowError("change storage mode", ex);
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
                storage.ShowError("export", ex);
            }
        }

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

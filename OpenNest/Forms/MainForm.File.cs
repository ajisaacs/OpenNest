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
        // The Drawing Sets window while it is open, and the release it was building when it last closed.
        private DrawingSetsForm drawingSetsForm;
        private Controls.ReleaseDraft releaseDraft;

        private void LoadNest(Nest nest, FormWindowState windowState = FormWindowState.Maximized,
            Guid remoteId = default, string serverUrl = null, int remoteRevision = 0)
        {
            // The view follows the application's scheme, not the one the file was saved under.
            ColorSchemeRegistry.RecolorDrawings(nest);
            var editForm = new EditNestForm(nest);
            if (remoteId != Guid.Empty)
                editForm.Document.BindRemote(remoteId, serverUrl, remoteRevision);
            editForm.MdiParent = this;
            editForm.PlateView.FilesDropped += (sender, paths) => OpenDroppedNestFiles(paths);
            editForm.PlateChanged += (sender, e) =>
            {
                NavigationEnableCheck();
                statusBar.UpdatePlate();
                mnuPlateRemove.Enabled = activeForm?.PlateManager.CanRemoveCurrent ?? false;
                mnuNestClear.Enabled = activeForm?.PlateManager.CanClearAll ?? false;
            };
            editForm.WindowState = windowState;
            editForm.Show();
            editForm.PlateView.ZoomToFit();
            if (remoteId != Guid.Empty)
                _ = editForm.HoldStatus.RefreshAfterSaveAsync();
        }

        /// <summary>Rereads every open nest's holds, e.g. after the storage mode or ledger changed.</summary>
        internal void RefreshLedgerHolds()
        {
            foreach (var form in MdiChildren.OfType<EditNestForm>())
                _ = form.HoldStatus.RefreshAfterSaveAsync();
        }

        /// <summary>
        /// Opens a nest that is already on the nest server (a drawing set release made it) in
        /// its own tab, bound to that record so the next Save updates it.
        /// </summary>
        internal void OpenServerNest(Nest nest, NestRecord record)
        {
            ArgumentNullException.ThrowIfNull(nest);
            ArgumentNullException.ThrowIfNull(record);
            // Make Nests nested it in a tab already: that tab becomes the saved nest.
            if (MdiChildren.OfType<EditNestForm>().FirstOrDefault(f => ReferenceEquals(f.Nest, nest)) is { } shown)
            {
                shown.Document.BindRemote(record.Id, NestStorage.Settings.ServerUrl, record.Revision);
                releaseTabs.Remove(shown);
                _ = shown.HoldStatus.RefreshAfterSaveAsync();
                return;
            }
            var windowState = ActiveMdiChild?.WindowState ?? FormWindowState.Maximized;
            LoadNest(nest, windowState, record.Id, NestStorage.Settings.ServerUrl, record.Revision);
        }

        /// <summary>True when a tab holds the server nest <paramref name="id"/>.</summary>
        internal bool IsServerNestOpen(Guid id) =>
            id != Guid.Empty && MdiChildren.OfType<EditNestForm>().Any(form => form.Document.RemoteId == id);

        #region File Menu Events

        private async void New_Click(object sender, EventArgs e)
        {
            var windowState =
                ActiveMdiChild != null ? ActiveMdiChild.WindowState : FormWindowState.Maximized;

            LoadNest(await NewNestFactory.CreateAsync(this), windowState);
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

        /// <summary>
        /// File mode: the BOM import dialog, which makes nests directly. Database mode: a BOM
        /// becomes a drawing set instead (Drawing Sets &gt; More &gt; Import BOM), whose Make Nests
        /// makes the nests.
        /// </summary>
        private void ImportBom_Click(object sender, EventArgs e)
        {
            if (NestStorage.Settings.IsDatabaseMode)
            {
                ShowDrawingSets(releases: false, importBom: true);
                return;
            }

            var form = new BomImportForm();
            form.MdiParentForm = this;
            form.ShowDialog(this);
        }

        private void FileMenu_Opening(object sender, EventArgs e)
        {
            // The drawing library and sets live on the nest server; File mode has nothing to browse.
            var databaseMode = NestStorage.Settings.IsDatabaseMode;
            mnuFile.DropDown.ShowItemToolTips = true;
            mnuFileDrawingLibrary.Enabled = databaseMode;
            mnuFileDrawingLibrary.ToolTipText = databaseMode ? "" : NestStorage.DatabaseModeOnly;
            mnuFileDrawingSets.Enabled = databaseMode;
            mnuFileDrawingSets.ToolTipText = databaseMode ? "" : NestStorage.DatabaseModeOnly;
            mnuFileReleases.Enabled = databaseMode;
            mnuFileReleases.ToolTipText = databaseMode ? "" : NestStorage.DatabaseModeOnly;
            mnuFileImportBom.ToolTipText = databaseMode
                ? "Imports the BOM into a new drawing set; Make Nests then makes its nests."
                : "";
        }

        /// <summary>
        /// Opens the Drawing Sets window on its Library tab, which edits the drawings' details and
        /// settings; needs no open nest. Drawings are added to sets or released from it, never to a nest.
        /// </summary>
        private void FileDrawingLibrary_Click(object sender, EventArgs e) => ShowDrawingSets(releases: false, libraryTab: true);

        /// <summary>
        /// Browses, edits and releases drawing sets; needs no open nest, since a release
        /// creates or merges into nests and opens each in its own tab.
        /// </summary>
        private void DrawingSets_Click(object sender, EventArgs e) => ShowDrawingSets(releases: false);

        /// <summary>A shortcut to the Drawing Sets window's Releases tab, listing every release.</summary>
        private void Releases_Click(object sender, EventArgs e) => ShowDrawingSets(releases: true);

        private void ShowDrawingSets(bool releases, bool importBom = false, bool libraryTab = false)
        {
            if (!NestStorage.Settings.IsDatabaseMode)
                return;

            // One Drawing Sets window at a time: a second menu click brings it forward on the tab asked for.
            if (drawingSetsForm is { IsDisposed: false } open)
            {
                if (open.WindowState == FormWindowState.Minimized)
                    open.WindowState = FormWindowState.Normal;
                open.Activate();
                if (releases)
                    _ = open.ShowReleasesAsync();
                else if (libraryTab)
                    open.ShowLibrary();
                else if (importBom)
                    _ = open.ImportBomAsync();
                else
                    open.ShowSets();
                return;
            }

            IDrawingLibraryStore library;
            IDrawingSetStore sets;
            INestRepository nests;
            try
            {
                library = NestStorage.CreateDrawingLibrary();
                sets = NestStorage.CreateDrawingSetStore();
                nests = NestStorage.CreateRepository();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Drawing Sets", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // A window of its own, beside the main window rather than in it: the main window's
            // menus and toolbar edit nests. It is not owned, so either can be in front.
            var form = new DrawingSetsForm(sets, library, NestStorage.Settings.ServerUrl)
            {
                Icon = Icon,
                ShowIcon = true,
                ShowInTaskbar = true,
                MinimizeBox = true,
            };
            form.EnableRelease(nests, OpenServerNest, IsServerNestOpen, this);
            form.RestoreRelease(releaseDraft);
            releaseDraft = null;
            // The nests a release made open here, so this window comes forward to show them.
            form.NestsOpened += (_, _) => Activate();
            if (releases)
                _ = form.ShowReleasesAsync();
            else if (libraryTab)
                form.ShowLibrary();
            form.ImportBomWhenShown = importBom;
            MainWindowSettings.LoadDrawingSets(form);
            form.FormClosed += (_, _) =>
            {
                // A release not yet made is kept for the next time the window opens in this run.
                releaseDraft = form.TakeRelease();
                MainWindowSettings.SaveDrawingSets(form);
                if (drawingSetsForm == form)
                    drawingSetsForm = null;
            };
            drawingSetsForm = form;
            form.Show();
        }

        private async void Save_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || storage.IsSaving || RefuseSaveDuringRelease(form))
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.Save();
            else
                await storage.SaveAsync(form);
        }

        private async void SaveAs_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || storage.IsSaving || RefuseSaveDuringRelease(form))
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
                RefreshLedgerHolds();
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

        private void Export_Click(object sender, EventArgs e) => WithActive(f => f.Export());

        private void ExportAll_Click(object sender, EventArgs e) => WithActive(f => f.ExportAll());

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

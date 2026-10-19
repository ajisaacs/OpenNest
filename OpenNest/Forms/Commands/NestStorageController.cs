using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms.Commands
{
    /// <summary>
    /// Nest-database open and save for the main window. Tracks the in-progress open and save
    /// so the owner can lock storage settings and saving; <see cref="StateChanged"/> fires
    /// whenever either changes.
    /// </summary>
    internal sealed class NestStorageController
    {
        private static readonly TimeSpan NameCheckTimeout = TimeSpan.FromSeconds(3);

        private readonly Form owner;
        private readonly Action<string> showStatus;

        public NestStorageController(Form owner, Action<string> showStatus)
        {
            this.owner = owner;
            this.showStatus = showStatus;
        }

        public event EventHandler StateChanged;

        /// <summary>True while a background save serializes a live nest.</summary>
        public bool IsSaving { get; private set; }

        public bool IsOpening { get; private set; }

        public bool IsBusy => IsSaving || IsOpening;

        /// <summary>
        /// Lets the user pick a saved nest and reads its current revision; <paramref name="load"/>
        /// receives the nest with its current metadata applied, the reader for its warnings,
        /// and the snapshot that identifies the remote copy.
        /// </summary>
        public async Task OpenAsync(Action<Nest, NestReader, NestCurrentSnapshot> load)
        {
            try
            {
                var repository = NestStorage.CreateRepository();
                var serverUrl = NestStorage.Settings.ServerUrl;
                using var dlg = new SavedNestsForm(repository, serverUrl);
                if (dlg.ShowDialog(owner) != DialogResult.OK)
                    return;

                SetOpening(true);
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
                load(nest, reader, snapshot);
            }
            catch (Exception ex)
            {
                ShowError("open", ex);
            }
            finally
            {
                SetOpening(false);
            }
        }

        public Task SaveAsync(EditNestForm form) => SaveAsync(form, saveCopy: false);

        public async Task SaveAsAsync(EditNestForm form)
        {
            // A pending upload is reconciled without writing, so only ask for a name
            // when this click will actually create the copy.
            string copyName = null;
            if (!form.Document.PendingCreateRequiresReconciliation)
            {
                using var dlg = new NestNameForm(form.Nest.Name);
                if (dlg.ShowDialog(owner) != DialogResult.OK)
                    return;
                copyName = dlg.NestName;
            }
            await SaveAsync(form, saveCopy: true, copyName);
        }

        private async Task SaveAsync(EditNestForm form, bool saveCopy, string copyName = null)
        {
            var originalName = form.Nest.Name;
            var renamed = false;
            SetSaving(true);
            try
            {
                var serverUrl = NestStorage.Settings.ServerUrl;
                var repository = NestStorage.CreateRepository();
                if (form.Document.PendingCreateRequiresReconciliation)
                {
                    var reconciled = await form.Document.ReconcilePendingCreateAsync(repository, serverUrl);
                    if (!owner.IsDisposed)
                        showStatus(reconciled is null
                            ? $"No pending nest was found. Review your work, then {(form.Document.PendingCreateWasCopy ? "Save As" : "Save")} again to retry the same ID."
                            : $"Found saved version {reconciled.Revision}. Review the current nest before saving again.");
                    return; // Never combine reconciliation with a write in the same click.
                }
                if (copyName != null)
                    form.Nest.Name = copyName;
                else if (!saveCopy && form.Document.IsFirstDatabaseSave(serverUrl))
                {
                    var chosen = await ChooseFirstSaveNameAsync(repository, form.Nest.Name);
                    if (chosen is null)
                    {
                        if (!owner.IsDisposed)
                            showStatus("Save cancelled");
                        return;
                    }
                    renamed = chosen != originalName;
                    form.Nest.Name = chosen;
                }
                var saved = await form.Document.SaveToDatabaseAsync(
                    repository, serverUrl, saveCopy);
                if (!form.IsDisposed)
                    form.Text = form.Document.Name;
                if (!owner.IsDisposed)
                    showStatus($"Saved {saved.Name} to nest database");
            }
            catch (Exception ex)
            {
                // Keep the new name only while its copy may still be pending on the server;
                // otherwise a later Save would rename the original nest.
                if (copyName != null && !form.Document.PendingCreateWasCopy)
                    form.Nest.Name = originalName;
                // Likewise a duplicate-name rename only sticks once its upload may exist.
                if (renamed && form.Document.PendingCreateId == Guid.Empty)
                    form.Nest.Name = originalName;
                if (!owner.IsDisposed)
                    ShowError("save", ex);
            }
            finally
            {
                SetSaving(false);
            }
        }

        /// <summary>
        /// The name for a nest's first upload: its own name unless another server nest
        /// already has it, in which case the user confirms or edits a newly allocated name.
        /// Null when the user cancels. A failed check keeps the current name.
        /// </summary>
        private async Task<string> ChooseFirstSaveNameAsync(INestRepository repository, string name)
        {
            var year = DateTime.Now.Year;
            NestNameAllocation replacement;
            using (var timeout = new CancellationTokenSource(NameCheckTimeout))
                replacement = await NestNameConflicts.FindReplacementAsync(repository, name, year, timeout.Token);
            if (replacement is null)
                return name;

            if (year == Settings.Default.LastNestCreatedDate.Year)
                Settings.Default.NestNumber = System.Math.Max(Settings.Default.NestNumber, replacement.Number + 1);
            using var dlg = new NestNameForm(replacement.Name, "Save",
                $"A nest named \"{name}\" is already on the server. Save this nest as:");
            return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.NestName : null;
        }

        public void ShowError(string action, Exception ex) =>
            MessageBox.Show(owner, $"Could not {action} nest: {ex.Message}",
                "Nest Storage", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private void SetOpening(bool value)
        {
            IsOpening = value;
            OnStateChanged();
        }

        private void SetSaving(bool value)
        {
            IsSaving = value;
            OnStateChanged();
        }

        private void OnStateChanged()
        {
            if (!owner.IsDisposed)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

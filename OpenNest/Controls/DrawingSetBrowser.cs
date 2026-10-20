using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// Browses the server's drawing sets one bounded page at a time through
    /// <see cref="DrawingSetBrowseSession"/>, and creates, edits, archives or restores them and
    /// shows their history. The Drawing Library dialog hosts it as its Sets tab. In pick mode
    /// (<see cref="ChooseDrawingSetForm"/>) only active sets are listed, and choosing a set or
    /// New Set raises <see cref="SetActivated"/> or <see cref="NewSetChosen"/> instead.
    /// </summary>
    public partial class DrawingSetBrowser : UserControl
    {
        private const string LoadingText = "Loading...";

        private IDrawingSetStore sets;
        private DrawingSetBrowseSession session;
        private bool pick;
        private bool busy;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Shows the set editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingSetEditSession, DrawingSetRecord> EditSet { get; set; }

        /// <summary>Shows a set's revision history; replaced by tests.</summary>
        internal Action<DrawingSetRecord> ShowHistory { get; set; }

        /// <summary>Pick mode: an active set was double-clicked (or chosen with Enter).</summary>
        public event EventHandler SetActivated;

        /// <summary>Pick mode: New Set was chosen.</summary>
        public event EventHandler NewSetChosen;

        /// <summary>The selection changed.</summary>
        public event EventHandler SelectedSetChanged;

        public DrawingSetBrowser()
        {
            InitializeComponent();
            GridStyle.Apply(setGrid);
            nameColumn.Tag = DrawingSetSortField.Name;
            customerColumn.Tag = DrawingSetSortField.Customer;
            revisionColumn.Tag = DrawingSetSortField.Revision;
            savedColumn.Tag = DrawingSetSortField.SavedAt;
        }

        /// <summary>True once <see cref="Initialize"/> has run; the first <see cref="RefreshAsync"/> loads the list.</summary>
        public bool IsInitialized => session != null;

        /// <summary>True after the list has been requested at least once.</summary>
        public bool HasLoaded { get; private set; }

        /// <summary>The highlighted set as listed (without members).</summary>
        public DrawingSetRecord SelectedSet => setGrid.CurrentRow?.Tag as DrawingSetRecord;

        /// <param name="library">Used by the set editor to pick drawings.</param>
        /// <param name="pick">Choose an active set instead of managing them.</param>
        public void Initialize(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl, bool pick = false)
        {
            if (session != null)
                throw new InvalidOperationException("The set browser is already initialized.");
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            ArgumentNullException.ThrowIfNull(library);
            this.pick = pick;
            session = new DrawingSetBrowseSession(sets);
            ShowMessage = (text, buttons, icon) => MessageBox.Show(FindForm(), text, FindForm()?.Text ?? "Drawing Sets", buttons, icon);
            EditSet = edit =>
            {
                using var form = new EditDrawingSetForm(edit, library, serverUrl);
                return form.ShowDialog(FindForm()) == DialogResult.OK ? form.Saved : null;
            };
            ShowHistory = set =>
            {
                using var form = new DrawingSetHistoryForm(sets, set);
                form.ShowDialog(FindForm());
            };

            if (pick)
            {
                newButton.Text = "New Set";
                showArchivedCheckBox.Visible = false;
                editButton.Visible = false;
                archiveButton.Visible = false;
                historyButton.Visible = false;
            }

            UpdateNavigation();
            UpdateCommands();
        }

        /// <summary>Re-reads the current page (the first call loads the list).</summary>
        public Task RefreshAsync()
        {
            HasLoaded = true;
            return RunAsync(session.RefreshAsync);
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private async void SearchTimer_Tick(object sender, EventArgs e)
        {
            searchTimer.Stop();
            if (session != null)
                await RunAsync(() => session.SetSearchAsync(searchBox.Text));
        }

        private async void ShowArchivedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (session != null)
                await RunAsync(() => session.SetIncludeArchivedAsync(showArchivedCheckBox.Checked));
        }

        private async void SetGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Sorting a page locally would misrepresent the list; the server sorts all matches.
            if (session != null && e.Button == MouseButtons.Left && setGrid.Columns[e.ColumnIndex].Tag is DrawingSetSortField field)
                await RunAsync(() => session.SortByAsync(field));
        }

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await RunAsync(session.PreviousPageAsync);

        private async void NextButton_Click(object sender, EventArgs e) =>
            await RunAsync(session.NextPageAsync);

        private void SetGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            notesBox.Text = SelectedSet?.Notes ?? "";
            UpdateCommands();
            SelectedSetChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void SetGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            if (pick)
                ActivateSelected();
            else
                await EditSelectedAsync();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && setGrid.ContainsFocus && SelectedSet != null)
            {
                if (pick)
                    ActivateSelected();
                else
                    _ = EditSelectedAsync();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async void NewButton_Click(object sender, EventArgs e) => await NewSetAsync();

        private async void EditButton_Click(object sender, EventArgs e) => await EditSelectedAsync();

        private async void ArchiveButton_Click(object sender, EventArgs e) => await ToggleArchivedAsync();

        private void HistoryButton_Click(object sender, EventArgs e)
        {
            if (SelectedSet is { } set)
                ShowHistory(set);
        }

        private void ActivateSelected()
        {
            if (SelectedSet is { Archived: false })
                SetActivated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Creates a set in the editor; in pick mode, raises <see cref="NewSetChosen"/> instead.</summary>
        internal async Task NewSetAsync()
        {
            if (session == null || busy)
                return;
            if (pick)
            {
                NewSetChosen?.Invoke(this, EventArgs.Empty);
                return;
            }

            var saved = EditSet(new DrawingSetEditSession(sets, null, Environment.UserName));
            if (saved == null || IsDisposed)
                return;
            await RunAsync(session.RefreshAsync);
            if (!IsDisposed)
                statusLabel.Text = $"Saved set '{saved.Name}' revision {saved.Revision}.";
        }

        /// <summary>
        /// Reads the selected set with its members' current drawings, edits it, then reloads
        /// the page so the list shows the saved revision.
        /// </summary>
        internal async Task EditSelectedAsync()
        {
            var listed = SelectedSet;
            if (session == null || listed == null || listed.Archived || busy)
                return;

            DrawingSetRecord current;
            busy = true;
            UpdateCommands();
            statusLabel.Text = $"Reading set '{listed.Name}'...";
            try
            {
                current = await sets.GetAsync(listed.Id);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                Fail($"Could not read set '{listed.Name}': {ex.Message}");
                return;
            }
            finally
            {
                busy = false;
                if (!IsDisposed)
                    UpdateCommands();
            }

            if (IsDisposed)
                return;
            statusLabel.Text = "";
            if (current == null || current.Archived)
            {
                ShowMessage(current == null
                    ? $"Set '{listed.Name}' no longer exists on the server."
                    : $"Set '{listed.Name}' was archived. Restore it to edit it.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RunAsync(session.RefreshAsync);
                return;
            }

            var saved = EditSet(new DrawingSetEditSession(sets, current, Environment.UserName));
            if (saved == null || IsDisposed)
                return;
            await RunAsync(session.RefreshAsync);
            if (!IsDisposed)
                statusLabel.Text = $"Saved set '{saved.Name}' revision {saved.Revision}.";
        }

        /// <summary>Archives the selected active set (after confirming) or restores an archived one.</summary>
        internal async Task ToggleArchivedAsync()
        {
            var listed = SelectedSet;
            if (session == null || listed == null || busy || pick)
                return;

            var archive = !listed.Archived;
            if (archive && ShowMessage($"Archive set '{listed.Name}'? It is hidden from the list until restored; nothing is deleted.",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            busy = true;
            UpdateCommands();
            try
            {
                var changed = archive
                    ? await sets.ArchiveAsync(listed.Id, listed.Revision)
                    : await sets.RestoreAsync(listed.Id, listed.Revision);
                if (IsDisposed)
                    return;
                statusLabel.Text = $"{(archive ? "Archived" : "Restored")} set '{changed.Name}'.";
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                // A conflict (changed since it was listed) is an IOException too; nothing was changed.
                Fail(ex is DrawingSetSaveConflictException
                    ? ex.Message
                    : $"Could not {(archive ? "archive" : "restore")} set '{listed.Name}': {ex.Message}");
            }
            finally
            {
                busy = false;
            }

            if (!IsDisposed)
            {
                var status = statusLabel.Text;
                await RunAsync(session.RefreshAsync);
                if (!IsDisposed)
                    statusLabel.Text = status;
            }
        }

        private void Fail(string message)
        {
            if (IsDisposed)
                return;
            statusLabel.Text = "";
            ShowMessage(message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private async Task RunAsync(Func<Task<bool>> request)
        {
            summaryLabel.Text = LoadingText;
            try
            {
                var started = request();
                UpdateNavigation();
                if (await started && !IsDisposed)
                    Populate(session.Page);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    setGrid.Rows.Clear();
                    summaryLabel.Text = ex is ServerTooOldException
                        ? ex.Message
                        : $"Could not load the drawing sets: {ex.Message}";
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    UpdateNavigation();
                    // A request that was declined or superseded renders nothing of its own.
                    if (!session.IsLoading && summaryLabel.Text == LoadingText)
                        summaryLabel.Text = session.Summary;
                    UpdateCommands();
                }
            }
        }

        private void Populate(DrawingSetPage page)
        {
            setGrid.Rows.Clear();
            foreach (var record in page?.Items ?? Array.Empty<DrawingSetRecord>())
            {
                // Tagged before it is added: adding the first row selects it, and the
                // selection handlers read the tag.
                var row = new DataGridViewRow { Tag = record };
                row.CreateCells(setGrid,
                    record.Name,
                    record.Customer,
                    record.MemberCount,
                    record.Revision,
                    record.SavedAt == default ? "" : record.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    record.SavedBy,
                    record.Archived ? "Archived" : "");
                setGrid.Rows.Add(row);
            }

            foreach (DataGridViewColumn column in setGrid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Tag is DrawingSetSortField field && field == session.Sort
                    ? session.Descending ? SortOrder.Descending : SortOrder.Ascending
                    : SortOrder.None;
            }

            notesBox.Text = SelectedSet?.Notes ?? "";
        }

        private void UpdateNavigation()
        {
            previousButton.Enabled = session is { IsLoading: false, CanGoPrevious: true };
            nextButton.Enabled = session is { IsLoading: false, CanGoNext: true };
        }

        private void UpdateCommands()
        {
            var selected = SelectedSet;
            var ready = session != null && !busy;
            newButton.Enabled = ready;
            editButton.Enabled = ready && selected is { Archived: false };
            archiveButton.Enabled = ready && selected != null;
            archiveButton.Text = selected is { Archived: true } ? "Restore" : "Archive";
            historyButton.Enabled = ready && selected != null;
        }
    }
}

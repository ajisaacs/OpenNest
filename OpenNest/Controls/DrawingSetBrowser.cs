using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.IO;

namespace OpenNest.Controls
{
    /// <summary>
    /// Browses the server's drawing sets one bounded page at a time through
    /// <see cref="DrawingSetBrowseSession"/>, and creates, edits, archives or restores them and
    /// shows their history. <see cref="DrawingSetsForm"/> hosts it on its Sets tab beside its
    /// <see cref="ReleasePanel"/>, and shows a set's Releases on its Releases tab. Once release is
    /// enabled, a double-click or Enter adds the set to the release (Add to Release does so for
    /// every selected set); Edit opens the set editor. New Set, Edit and Add to Release are
    /// buttons; Add to Release, Edit Set, Archive/Restore, History and Releases, then Import BOM,
    /// are in the More menu, which is also the list's right-click menu. The details pane beside
    /// the list shows the highlighted set's notes and all of its drawings at one scale
    /// (<see cref="DrawingSetPreviewControl"/>), read shortly after the selection settles; drawings
    /// are cached per revision for the control's lifetime. In pick mode
    /// (<see cref="ChooseDrawingSetForm"/>) only active sets are listed, and choosing a set or
    /// New Set raises <see cref="SetActivated"/> or <see cref="NewSetChosen"/> instead.
    /// </summary>
    public partial class DrawingSetBrowser : UserControl
    {
        private const int DetailsWidth = 420;
        private const int PreviewCacheLimit = 256;
        private const int PreviewConcurrency = 4;

        private readonly Dictionary<(Guid Id, int Revision), Drawing> previewDrawings = new();
        private CancellationTokenSource previewCancel;
        private (Guid Id, int Revision)? previewShown;
        private bool detailsFitted;
        private IDrawingSetStore sets;
        private IDrawingLibraryStore library;
        private string serverUrl;
        private DrawingSetBrowseSession session;
        private BrowsePager<DrawingSetPage, DrawingSetSortField> pager;
        private bool pick;
        private bool busy;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Shows the set editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingSetEditSession, DrawingSetRecord> EditSet { get; set; }

        /// <summary>Shows a set's revision history; replaced by tests.</summary>
        internal Action<DrawingSetRecord> ShowHistory { get; set; }

        /// <summary>Shows a set's releases; set by the host (Releases is disabled without it) and replaced by tests.</summary>
        internal Action<DrawingSetRecord> ShowReleases { get; set; }

        /// <summary>
        /// Shows Import BOM and returns the new set it prefilled (its library drawings already
        /// stored with the operator's consent), or null when canceled; replaced by tests.
        /// </summary>
        internal Func<DrawingSetEditSession> ImportBom { get; set; }

        /// <summary>
        /// Reads the drawing a record's package holds for the preview, or null when the
        /// revision is gone; replaced by tests.
        /// </summary>
        internal Func<DrawingLibraryRecord, CancellationToken, Task<Drawing>> LoadPreviewDrawing { get; set; }

        /// <summary>The preview read in flight or last finished; for tests.</summary>
        internal Task PreviewLoad { get; private set; } = Task.CompletedTask;

        internal DrawingSetPreviewControl PreviewBox => previewBox;

        /// <summary>Adds a set read with its members to the release; set by <see cref="EnableRelease"/>.</summary>
        internal Action<DrawingSetRecord> AddToRelease { get; private set; }

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
            previewBox.SetInfo("Select a set to preview its drawings.");
            Load += (_, _) => FitDetailsPane();
            // Until the first fit takes, every resize retries it; after that the split is the user's.
            splitContainer.Resize += (_, _) =>
            {
                if (!detailsFitted && IsHandleCreated)
                    FitDetailsPane();
            };
            Disposed += (_, _) => previewCancel?.Cancel();
        }

        // The list takes the spare width; the details pane keeps its width as the window resizes.
        // A container too narrow for both minimums (a tab not yet laid out) is left alone, since
        // the split container refuses minimums it cannot honor.
        internal void FitDetailsPane()
        {
            const int listMin = 320;
            const int detailsMin = 300;
            var available = splitContainer.Width - splitContainer.SplitterWidth;
            if (available < listMin + detailsMin)
                return;
            splitContainer.Panel1MinSize = listMin;
            splitContainer.SplitterDistance = System.Math.Clamp(available - DetailsWidth, listMin, available - detailsMin);
            splitContainer.Panel2MinSize = detailsMin;
            detailsFitted = true;
        }

        /// <summary>True once <see cref="Initialize"/> has run; the first <see cref="RefreshAsync"/> loads the list.</summary>
        public bool IsInitialized => session != null;

        /// <summary>True after the list has been requested at least once.</summary>
        public bool HasLoaded { get; private set; }

        /// <summary>The highlighted set as listed (without members).</summary>
        public DrawingSetRecord SelectedSet => setGrid.CurrentRow?.Tag as DrawingSetRecord;

        /// <summary>The selected sets as listed, in list order; the highlighted one when none is selected.</summary>
        internal IReadOnlyList<DrawingSetRecord> SelectedSets()
        {
            var selected = setGrid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index)
                .Select(r => r.Tag).OfType<DrawingSetRecord>().ToList();
            return selected.Count > 0 || SelectedSet == null ? selected : new[] { SelectedSet };
        }

        /// <param name="library">Used by the set editor to pick drawings.</param>
        /// <param name="pick">Choose an active set instead of managing them.</param>
        public void Initialize(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl, bool pick = false)
        {
            if (session != null)
                throw new InvalidOperationException("The set browser is already initialized.");
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            this.pick = pick;
            session = new DrawingSetBrowseSession(sets);
            Disposed += (_, _) => session.Dispose();
            pager = new BrowsePager<DrawingSetPage, DrawingSetSortField>(
                session, setGrid, summaryLabel, previousButton, nextButton, "the drawing sets")
            {
                Populate = Populate,
                Settled = UpdateCommands,
            };
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
            ImportBom = () =>
            {
                var defaults = MachineSettings.LoadSavedNestDefaults(out _);
                var cutType = Enum.TryParse<CutType>(Properties.Settings.Default.DrawingLibraryCutType, out var saved)
                    && Enum.IsDefined(saved) ? saved : CutType.Laser;
                using var form = new ImportBomToSetForm(sets, library, defaults.Units, cutType, Environment.UserName);
                return form.ShowDialog(FindForm()) == DialogResult.OK ? form.Edit : null;
            };
            setGrid.ContextMenuStrip = pick ? null : moreMenu;
            LoadPreviewDrawing = async (record, token) =>
            {
                var bytes = await library.GetRevisionPackageAsync(record.Id, record.Revision, token);
                return bytes == null ? null : await Task.Run(() => DrawingPackage.Read(bytes).Drawing, token);
            };
            setGrid.MultiSelect = !pick;
            // Add to Release acts on every selected set.
            setGrid.SelectionChanged += (_, _) => UpdateCommands();

            if (pick)
            {
                newButton.Text = "New Set";
                showArchivedCheckBox.Visible = false;
                editButton.Visible = false;
                moreButton.Visible = false;
            }

            pager.UpdateNavigation();
            UpdateCommands();
        }

        /// <summary>
        /// Shows Add to Release and makes a double-click or Enter add the set, read with its
        /// members' current drawings, through <paramref name="addToRelease"/>.
        /// </summary>
        public void EnableRelease(Action<DrawingSetRecord> addToRelease)
        {
            if (session == null || pick)
                throw new InvalidOperationException("Release needs an initialized set browser that is not picking.");
            AddToRelease = addToRelease ?? throw new ArgumentNullException(nameof(addToRelease));
            releaseButton.Visible = true;
            addToReleaseMenuItem.Visible = true;
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
            SchedulePreview();
            UpdateCommands();
            SelectedSetChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clears the preview for a new highlighted set and starts the short timer that reads it,
        /// so moving through the list does not read every set passed over.
        /// </summary>
        private void SchedulePreview()
        {
            var set = SelectedSet;
            if (set != null && previewShown == (set.Id, set.Revision))
                return;

            previewTimer.Stop();
            previewCancel?.Cancel();
            previewShown = null;
            previewBox.ShowDrawings(Array.Empty<(Drawing, string)>());
            if (session == null || set == null)
                previewBox.SetInfo("Select a set to preview its drawings.");
            else if (set.MemberCount == 0)
                previewBox.SetInfo("This set has no drawings.");
            else
            {
                previewBox.SetInfo("Loading preview...");
                previewTimer.Start();
            }
        }

        private void PreviewTimer_Tick(object sender, EventArgs e) => PreviewLoad = LoadSelectedPreviewAsync();

        /// <summary>
        /// Reads the highlighted set with its members, then each member's current drawing (cached
        /// per revision), and shows them all at one scale. A newer selection cancels it; a
        /// failure only shows in the preview, as the preview is best-effort.
        /// </summary>
        internal async Task LoadSelectedPreviewAsync()
        {
            previewTimer.Stop();
            var listed = SelectedSet;
            if (session == null || listed == null || listed.MemberCount == 0)
                return;

            previewCancel?.Cancel();
            var cancel = previewCancel = new CancellationTokenSource();
            var token = cancel.Token;
            DrawingSetRecord set;
            Drawing[] drawings;
            try
            {
                set = await sets.GetAsync(listed.Id, token);
                drawings = set == null ? null : await LoadPreviewDrawingsAsync(set.Members, token);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (cancel == previewCancel && !IsDisposed)
                    previewBox.SetInfo("Preview unavailable.", ex.Message);
                return;
            }

            if (IsDisposed || cancel != previewCancel)
                return;
            if (set == null)
            {
                previewBox.SetInfo("This set is no longer on the server.");
                return;
            }

            var members = set.Members;
            var prefix = SharedNamePrefix(members.Select(m => m.Drawing?.Name).ToList());
            previewBox.ShowDrawings(members.Select((member, i) =>
                (drawings[i], $"{member.Drawing?.Name?.Substring(prefix.Length)} ×{member.Quantity}")));
            previewBox.SetInfo(PreviewInfo(members, drawings, prefix).ToArray());
            previewShown = (listed.Id, listed.Revision);
        }

        private async Task<Drawing[]> LoadPreviewDrawingsAsync(IReadOnlyList<DrawingSetMember> members, CancellationToken token)
        {
            var drawings = new Drawing[members.Count];
            using var gate = new SemaphoreSlim(PreviewConcurrency);
            await Task.WhenAll(members.Select(async (member, i) =>
            {
                if (member.Drawing is not { } record)
                    return;
                var key = (record.Id, record.Revision);
                if (previewDrawings.TryGetValue(key, out var cached))
                {
                    drawings[i] = cached;
                    return;
                }

                await gate.WaitAsync(token);
                try
                {
                    drawings[i] = await LoadPreviewDrawing(record, token);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // One drawing that fails to load is counted as missing; the rest still show.
                    return;
                }
                finally
                {
                    gate.Release();
                }

                // Continuations resume on the UI thread, so the cache needs no lock.
                if (previewDrawings.Count >= PreviewCacheLimit)
                    previewDrawings.Clear();
                previewDrawings[key] = drawings[i];
            }));
            return drawings;
        }

        /// <summary>
        /// The leading words every name shares, ending at a space, '-', '_' or '.', so labels can
        /// drop them. Empty unless there are two or more names and each keeps text after it.
        /// </summary>
        internal static string SharedNamePrefix(IReadOnlyList<string> names)
        {
            if (names.Count < 2 || names.Any(string.IsNullOrEmpty))
                return "";

            var length = names.Min(n => n.Length);
            foreach (var name in names)
            {
                var i = 0;
                while (i < length && name[i] == names[0][i])
                    i++;
                length = i;
            }

            // Back up to just past the last separator, so no word is split; a name the prefix
            // would empty keeps its whole text.
            while (length > 0 && !IsNameSeparator(names[0][length - 1]))
                length--;
            while (length > 0 && names.Any(n => n.Skip(length).All(IsNameSeparator)))
            {
                length--;
                while (length > 0 && !IsNameSeparator(names[0][length - 1]))
                    length--;
            }

            return names[0].Substring(0, length);
        }

        private static bool IsNameSeparator(char c) => c is ' ' or '-' or '_' or '.';

        private static IEnumerable<string> PreviewInfo(IReadOnlyList<DrawingSetMember> members, Drawing[] drawings, string prefix)
        {
            var parts = members.Sum(m => m.Quantity);
            yield return $"{members.Count} drawing{(members.Count == 1 ? "" : "s")}, {parts} part{(parts == 1 ? "" : "s")}, one scale";
            if (prefix.Length > 0)
                yield return $"Names start with \"{prefix.TrimEnd()}\".";
            if (members.Where(m => m.Drawing != null).Select(m => m.Drawing.Units).Distinct().Count() > 1)
                yield return "Mixes inches and mm: those are not to scale.";
            var missing = drawings.Count(d => !DrawingSetPreviewControl.CanShow(d));
            if (missing > 0)
                yield return $"{missing} could not be loaded.";
        }

        private async void SetGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            if (pick)
                ActivateSelected();
            else if (AddToRelease != null)
                await AddSelectedToReleaseAsync(highlightedOnly: true);
            else
                await EditSelectedAsync();
        }

        private void SetGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            // A right-click acts on the row under the pointer, as the menu's commands read the selection.
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !pick)
                setGrid.CurrentCell = setGrid.Rows[e.RowIndex].Cells[System.Math.Max(e.ColumnIndex, 0)];
        }

        private void MoreButton_Click(object sender, EventArgs e) =>
            moreMenu.Show(moreButton, new Point(0, moreButton.Height));

        private void MoreMenu_Opening(object sender, CancelEventArgs e)
        {
            UpdateCommands();
            e.Cancel = session == null || pick;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && setGrid.ContainsFocus && SelectedSet != null)
            {
                if (pick)
                    ActivateSelected();
                else if (AddToRelease != null)
                    _ = AddSelectedToReleaseAsync();
                else
                    _ = EditSelectedAsync();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async void NewButton_Click(object sender, EventArgs e) => await NewSetAsync();

        private async void EditButton_Click(object sender, EventArgs e) => await EditSelectedAsync();

        private async void ArchiveMenuItem_Click(object sender, EventArgs e) => await ToggleArchivedAsync();

        private void HistoryMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedSet is { } set)
                ShowHistory(set);
        }

        private void ReleasesMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedSet is { } set)
                ShowReleases?.Invoke(set);
        }

        private async void ReleaseButton_Click(object sender, EventArgs e) => await AddSelectedToReleaseAsync();

        private async void ImportBomMenuItem_Click(object sender, EventArgs e) => await ImportBomAsync();

        /// <summary>
        /// Imports a BOM into a new set: the import dialog matches its drawings to the library,
        /// then the set editor opens prefilled for review and Save, as New Set does.
        /// </summary>
        internal async Task ImportBomAsync()
        {
            if (session == null || pick || busy)
                return;

            var edit = ImportBom();
            if (edit == null || IsDisposed)
                return;
            var saved = EditSet(edit);
            if (IsDisposed)
                return;
            if (saved == null)
            {
                statusLabel.Text = "The imported set was not saved.";
                return;
            }

            await RunAsync(session.RefreshAsync);
            if (!IsDisposed)
                statusLabel.Text = $"Saved set '{saved.Name}' revision {saved.Revision}.";
        }

        /// <summary>
        /// Reads each selected active set with its members' current drawings and adds it to the
        /// release at one kit; <paramref name="highlightedOnly"/> takes just the highlighted one,
        /// as a double-click does.
        /// </summary>
        internal async Task AddSelectedToReleaseAsync(bool highlightedOnly = false)
        {
            if (session == null || AddToRelease == null || busy)
                return;
            var chosen = (highlightedOnly && SelectedSet != null ? new[] { SelectedSet } : SelectedSets())
                .Where(s => !s.Archived).ToList();
            if (chosen.Count == 0)
                return;

            var read = new List<DrawingSetRecord>();
            var gone = new List<string>();
            busy = true;
            UpdateCommands();
            try
            {
                foreach (var listed in chosen)
                {
                    statusLabel.Text = $"Reading set '{listed.Name}'...";
                    var current = await sets.GetAsync(listed.Id);
                    if (IsDisposed)
                        return;
                    if (current == null || current.Archived)
                        gone.Add(listed.Name);
                    else
                        read.Add(current);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                Fail($"Could not read the set: {ex.Message}");
                return;
            }
            finally
            {
                busy = false;
                if (!IsDisposed)
                    UpdateCommands();
            }

            statusLabel.Text = "";
            foreach (var set in read)
                AddToRelease(set);
            if (gone.Count > 0)
            {
                ShowMessage($"No longer releasable (deleted or archived since listed): {string.Join(", ", gone.Select(n => $"'{n}'"))}.",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await RunAsync(session.RefreshAsync);
            }
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

        private Task RunAsync(Func<Task<bool>> request) => pager.RunAsync(request);

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

            // The grid only sometimes makes the first row current by itself (it depends on focus).
            if (setGrid.CurrentCell == null && setGrid.Rows.Count > 0)
                setGrid.CurrentCell = setGrid.Rows[0].Cells[0];

            pager.ShowSortGlyphs();
            notesBox.Text = SelectedSet?.Notes ?? "";
        }

        private void UpdateCommands()
        {
            var selected = SelectedSet;
            var ready = session != null && !busy;
            newButton.Enabled = ready;
            editButton.Enabled = ready && selected is { Archived: false };
            editMenuItem.Enabled = editButton.Enabled;
            editMenuItem.Visible = editSeparator.Visible = !pick;
            archiveMenuItem.Enabled = ready && selected != null;
            archiveMenuItem.Text = selected is { Archived: true } ? "&Restore" : "&Archive";
            historyMenuItem.Enabled = ready && selected != null;
            releasesMenuItem.Enabled = ready && selected != null && ShowReleases != null;
            importBomMenuItem.Enabled = ready && !pick;
            moreButton.Enabled = archiveMenuItem.Enabled || historyMenuItem.Enabled || releasesMenuItem.Enabled
                || importBomMenuItem.Enabled;
            releaseButton.Enabled = ready && AddToRelease != null
                && SelectedSets().Any(s => !s.Archived && s.MemberCount > 0);
            addToReleaseMenuItem.Enabled = releaseButton.Enabled;
        }
    }
}

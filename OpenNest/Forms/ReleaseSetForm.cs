using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing Sets &gt; Release: the operator lists the sets to release, typed in or added from
    /// the browser, each with a number of kits, and library drawings on their own (Add
    /// Drawings), each with a quantity, and the dialog previews how they split into one
    /// nest per material group (<see cref="SetReleasePlan"/>), with an archived-drawing toggle
    /// and a tick per group. Each group makes a new Draft nest or merges into a Draft nest of
    /// the same material, whose layout is discarded. <see cref="SetReleaseSession"/> then builds
    /// those nests, nests each one with Auto Nest when Nest after release is ticked, saves them,
    /// opens each in its own tab, and records the release once every chosen nest exists. The
    /// dialog shows every group's outcome; a release runs once per dialog. A group refused for
    /// its drawings (no material or thickness, or two with one name) is not a dead end: Fix
    /// Drawings, a double-click or the group's right-click menu opens Edit Drawing on the
    /// current library record, and the plan is rebuilt with what was saved.
    /// </summary>
    public partial class ReleaseSetForm : Form
    {
        private const string NewNestChoice = "New Draft nest";

        private readonly IDrawingSetStore sets;
        private readonly IDrawingLibrary library;
        // Edit Drawing needs a writable library; null hides it.
        private readonly IDrawingLibraryStore libraryStore;
        private readonly INestRepository nests;
        private readonly string serverUrl;
        private readonly Action<Nest, NestRecord> openNest;
        private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
        // The draft each group merges into, by group key, so the choice survives plan rebuilds.
        private readonly Dictionary<string, NestRecord> targets = new(StringComparer.Ordinal);
        // The drafts each group could merge into, by group key; absent while still loading.
        private readonly Dictionary<string, IReadOnlyList<NestRecord>> drafts = new(StringComparer.Ordinal);
        private SetReleasePlan plan;
        private SetReleaseSession session;
        private CancellationTokenSource cancellation;
        private CancellationTokenSource searchCancellation;
        private bool populating;
        private bool running;
        private bool loadingDrafts;
        private bool editing;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Makes the empty nest for a group, as File &gt; New would in its units; replaced by tests.</summary>
        internal Func<SetReleaseGroup, CancellationToken, Task<Nest>> CreateNest { get; set; }

        /// <summary>
        /// Nests a group's built nest when Nest after release is ticked: Auto Nest headless with
        /// the selected engine (<see cref="NestRenest"/>); replaced by tests.
        /// </summary>
        internal Func<Nest, CancellationToken, Task<SetReleaseRenest>> Renest { get; set; } = RenestAsync;

        /// <summary>
        /// True when a server nest is open in a tab of this desktop. Such a draft is not merged
        /// into: the tab could hold edits the merge would leave behind.
        /// </summary>
        public Func<Guid, bool> IsNestOpen { get; set; } = _ => false;

        /// <summary>
        /// Shows the library browser that adds drawings on their own (<see cref="AddReleaseDrawingsForm"/>);
        /// replaced by tests.
        /// </summary>
        internal Action ShowAddDrawings { get; set; }

        /// <summary>Shows Edit Drawing and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingLibraryEditSession, DrawingLibraryRecord> EditDrawing { get; set; }

        /// <summary>The finished release, once one has run; otherwise null.</summary>
        public SetReleaseResult Result { get; private set; }

        /// <param name="set">The first set to release, read with <see cref="IDrawingSetStore.GetAsync"/>, members joined.</param>
        /// <param name="openNest">Opens a created or merged nest in its own tab, bound to its server record.</param>
        public ReleaseSetForm(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
            DrawingSetRecord set, Action<Nest, NestRecord> openNest)
            : this(sets, library, nests, serverUrl, new[] { set ?? throw new ArgumentNullException(nameof(set)) },
                Array.Empty<SetReleaseDrawingLine>(), openNest)
        {
        }

        /// <param name="setsToRelease">
        /// Sets read with <see cref="IDrawingSetStore.GetAsync"/>, members joined, each at one kit.
        /// With <paramref name="drawings"/> empty too, the dialog starts empty (File &gt; Releases
        /// &gt; New Release) and the operator adds sets and drawings before releasing.
        /// </param>
        /// <param name="drawings">Library drawings to release on their own, each with its quantity.</param>
        /// <param name="openNest">Opens a created or merged nest in its own tab, bound to its server record.</param>
        public ReleaseSetForm(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
            IReadOnlyList<DrawingSetRecord> setsToRelease, IReadOnlyList<SetReleaseDrawingLine> drawings,
            Action<Nest, NestRecord> openNest)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            libraryStore = library as IDrawingLibraryStore;
            this.nests = nests ?? throw new ArgumentNullException(nameof(nests));
            this.serverUrl = serverUrl ?? "";
            this.openNest = openNest ?? throw new ArgumentNullException(nameof(openNest));
            ArgumentNullException.ThrowIfNull(setsToRelease);
            ArgumentNullException.ThrowIfNull(drawings);
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
            CreateNest = (group, _) => NewNestFactory.CreateAsync(this, group.Units);
            ShowAddDrawings = () =>
            {
                using var form = new AddReleaseDrawingsForm(library, this.serverUrl, AddDrawingLines,
                    id => DrawingLines().Any(l => l.Drawing.Id == id));
                form.ShowDialog(this);
            };
            EditDrawing = edit =>
            {
                using var form = new EditLibraryDrawingForm(edit);
                return form.ShowDialog(this) == DialogResult.OK ? form.Saved : null;
            };
            fixDrawingsButton.Visible = libraryStore != null;

            GridStyle.Apply(setGrid);
            GridStyle.Apply(groupGrid);
            populating = true;
            foreach (var set in setsToRelease)
                AddRow(set, set.Name, set.Customer, set.Revision, 1);
            foreach (var line in drawings)
                AddRow(line.Drawing, line.Drawing.Name, line.Drawing.Customer, line.Drawing.Revision, line.Quantity);
            populating = false;
            BuildPlan();
            // The first sets by name are listed before anything is typed, so the arrow shows them.
            Shown += async (_, _) => await SearchSetsAsync("", dropDown: false);
        }

        /// <summary>The sets listed, in order, with their kits.</summary>
        internal IReadOnlyList<SetReleasePlanLine> Lines() =>
            setGrid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is DrawingSetRecord)
                .Select(r => new SetReleasePlanLine((DrawingSetRecord)r.Tag, Kits(r)))
                .ToList();

        /// <summary>The drawings listed on their own, in order, with their quantities.</summary>
        internal IReadOnlyList<SetReleaseDrawingLine> DrawingLines() =>
            setGrid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is DrawingLibraryRecord)
                .Select(r => new SetReleaseDrawingLine((DrawingLibraryRecord)r.Tag, Kits(r)))
                .ToList();

        /// <summary>
        /// Adds a set read from the server (members joined) with <paramref name="kits"/>; false,
        /// with the reason shown, when it is archived or already listed.
        /// </summary>
        internal bool AddLine(DrawingSetRecord set, int kits)
        {
            ArgumentNullException.ThrowIfNull(set);
            var listed = setGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is DrawingSetRecord s && s.Id == set.Id);
            if (listed != null)
            {
                listed.Selected = true;
                statusLabel.Text = $"'{set.Name}' is already listed. Change its quantity instead.";
                return false;
            }
            if (set.Archived)
            {
                statusLabel.Text = $"'{set.Name}' is archived. Restore it before releasing it.";
                return false;
            }

            AddRow(set, set.Name, set.Customer, set.Revision, kits);
            statusLabel.Text = "";
            BuildPlan();
            return true;
        }

        /// <summary>
        /// Adds library drawings on their own at <paramref name="quantity"/> each, skipping (and
        /// naming) those already listed or archived, and returns what happened for the operator.
        /// </summary>
        internal string AddDrawingLines(IReadOnlyList<DrawingLibraryRecord> drawings, int quantity)
        {
            ArgumentNullException.ThrowIfNull(drawings);
            if (running || Result != null)
                return "The release has run; nothing can be added.";
            var listed = DrawingLines().Select(l => l.Drawing.Id).ToHashSet();
            var added = new List<DrawingLibraryRecord>();
            var already = new List<DrawingLibraryRecord>();
            var archived = new List<DrawingLibraryRecord>();
            foreach (var drawing in drawings)
            {
                if (drawing.Archived)
                    archived.Add(drawing);
                else if (!listed.Add(drawing.Id))
                    already.Add(drawing);
                else if (listed.Count > SetReleaseRules.MaxDrawingLines)
                {
                    listed.Remove(drawing.Id);
                    break;
                }
                else
                    added.Add(drawing);
            }

            foreach (var drawing in added)
                AddRow(drawing, drawing.Name, drawing.Customer, drawing.Revision, quantity);
            if (added.Count > 0)
                BuildPlan();

            var text = added.Count switch
            {
                0 => "Nothing added.",
                1 => $"Added '{added[0].Name}'.",
                _ => $"Added {added.Count} drawings.",
            };
            if (already.Count > 0)
                text += $" Already listed (change the quantity there): {Names(already)}.";
            if (archived.Count > 0)
                text += $" Archived, restore before releasing: {Names(archived)}.";
            if (added.Count + already.Count + archived.Count < drawings.Count)
                text += $" A release may list at most {SetReleaseRules.MaxDrawingLines} drawings on their own.";
            statusLabel.Text = text;
            return text;
        }

        private static string Names(IEnumerable<DrawingLibraryRecord> drawings) =>
            string.Join(", ", drawings.Select(d => $"'{d.Name}'"));

        private void AddRow(object tag, string name, string customer, int revision, int quantity)
        {
            var row = new DataGridViewRow { Tag = tag };
            row.CreateCells(setGrid, tag is DrawingSetRecord ? "Set" : "Drawing", name, customer, revision,
                quantity.ToString(CultureInfo.CurrentCulture));
            setGrid.Rows.Add(row);
        }

        private void AddDrawingsButton_Click(object sender, EventArgs e)
        {
            if (!running && Result == null && setGrid.EndEdit())
                ShowAddDrawings();
        }

        private static int Kits(DataGridViewRow row) =>
            int.TryParse(Convert.ToString(row.Cells["kitsColumn"].Value, CultureInfo.CurrentCulture), NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var kits) ? kits : 1;

        private void Options_Changed(object sender, EventArgs e)
        {
            if (!running && Result == null)
                BuildPlan();
        }

        // The merge notes say what happens to a draft's layout, which this choice changes.
        private void NestAfterReleaseCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (running || Result != null)
                return;
            populating = true;
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                if (row.Tag is SetReleaseGroup group)
                    row.Cells[statusColumn.Index].Value = GroupNote(group);
            }
            populating = false;
        }

        private void SetGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex != kitsColumn.Index)
                return;
            var drawing = setGrid.Rows[e.RowIndex].Tag is DrawingLibraryRecord;
            var maximum = drawing ? SetReleaseRules.MaxDrawingQuantity : SetReleaseRules.MaxQuantity;
            var valid = int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture), NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var kits) && kits >= 1 && kits <= maximum;
            setGrid.Rows[e.RowIndex].ErrorText = valid ? ""
                : $"Enter a whole number from 1 to {maximum:N0}.";
            e.Cancel = !valid;
        }

        private void SetGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == kitsColumn.Index && !running && Result == null && !populating)
                BuildPlan();
        }

        private void SetGrid_SelectionChanged(object sender, EventArgs e) => UpdateCommands();

        private void RemoveSetButton_Click(object sender, EventArgs e)
        {
            if (setGrid.Rows.Count < 2 || setGrid.CurrentRow == null)
                return;
            setGrid.Rows.Remove(setGrid.CurrentRow);
            BuildPlan();
        }

        // Opening the empty list reads the first sets when the initial read failed or found none yet.
        private async void AddSetCombo_DropDown(object sender, EventArgs e)
        {
            if (addSetCombo.Items.Count == 0 && string.IsNullOrEmpty(addSetCombo.Text) && addSetCombo.Enabled)
                await SearchSetsAsync("", dropDown: false);
        }

        // Type-ahead: the set list is queried once typing pauses.
        private void AddSetCombo_TextUpdate(object sender, EventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private async void SearchTimer_Tick(object sender, EventArgs e)
        {
            searchTimer.Stop();
            await SearchSetsAsync(addSetCombo.Text);
        }

        private async void AddSetCombo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.Handled = e.SuppressKeyPress = true;
            await AddTypedSetAsync();
        }

        private async void AddSetButton_Click(object sender, EventArgs e) => await AddTypedSetAsync();

        /// <summary>
        /// Lists the first active sets by name whose name, customer or notes contain
        /// <paramref name="text"/> (all of them for an empty text); <paramref name="dropDown"/>
        /// opens the list when the box has focus, as typing does.
        /// </summary>
        internal async Task SearchSetsAsync(string text, bool dropDown = true)
        {
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            searchCancellation = new CancellationTokenSource();
            var token = searchCancellation.Token;
            DrawingSetPage page;
            try
            {
                page = await sets.QueryAsync(new DrawingSetQuery { Search = text ?? "", Limit = 20, Sort = DrawingSetSortField.Name, Descending = false }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                if (!IsDisposed)
                    statusLabel.Text = $"Could not search the sets: {ex.Message}";
                return;
            }

            if (IsDisposed || token.IsCancellationRequested)
                return;
            // Refilling the list resets the text, so the operator's typing is put back.
            var typed = addSetCombo.Text;
            var caret = addSetCombo.SelectionStart;
            addSetCombo.BeginUpdate();
            addSetCombo.Items.Clear();
            foreach (var found in page.Items)
                addSetCombo.Items.Add(new SetChoice(found));
            addSetCombo.EndUpdate();
            addSetCombo.Text = typed;
            addSetCombo.SelectionStart = System.Math.Min(caret, typed.Length);
            if (dropDown && addSetCombo.Focused && page.Items.Count > 0)
                addSetCombo.DroppedDown = true;
        }

        /// <summary>
        /// Adds the set picked in the list, or the one active set whose name is exactly what was
        /// typed, read with its members.
        /// </summary>
        internal async Task AddTypedSetAsync()
        {
            if (running || Result != null)
                return;
            var typed = (addSetCombo.Text ?? "").Trim();
            try
            {
                Guid id;
                if (addSetCombo.SelectedItem is SetChoice choice && choice.ToString() == addSetCombo.Text)
                {
                    id = choice.Set.Id;
                }
                else
                {
                    if (typed.Length == 0)
                        return;
                    var page = await sets.QueryAsync(new DrawingSetQuery { Search = typed, Limit = 50 });
                    var exact = page.Items.Where(s => string.Equals(s.Name.Trim(), typed, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (exact.Count != 1)
                    {
                        statusLabel.Text = exact.Count == 0
                            ? $"No active set is named '{typed}'. Pick one from the list."
                            : $"{exact.Count} sets are named '{typed}'. Pick one from the list.";
                        return;
                    }
                    id = exact[0].Id;
                }

                var read = await sets.GetAsync(id);
                if (IsDisposed)
                    return;
                if (read == null)
                {
                    statusLabel.Text = "That set no longer exists on the server.";
                    return;
                }
                if (AddLine(read, 1))
                    addSetCombo.Text = "";
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                if (!IsDisposed)
                    statusLabel.Text = $"Could not read the set: {ex.Message}";
            }
        }

        // Commit a checkbox click or a target choice at once so the summary follows it.
        private void GroupGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (groupGrid.IsCurrentCellDirty
                && (groupGrid.CurrentCell?.OwningColumn == includeColumn || groupGrid.CurrentCell?.OwningColumn == targetColumn))
                groupGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void GroupGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (populating || e.RowIndex < 0)
                return;
            var row = groupGrid.Rows[e.RowIndex];
            if (row.Tag is not SetReleaseGroup group)
                return;
            if (e.ColumnIndex == includeColumn.Index)
            {
                if (row.Cells[includeColumn.Index].Value is true)
                    excluded.Remove(group.Key);
                else
                    excluded.Add(group.Key);
            }
            else if (e.ColumnIndex == targetColumn.Index)
            {
                ChooseTarget(row, group, row.Cells[targetColumn.Index].Value as string);
            }
            UpdateCommands();
        }

        // A combo value that is not in a rebuilt list is reset rather than reported.
        private void GroupGrid_DataError(object sender, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

        /// <summary>Makes <paramref name="group"/> merge into the draft named by <paramref name="choice"/>, or a new nest.</summary>
        internal void ChooseTarget(DataGridViewRow row, SetReleaseGroup group, string choice)
        {
            var draft = drafts.TryGetValue(group.Key, out var candidates)
                ? candidates.FirstOrDefault(d => DraftChoice(d) == choice)
                : null;
            if (draft != null && IsNestOpen(draft.Id))
            {
                ShowMessage($"'{draft.Name}' is open in a tab here. Close it first, so no edits there are left behind by the merge.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                draft = null;
            }

            if (draft == null)
                targets.Remove(group.Key);
            else
                targets[group.Key] = draft;
            populating = true;
            row.Cells[targetColumn.Index].Value = draft == null ? NewNestChoice : DraftChoice(draft);
            row.Cells[statusColumn.Index].Value = GroupNote(group);
            populating = false;
        }

        private void GroupGrid_CurrentCellChanged(object sender, EventArgs e) => UpdateCommands();

        // A right-click selects the row under the pointer, so the menu acts on what was clicked.
        private void GroupGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && groupGrid.CurrentRow?.Index != e.RowIndex)
                groupGrid.CurrentCell = groupGrid.Rows[e.RowIndex].Cells[materialColumn.Index];
        }

        private async void GroupGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && groupGrid.Rows[e.RowIndex].Tag is SetReleaseGroup { CanRelease: false })
                await FixSelectedGroupAsync();
        }

        private async void FixDrawingsButton_Click(object sender, EventArgs e) => await FixSelectedGroupAsync();

        // One Edit Drawing item per drawing of the group, the ones it is refused for first.
        private void GroupMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            groupMenu.Items.Clear();
            if (libraryStore == null || groupGrid.CurrentRow?.Tag is not SetReleaseGroup group)
            {
                e.Cancel = true;
                return;
            }

            const int listed = 15;
            var items = group.RefusedItems.Concat(group.Items.Where(i => !group.RefusedItems.Contains(i))).ToList();
            var enabled = CanEditDrawings();
            foreach (var item in items.Take(listed))
            {
                var menuItem = new ToolStripMenuItem($"Edit Drawing '{item.Drawing.Name}'...") { Enabled = enabled };
                menuItem.Click += async (_, _) => await EditItemDrawingAsync(item);
                groupMenu.Items.Add(menuItem);
            }
            if (items.Count > listed)
            {
                groupMenu.Items.Add(new ToolStripMenuItem(
                    $"...and {items.Count - listed} more: edit them in the set or the Drawing Library")
                { Enabled = false });
            }
        }

        private bool CanEditDrawings() => libraryStore != null && !running && !editing && Result == null;

        /// <summary>
        /// Opens Edit Drawing, one after another, on each drawing the selected group is refused
        /// for; cancelling one stops. The plan is rebuilt after each save.
        /// </summary>
        internal async Task FixSelectedGroupAsync()
        {
            if (!CanEditDrawings() || groupGrid.CurrentRow?.Tag is not SetReleaseGroup { CanRelease: false } group)
                return;
            foreach (var item in group.RefusedItems.ToList())
            {
                if (!await EditItemDrawingAsync(item) || IsDisposed)
                    return;
            }
        }

        /// <summary>
        /// Reads the item's current library record and opens Edit Drawing on it; true when it
        /// was saved. What was read or saved replaces the drawing in the plan.
        /// </summary>
        internal async Task<bool> EditItemDrawingAsync(SetReleaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!CanEditDrawings())
                return false;

            var name = item.Drawing.Name;
            DrawingLibraryRecord current;
            editing = true;
            UpdateCommands();
            statusLabel.Text = $"Reading '{name}'...";
            try
            {
                current = await libraryStore.GetAsync(item.Drawing.Id);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!IsDisposed)
                {
                    statusLabel.Text = "";
                    ShowMessage($"Could not read '{name}': {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
            finally
            {
                editing = false;
                if (!IsDisposed)
                    UpdateCommands();
            }

            if (IsDisposed)
                return false;
            statusLabel.Text = "";
            if (current == null)
            {
                ShowMessage($"'{name}' is no longer in the library.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (current.Archived)
            {
                ShowMessage($"'{current.Name}' is archived in the library. Restore it there before editing it.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            var saved = EditDrawing(new DrawingLibraryEditSession(libraryStore, current, Environment.UserName));
            if (IsDisposed)
                return false;
            ApplyDrawing(saved ?? current);
            if (saved == null)
                return false;
            statusLabel.Text = $"Saved '{saved.Name}' revision {saved.Revision} to the library.";
            return true;
        }

        /// <summary>
        /// Shows a newer library record of a listed drawing, in the sets that list it (their
        /// joined members) and on its own line, and rebuilds the plan. Sets list drawings by id,
        /// so the release takes the drawing as it now is.
        /// </summary>
        internal void ApplyDrawing(DrawingLibraryRecord drawing)
        {
            ArgumentNullException.ThrowIfNull(drawing);
            var changed = false;
            foreach (DataGridViewRow row in setGrid.Rows)
            {
                if (row.Tag is DrawingSetRecord set)
                {
                    foreach (var member in set.Members ?? new List<DrawingSetMember>())
                    {
                        if (member.DrawingId == drawing.Id && (member.Drawing == null || member.Drawing.Revision < drawing.Revision))
                        {
                            member.Drawing = drawing;
                            changed = true;
                        }
                    }
                }
                else if (row.Tag is DrawingLibraryRecord listed && listed.Id == drawing.Id && listed.Revision < drawing.Revision)
                {
                    row.Tag = drawing;
                    row.Cells[setNameColumn.Index].Value = drawing.Name;
                    row.Cells[setCustomerColumn.Index].Value = drawing.Customer;
                    row.Cells[setRevisionColumn.Index].Value = drawing.Revision;
                    changed = true;
                }
            }

            if (changed)
                BuildPlan();
        }

        private async void ReleaseButton_Click(object sender, EventArgs e)
        {
            if (Result is { Release: null, ReleaseError.Length: > 0 })
                await RetryRecordAsync();
            else
                await ReleaseAsync();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            if (running)
            {
                cancellation?.Cancel();
                statusLabel.Text = "Stopping after the nest being saved...";
                closeButton.Enabled = false;
                return;
            }

            Close();
        }

        private void ReleaseSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Nests may be on their way to the server; stop between uploads, never mid-way.
            if (!running)
                return;
            e.Cancel = true;
            CloseButton_Click(sender, e);
        }

        /// <summary>The groups ticked for release, in plan order.</summary>
        internal IReadOnlyList<SetReleaseGroup> ChosenGroups() =>
            plan.Groups.Where(g => g.CanRelease && !excluded.Contains(g.Key)).ToList();

        /// <summary>The chosen groups that merge into a draft, with that draft.</summary>
        internal IReadOnlyDictionary<SetReleaseGroup, NestRecord> ChosenMerges() =>
            ChosenGroups().Where(g => targets.ContainsKey(g.Key)).ToDictionary(g => g, g => targets[g.Key]);

        /// <summary>
        /// Confirms, then makes or merges one Draft nest per ticked group, opens each in its own
        /// tab and records the release. Every group's outcome is shown in the grid.
        /// </summary>
        internal async Task ReleaseAsync()
        {
            if (running || Result != null || !setGrid.EndEdit() || !groupGrid.EndEdit())
                return;
            var chosen = ChosenGroups();
            if (chosen.Count == 0)
                return;
            var merges = ChosenMerges();
            if (merges.Values.FirstOrDefault(d => IsNestOpen(d.Id)) is { } open)
            {
                ShowMessage($"'{open.Name}' is open in a tab here. Close it, or choose a new nest for its group.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var created = chosen.Count - merges.Count;
            var text = $"Make {chosen.Count} Draft nest{(chosen.Count == 1 ? "" : "s")} from {DescribeLines()}?";
            var nestAfter = nestAfterReleaseCheckBox.Checked;
            if (merges.Count > 0)
                text += $" {created} new, {merges.Count} merged into existing drafts. A merged draft's layout is "
                    + (nestAfter ? "replaced" : "cleared for Auto Nest")
                    + "; Undo Release restores it.";
            text += nestAfter ? " Each is nested with Auto Nest and opens in a tab." : " Each opens in a tab.";
            if (ShowMessage(text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            running = true;
            cancellation = new CancellationTokenSource();
            closeButton.Text = "Cancel";
            UpdateCommands();
            session = new SetReleaseSession(sets, library, nests, serverUrl, ReadPackage, WriteNest, CreateNest, ReadNest,
                nestAfter ? Renest : null);
            var progress = new Progress<SetReleaseProgress>(ShowProgress);
            try
            {
                Result = await session.RunAsync(plan, chosen, Environment.UserName, notesBox.Text, progress, cancellation.Token, merges);
            }
            finally
            {
                running = false;
                if (!IsDisposed)
                {
                    closeButton.Text = "Close";
                    closeButton.Enabled = true;
                }
            }

            foreach (var done in Result.Created)
                openNest(done.Nest, done.Record);
            if (IsDisposed)
                return;
            ShowOutcome();
        }

        /// <summary>Posts the release again after every nest was saved but recording it failed.</summary>
        internal async Task RetryRecordAsync()
        {
            if (running || session == null || !session.HasPendingRelease)
                return;
            running = true;
            UpdateCommands();
            statusLabel.Text = "Recording the release...";
            try
            {
                var release = await session.RetryReleaseAsync();
                Result = Result.WithRelease(release);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException
                || ex is TaskCanceledException || ex is System.Text.Json.JsonException)
            {
                if (!IsDisposed)
                    ShowMessage($"The release is still not recorded: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                running = false;
            }

            if (!IsDisposed)
                ShowOutcome();
        }

        private void BuildPlan()
        {
            plan = SetReleasePlan.Build(Lines(), DrawingLines(), includeArchivedCheckBox.Checked);
            Text = (plan.Lines.Count + plan.Drawings.Count) switch
            {
                0 => "New Release",
                1 => $"Release '{(plan.Lines.Count == 1 ? plan.Lines[0].Set.Name : plan.Drawings[0].Drawing.Name)}'",
                _ => $"Release {DescribeLines()}",
            };
            populating = true;
            groupGrid.Rows.Clear();
            foreach (var group in plan.Groups)
            {
                var row = new DataGridViewRow { Tag = group };
                row.CreateCells(groupGrid,
                    group.CanRelease && !excluded.Contains(group.Key),
                    group.IsUnassigned ? "(none)" : group.MaterialName,
                    group.MaterialGrade,
                    group.IsUnassigned ? "" : group.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                    group.IsUnassigned ? "" : UnitsHelper.GetShortString(group.Units),
                    group.IsUnassigned ? "" : group.CutType.ToString(),
                    group.Items.Count,
                    group.TotalQuantity,
                    null,
                    GroupNote(group));
                // A refused group cannot be ticked.
                row.Cells[includeColumn.Index].ReadOnly = !group.CanRelease;
                groupGrid.Rows.Add(row);
                FillTargets(row, group);
            }
            populating = false;

            var skipped = plan.SkippedArchived;
            skippedLabel.Visible = skipped.Count > 0;
            skippedLabel.Text = skipped.Count == 0 ? "" :
                $"{skipped.Count} archived drawing{(skipped.Count == 1 ? " is" : "s are")} left out: "
                + string.Join(", ", skipped.Take(5).Select(m => $"'{m.Drawing!.Name}'"))
                + (skipped.Count > 5 ? $" and {skipped.Count - 5} more" : "")
                + ". Tick Include archived drawings to add them.";
            UpdateCommands();
            _ = LoadDraftsAsync();
        }

        // "set 'Kit A' ×2", "3 sets and 2 drawings", "drawing 'SYN-A' ×4".
        private string DescribeLines()
        {
            var parts = new List<string>();
            if (plan.Lines.Count == 1)
                parts.Add($"set '{plan.Lines[0].Set.Name}'" + (plan.Lines[0].Quantity == 1 ? "" : $" ×{plan.Lines[0].Quantity}"));
            else if (plan.Lines.Count > 1)
                parts.Add($"{plan.Lines.Count} sets");
            if (plan.Drawings.Count == 1)
                parts.Add($"drawing '{plan.Drawings[0].Drawing.Name}' ×{plan.Drawings[0].Quantity}");
            else if (plan.Drawings.Count > 1)
                parts.Add($"{plan.Drawings.Count} drawings");
            return string.Join(" and ", parts);
        }

        // The target list of one group: a new nest, then each draft it could merge into.
        private void FillTargets(DataGridViewRow row, SetReleaseGroup group)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[targetColumn.Index];
            cell.Items.Clear();
            cell.Items.Add(NewNestChoice);
            if (group.CanRelease && drafts.TryGetValue(group.Key, out var candidates))
            {
                foreach (var draft in candidates)
                    cell.Items.Add(DraftChoice(draft));
            }
            if (targets.TryGetValue(group.Key, out var target) && !cell.Items.Contains(DraftChoice(target)))
                targets.Remove(group.Key);
            cell.Value = targets.TryGetValue(group.Key, out target) ? DraftChoice(target) : NewNestChoice;
            cell.ReadOnly = !group.CanRelease;
        }

        /// <summary>Looks up, once per group, the Draft nests it could merge into, then fills the lists.</summary>
        internal async Task LoadDraftsAsync()
        {
            if (loadingDrafts || plan == null)
                return;
            loadingDrafts = true;
            try
            {
                while (plan.Groups.FirstOrDefault(g => g.CanRelease && !drafts.ContainsKey(g.Key)) is { } group)
                {
                    try
                    {
                        drafts[group.Key] = await SetReleasePlan.FindDraftsAsync(nests, group);
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                        or System.Text.Json.JsonException or NotSupportedException)
                    {
                        drafts[group.Key] = Array.Empty<NestRecord>();
                        if (!IsDisposed)
                            statusLabel.Text = $"Could not list the draft nests to merge into: {ex.Message}";
                    }
                    if (IsDisposed)
                        return;
                }
            }
            finally
            {
                loadingDrafts = false;
            }

            if (IsDisposed || running || Result != null)
                return;
            populating = true;
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                if (row.Tag is SetReleaseGroup group)
                    FillTargets(row, group);
            }
            populating = false;
        }

        private string GroupNote(SetReleaseGroup group)
        {
            if (!group.CanRelease)
                return group.Refusal;
            var notes = group.Warnings.ToList();
            if (targets.TryGetValue(group.Key, out var draft))
                notes.Insert(0, $"Replaces the layout of '{draft.Name}' ({draft.PlateCount} sheet"
                    + $"{(draft.PlateCount == 1 ? "" : "s")}) "
                    + (nestAfterReleaseCheckBox.Checked ? "and nests again" : "and leaves it for Auto Nest")
                    + "; Undo Release restores it.");
            return string.Join(" ", notes);
        }

        private static string DraftChoice(NestRecord draft) =>
            $"Merge into {draft.Name} ({draft.PlateCount} sheet{(draft.PlateCount == 1 ? "" : "s")}, saved "
            + $"{draft.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)})";

        private void ShowProgress(SetReleaseProgress progress)
        {
            // Progress is posted, so a report can arrive after the run ended; the outcome wins.
            if (IsDisposed || !running)
                return;
            statusLabel.Text = progress.Stage switch
            {
                SetReleaseStage.Preparing => $"Preparing nest {progress.Index + 1} of {progress.Count}: {Describe(progress.Group)}...",
                SetReleaseStage.Nesting => $"Nesting nest {progress.Index + 1} of {progress.Count}: {Describe(progress.Group)}...",
                SetReleaseStage.Uploading => $"Saving nest {progress.Index + 1} of {progress.Count}: {Describe(progress.Group)}...",
                _ => "Recording the release...",
            };
        }

        private void ShowOutcome()
        {
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                var outcome = Result.Groups.FirstOrDefault(g => ReferenceEquals(g.Group, row.Tag));
                if (outcome == null)
                    continue;
                var notes = outcome.Warnings.Count == 0 ? "" : " — " + string.Join(" ", outcome.Warnings);
                row.Cells[statusColumn.Index].Value = outcome.Outcome switch
                {
                    SetReleaseGroupOutcome.Created when outcome.MergedInto != null => $"Merged into {outcome.Record!.Name}" + notes,
                    SetReleaseGroupOutcome.Created => $"Created {outcome.Record!.Name}" + notes,
                    SetReleaseGroupOutcome.Refused => "Refused: " + outcome.Error,
                    SetReleaseGroupOutcome.Failed => "Failed: " + outcome.Error,
                    _ => "Not started",
                };
            }

            var done = Result.Created.Count;
            var nestsText = $"{done} nest{(done == 1 ? "" : "s")}";
            if (Result.Release != null)
                statusLabel.Text = $"Done: {nestsText} saved and opened.";
            else if (Result.ReleaseError.Length > 0)
                statusLabel.Text = Result.ReleaseError + " Choose Try Again.";
            else if (Result.Canceled)
                statusLabel.Text = $"Canceled: {nestsText} saved, release not recorded.";
            else
                statusLabel.Text = done == 0
                    ? "Nothing saved. See Status."
                    : $"Stopped: {nestsText} saved, release not recorded. See Status.";
            UpdateCommands();
        }

        private void UpdateCommands()
        {
            var chosen = plan == null ? Array.Empty<SetReleaseGroup>() : ChosenGroups();
            var editable = !running && Result == null;
            setGrid.ReadOnly = !editable;
            addSetCombo.Enabled = editable;
            addSetButton.Enabled = editable;
            addDrawingsButton.Enabled = editable;
            removeSetButton.Enabled = editable && setGrid.Rows.Count > 1 && setGrid.CurrentRow != null;
            includeArchivedCheckBox.Enabled = editable;
            nestAfterReleaseCheckBox.Enabled = editable;
            notesBox.ReadOnly = !editable;
            includeColumn.ReadOnly = !editable;
            targetColumn.ReadOnly = !editable;
            if (editable)
            {
                foreach (DataGridViewRow row in groupGrid.Rows)
                {
                    var refused = row.Tag is SetReleaseGroup { CanRelease: false };
                    row.Cells[includeColumn.Index].ReadOnly = refused;
                    row.Cells[targetColumn.Index].ReadOnly = refused;
                }
            }

            fixDrawingsButton.Enabled = CanEditDrawings() && groupGrid.CurrentRow?.Tag is SetReleaseGroup { CanRelease: false };

            var retry = Result is { Release: null, ReleaseError.Length: > 0 };
            releaseButton.Text = retry ? "Try Again" : "Make Nests...";
            releaseButton.Enabled = !running && (retry || (editable && chosen.Count > 0));
            var parts = chosen.Sum(g => (long)g.TotalQuantity);
            var merged = chosen.Count(g => targets.ContainsKey(g.Key));
            summaryLabel.Text = plan == null || plan.Groups.Count == 0
                ? "No nests to make."
                : $"{chosen.Count} of {plan.Groups.Count} material{(plan.Groups.Count == 1 ? "" : "s")} chosen: "
                    + $"{chosen.Count - merged} new Draft nest{(chosen.Count - merged == 1 ? "" : "s")}"
                    + (merged == 0 ? "" : $", {merged} merge{(merged == 1 ? "" : "s")}")
                    + $", {parts:N0} part{(parts == 1 ? "" : "s")}";
        }

        private static string Describe(SetReleaseGroup group) =>
            group == null ? "" : string.Join(" ", new[]
            {
                group.MaterialName,
                group.MaterialGrade,
                $"{group.Thickness.ToString("0.####", CultureInfo.CurrentCulture)} {UnitsHelper.GetShortString(group.Units)}",
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // The nest is not shown anywhere yet, so Auto Nest runs off the UI thread on it alone.
        private static Task<SetReleaseRenest> RenestAsync(Nest nest, CancellationToken token)
        {
            var engineName = EngineSelection.EngineName;
            return Task.Run(() =>
            {
                var renested = NestRenest.Run(nest, engineName, AutoNestForm.DefaultMinRemnantSize, token: token);
                return new SetReleaseRenest(renested.Nested, renested.Note);
            }, token);
        }

        private static LibraryPackage ReadPackage(byte[] bytes)
        {
            var contents = DrawingPackage.Read(bytes);
            return new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings);
        }

        private static Nest ReadNest(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new NestReader(stream) { BestFitStore = BestFitCacheStore.Shared }.Read();
        }

        private static byte[] WriteNest(Nest nest)
        {
            using var stream = new MemoryStream();
            new NestWriter(nest) { BestFitStore = BestFitCacheStore.Shared }.Write(stream);
            return stream.ToArray();
        }

        /// <summary>A set in the type-ahead list, shown by name and customer.</summary>
        private sealed record SetChoice(DrawingSetRecord Set)
        {
            public override string ToString() =>
                string.IsNullOrWhiteSpace(Set.Customer) ? Set.Name : $"{Set.Name} ({Set.Customer})";
        }
    }
}

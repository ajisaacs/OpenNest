using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.IO;

namespace OpenNest.Controls
{
    /// <summary>
    /// The release being built, beside the Library and Sets lists of
    /// <see cref="DrawingSetsForm"/>: a double-click there adds a set at one kit or a library
    /// drawing on its own at the entered quantity, and adding one already listed adds to its
    /// quantity. The lines are edited in place (quantity, ✕ to remove) with the release's
    /// customer and notes, and "Nests it will make" previews live how they split into one nest
    /// per material group (<see cref="SetReleasePlan"/>), with a tick per group and its target.
    /// A group merges into a Draft nest of the same material by default: the only one, or the
    /// most recently saved of several (the row names it and offers the others or a new nest);
    /// with none it makes a new Draft nest. A draft open in a tab of this desktop is never
    /// merged into: its row says so and Make Nests waits until it is closed or another target
    /// is chosen. A group refused for its drawings (no material or thickness, or two with one
    /// name) shows why at once; Fix Drawings, a double-click or the group's right-click menu
    /// opens Edit Drawing and the plan is rebuilt with what was saved. Make Nests is the only
    /// step that writes: <see cref="SetReleaseSession"/> builds the nests, nests each with Auto
    /// Nest when Nest after release is ticked (remembered per user; live in the main window
    /// through <see cref="NestingHost"/>, else headless), saves them, opens each in its own
    /// tab and records the release; each group's outcome is then shown.
    /// </summary>
    public partial class ReleasePanel : UserControl
    {
        internal const string NewNestChoice = "New Draft nest";

        private IDrawingSetStore sets;
        private IDrawingLibrary library;
        // Edit Drawing needs a writable library; null hides Fix Drawings.
        private IDrawingLibraryStore libraryStore;
        private INestRepository nests;
        private string serverUrl = "";
        private Action<Nest, NestRecord> openNest = (_, _) => { };
        private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
        // The draft the operator picked for a group, by group key; without one the group takes the default.
        private readonly Dictionary<string, NestRecord> targets = new(StringComparer.Ordinal);
        // Groups the operator chose to make a new nest for although a draft matches.
        private readonly HashSet<string> newNests = new(StringComparer.Ordinal);
        // The drafts each group could merge into, by group key; absent while still loading.
        private readonly Dictionary<string, IReadOnlyList<NestRecord>> drafts = new(StringComparer.Ordinal);
        // Group keys whose draft lookup failed, with the reason.
        private readonly Dictionary<string, string> lookupErrors = new(StringComparer.Ordinal);
        private SetReleasePlan plan;
        private SetReleaseSession session;
        private CancellationTokenSource cancellation;
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

        /// <summary>Shows Edit Drawing and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingLibraryEditSession, DrawingLibraryRecord> EditDrawing { get; set; }

        /// <summary>
        /// True when a server nest is open in a tab of this desktop. Such a draft is not merged
        /// into: the tab could hold edits the merge would leave behind.
        /// </summary>
        public Func<Guid, bool> IsNestOpen { get; set; } = _ => false;

        /// <summary>Raised once Make Nests has opened the nests it made, so the host can show them.</summary>
        public event EventHandler NestsOpened;

        /// <summary>Raised when Make Nests hands its nesting to <see cref="NestingHost"/>.</summary>
        public event EventHandler NestingStarted;

        /// <summary>Raised when Make Nests has finished, whatever its outcome (see <see cref="NeedsAttention"/>).</summary>
        public event EventHandler MakeNestsFinished;

        /// <summary>
        /// Nests each released nest where the operator can watch it (the main window, live, in
        /// the nest's own tab) when Nest after release is ticked; null nests headless here.
        /// </summary>
        public IReleaseNestingHost NestingHost { get; set; }

        /// <summary>The finished release, once Make Nests has run; otherwise null.</summary>
        public SetReleaseResult Result { get; private set; }

        /// <summary>True while Make Nests is making nests or recording the release.</summary>
        public bool IsRunning => running;

        public ReleasePanel()
        {
            InitializeComponent();
            GridStyle.Apply(lineGrid, sizeColumns: false);
            GridStyle.Apply(groupGrid, sizeColumns: false);
            // A refusal or outcome wraps rather than being cut off.
            groupGrid.RowTemplate.MinimumHeight = groupGrid.RowTemplate.Height;
            groupGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            // Only the operator's own click is remembered, so a test or a host setting it is not.
            nestAfterReleaseCheckBox.Checked = Properties.Settings.Default.ReleaseNestAfter;
            nestAfterReleaseCheckBox.Click += NestAfterReleaseCheckBox_Click;
            ShowMessage = (text, buttons, icon) => MessageBox.Show(FindForm(), text, "Release", buttons, icon);
            CreateNest = (group, _) => NewNestFactory.CreateAsync(FindForm(), group.Units);
            EditDrawing = edit =>
            {
                using var form = new EditLibraryDrawingForm(edit);
                return form.ShowDialog(FindForm()) == DialogResult.OK ? form.Saved : null;
            };
            BuildPlan();
        }

        /// <param name="openNest">Opens a created or merged nest in its own tab, bound to its server record.</param>
        public void Initialize(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
            Action<Nest, NestRecord> openNest)
        {
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            libraryStore = library as IDrawingLibraryStore;
            this.nests = nests ?? throw new ArgumentNullException(nameof(nests));
            this.serverUrl = serverUrl ?? "";
            this.openNest = openNest ?? throw new ArgumentNullException(nameof(openNest));
            UpdateCommands();
        }

        /// <summary>True once <see cref="Initialize"/> has run.</summary>
        public bool IsInitialized => nests != null;

        /// <summary>The sets listed, in order, with their kits.</summary>
        internal IReadOnlyList<SetReleasePlanLine> Lines() =>
            lineGrid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is DrawingSetRecord)
                .Select(r => new SetReleasePlanLine((DrawingSetRecord)r.Tag, Quantity(r)))
                .ToList();

        /// <summary>The drawings listed on their own, in order, with their quantities.</summary>
        internal IReadOnlyList<SetReleaseDrawingLine> DrawingLines() =>
            lineGrid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is DrawingLibraryRecord)
                .Select(r => new SetReleaseDrawingLine((DrawingLibraryRecord)r.Tag, Quantity(r)))
                .ToList();

        /// <summary>True when nothing is listed.</summary>
        public bool IsEmpty => lineGrid.Rows.Count == 0;

        /// <summary>
        /// Adds a set read from the server (members joined) at <paramref name="kits"/>, or that
        /// many more kits when it is already listed. False, with the reason shown, when it is
        /// archived or nothing can be added now. A finished release is cleared first, so the
        /// set starts the next one.
        /// </summary>
        public bool AddSet(DrawingSetRecord set, int kits = 1)
        {
            ArgumentNullException.ThrowIfNull(set);
            if (!PrepareToAdd())
                return false;
            if (set.Archived)
            {
                statusLabel.Text = $"'{set.Name}' is archived. Restore it before releasing it.";
                return false;
            }

            var listed = lineGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is DrawingSetRecord s && s.Id == set.Id);
            if (listed != null)
            {
                var now = System.Math.Min(Quantity(listed) + kits, SetReleaseRules.MaxQuantity);
                SetQuantity(listed, now);
                listed.Selected = true;
                statusLabel.Text = $"'{set.Name}' is now ×{now} {Unit(set, now)}.";
            }
            else
            {
                AddRow(set, set.Name, System.Math.Clamp(kits, 1, SetReleaseRules.MaxQuantity));
                statusLabel.Text = $"Added set '{set.Name}'.";
            }
            BuildPlan();
            return true;
        }

        /// <summary>
        /// Adds library drawings on their own at <paramref name="quantity"/> each, adding that
        /// many more to those already listed and skipping (and naming) archived ones, and
        /// returns what happened for the operator.
        /// </summary>
        public string AddDrawings(IReadOnlyList<DrawingLibraryRecord> drawings, int quantity)
        {
            ArgumentNullException.ThrowIfNull(drawings);
            if (!PrepareToAdd())
                return statusLabel.Text;
            var added = new List<DrawingLibraryRecord>();
            var more = new List<DrawingLibraryRecord>();
            var archived = new List<DrawingLibraryRecord>();
            var full = false;
            foreach (var drawing in drawings)
            {
                if (drawing.Archived)
                {
                    archived.Add(drawing);
                    continue;
                }
                var listed = lineGrid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(r => r.Tag is DrawingLibraryRecord d && d.Id == drawing.Id);
                if (listed != null)
                {
                    SetQuantity(listed, System.Math.Min(Quantity(listed) + quantity, SetReleaseRules.MaxDrawingQuantity));
                    more.Add(drawing);
                }
                else if (DrawingLines().Count >= SetReleaseRules.MaxDrawingLines)
                {
                    full = true;
                }
                else
                {
                    AddRow(drawing, drawing.Name, System.Math.Clamp(quantity, 1, SetReleaseRules.MaxDrawingQuantity));
                    added.Add(drawing);
                }
            }
            if (added.Count + more.Count > 0)
                BuildPlan();

            var text = (added.Count, more.Count) switch
            {
                (0, 0) => "Nothing added.",
                (1, 0) => $"Added '{added[0].Name}' ×{quantity}.",
                (_, 0) => $"Added {added.Count} drawings.",
                (0, 1) => $"'{more[0].Name}' is now ×{DrawingLines().Single(l => l.Drawing.Id == more[0].Id).Quantity} pcs.",
                _ => $"Added {added.Count} drawing{(added.Count == 1 ? "" : "s")} and {quantity} more of {more.Count} listed.",
            };
            if (archived.Count > 0)
                text += $" Archived, restore before releasing: {Names(archived)}.";
            if (full)
                text += $" A release may list at most {SetReleaseRules.MaxDrawingLines} drawings on their own.";
            statusLabel.Text = text;
            return text;
        }

        private static string Names(IEnumerable<DrawingLibraryRecord> drawings) =>
            string.Join(", ", drawings.Select(d => $"'{d.Name}'"));

        // A finished release makes way for the next; one waiting to be recorded, or running, does not.
        private bool PrepareToAdd()
        {
            if (running)
            {
                statusLabel.Text = "Wait for Make Nests to finish before adding more.";
                return false;
            }
            if (Result == null)
                return true;
            if (session?.HasPendingRelease == true)
            {
                statusLabel.Text = "The nests are saved but the release is not recorded. Try Again records it; Clear leaves it unrecorded.";
                return false;
            }
            Clear();
            return true;
        }

        private void AddRow(object tag, string name, int quantity)
        {
            var row = new DataGridViewRow { Tag = tag };
            row.CreateCells(lineGrid, name, quantity.ToString(CultureInfo.CurrentCulture), Unit(tag, quantity));
            row.Cells[lineNameColumn.Index].ToolTipText = tag switch
            {
                DrawingSetRecord set => $"Set '{set.Name}', revision {set.Revision}"
                    + (string.IsNullOrWhiteSpace(set.Customer) ? "" : $", {set.Customer}"),
                DrawingLibraryRecord drawing => $"Drawing '{drawing.Name}' on its own, revision {drawing.Revision}"
                    + (string.IsNullOrWhiteSpace(drawing.Customer) ? "" : $", {drawing.Customer}"),
                _ => "",
            };
            lineGrid.Rows.Add(row);
        }

        private void SetQuantity(DataGridViewRow row, int quantity)
        {
            populating = true;
            row.Cells[lineQuantityColumn.Index].Value = quantity.ToString(CultureInfo.CurrentCulture);
            row.Cells[lineUnitColumn.Index].Value = Unit(row.Tag, quantity);
            populating = false;
        }

        // "kits" for a set, "pcs" for a drawing on its own.
        private static string Unit(object tag, int quantity) =>
            tag is DrawingSetRecord ? (quantity == 1 ? "kit" : "kits") : (quantity == 1 ? "pc" : "pcs");

        private static int Quantity(DataGridViewRow row) =>
            int.TryParse(Convert.ToString(row.Cells["lineQuantityColumn"].Value, CultureInfo.CurrentCulture), NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var quantity) ? quantity : 1;

        private void LineGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex != lineQuantityColumn.Index || e.RowIndex < 0)
                return;
            var drawing = lineGrid.Rows[e.RowIndex].Tag is DrawingLibraryRecord;
            var maximum = drawing ? SetReleaseRules.MaxDrawingQuantity : SetReleaseRules.MaxQuantity;
            var valid = int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture), NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var quantity) && quantity >= 1 && quantity <= maximum;
            lineGrid.Rows[e.RowIndex].ErrorText = valid ? "" : $"Enter a whole number from 1 to {maximum:N0}.";
            e.Cancel = !valid;
        }

        private void LineGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (populating || e.RowIndex < 0 || e.ColumnIndex != lineQuantityColumn.Index || running || Result != null)
                return;
            var row = lineGrid.Rows[e.RowIndex];
            populating = true;
            row.Cells[lineUnitColumn.Index].Value = Unit(row.Tag, Quantity(row));
            populating = false;
            BuildPlan();
        }

        private void LineGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == lineRemoveColumn.Index)
                RemoveLine(lineGrid.Rows[e.RowIndex]);
        }

        private void LineGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !lineGrid.IsCurrentCellInEditMode && lineGrid.CurrentRow != null)
            {
                RemoveLine(lineGrid.CurrentRow);
                e.Handled = true;
            }
        }

        /// <summary>Takes a line out of the release in progress.</summary>
        internal void RemoveLine(DataGridViewRow row)
        {
            if (running || Result != null || row == null)
                return;
            lineGrid.CancelEdit();
            lineGrid.Rows.Remove(row);
            statusLabel.Text = "";
            BuildPlan();
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            if (running)
            {
                cancellation?.Cancel();
                statusLabel.Text = "Stopping after the nest being saved...";
                clearButton.Enabled = false;
                return;
            }
            if (ConfirmDiscard())
                Clear();
        }

        /// <summary>
        /// True when nothing would be lost by dropping this release, or the operator agreed to
        /// leave saved nests whose release is not recorded.
        /// </summary>
        public bool ConfirmDiscard() =>
            !running && (session?.HasPendingRelease != true
                || ShowMessage("The nests are saved but the release is not recorded, so it can't be undone as one. Leave it unrecorded?",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK);

        /// <summary>Empties the panel for a new release.</summary>
        public void Clear()
        {
            if (running)
                return;
            lineGrid.Rows.Clear();
            notesBox.Text = "";
            includeArchivedCheckBox.Checked = false;
            excluded.Clear();
            targets.Clear();
            newNests.Clear();
            // A release may have made new drafts, so the next one looks again.
            drafts.Clear();
            lookupErrors.Clear();
            Result = null;
            session = null;
            statusLabel.Text = "";
            BuildPlan();
        }

        /// <summary>The release in progress, to keep while the window is closed; null when there is none.</summary>
        public ReleaseDraft TakeDraft() =>
            running || Result != null || IsEmpty ? null
                : new ReleaseDraft(Lines(), DrawingLines(), notesBox.Text, includeArchivedCheckBox.Checked);

        /// <summary>Lists a release kept by <see cref="TakeDraft"/> again; its drafts to merge into are looked up afresh.</summary>
        public void Restore(ReleaseDraft draft)
        {
            if (draft == null || running)
                return;
            Clear();
            foreach (var line in draft.Sets)
                AddRow(line.Set, line.Set.Name, line.Quantity);
            foreach (var line in draft.Drawings)
                AddRow(line.Drawing, line.Drawing.Name, line.Quantity);
            notesBox.Text = draft.Notes ?? "";
            includeArchivedCheckBox.Checked = draft.IncludeArchived;
            BuildPlan();
        }

        /// <summary>
        /// Re-reads which nests are open in tabs, as the operator may have closed a draft there
        /// since; called when the window is activated.
        /// </summary>
        public void RefreshOpenNests()
        {
            if (running || Result != null || plan == null)
                return;
            UpdateNotes();
            UpdateCommands();
        }

        private void IncludeArchivedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!running && Result == null && !populating)
                BuildPlan();
        }

        // The notes say what a merge does to the draft's layout, which this choice changes.
        private void NestAfterReleaseCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (running || Result != null)
                return;
            UpdateNotes();
        }

        private void NestAfterReleaseCheckBox_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.ReleaseNestAfter = nestAfterReleaseCheckBox.Checked;
            Properties.Settings.Default.Save();
        }

        private void ReleasePanel_Resize(object sender, EventArgs e)
        {
            // Long notes wrap to the panel's width rather than widening it.
            var width = System.Math.Max(100, layoutTable.ClientSize.Width - 8);
            foreach (var label in new[] { hintLabel, previewLabel, statusLabel })
                label.MaximumSize = new System.Drawing.Size(width, 0);
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
                UpdateNotes();
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
                ? candidates.FirstOrDefault(d => DraftChoice(d, candidates) == choice)
                : null;
            if (draft == null)
            {
                targets.Remove(group.Key);
                newNests.Add(group.Key);
            }
            else
            {
                targets[group.Key] = draft;
                newNests.Remove(group.Key);
            }
            populating = true;
            row.Cells[targetColumn.Index].Value = TargetChoice(group);
            row.Cells[statusColumn.Index].Value = GroupNote(group);
            populating = false;
            UpdateCommands();
        }

        /// <summary>
        /// The draft <paramref name="group"/> merges into: the one the operator picked, else the
        /// most recently saved Draft nest of its material; null for a new nest or while its
        /// drafts are still being looked up.
        /// </summary>
        internal NestRecord TargetOf(SetReleaseGroup group)
        {
            if (!group.CanRelease || newNests.Contains(group.Key)
                || !drafts.TryGetValue(group.Key, out var candidates) || candidates.Count == 0)
                return null;
            if (targets.TryGetValue(group.Key, out var picked) && candidates.Any(d => d.Id == picked.Id))
                return picked;
            return candidates.OrderByDescending(d => d.SavedAt).First();
        }

        private void GroupGrid_CurrentCellChanged(object sender, EventArgs e) => UpdateCommands();

        // A right-click selects the row under the pointer, so the menu acts on what was clicked.
        private void GroupGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && groupGrid.CurrentRow?.Index != e.RowIndex)
                groupGrid.CurrentCell = groupGrid.Rows[e.RowIndex].Cells[groupColumn.Index];
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

        // The selected refused group, else the first one.
        private DataGridViewRow RefusedRow() =>
            groupGrid.CurrentRow?.Tag is SetReleaseGroup { CanRelease: false } ? groupGrid.CurrentRow
                : groupGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is SetReleaseGroup { CanRelease: false });

        private bool CanEditDrawings() => libraryStore != null && !running && !editing && Result == null;

        /// <summary>
        /// Opens Edit Drawing, one after another, on each drawing the selected group is refused
        /// for, or the first refused group's when a releasable one is selected; cancelling one
        /// stops. The plan is rebuilt after each save.
        /// </summary>
        internal async Task FixSelectedGroupAsync()
        {
            if (!CanEditDrawings() || RefusedRow() is not { Tag: SetReleaseGroup group } row)
                return;
            if (groupGrid.CurrentRow != row)
                groupGrid.CurrentCell = row.Cells[groupColumn.Index];
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
            foreach (DataGridViewRow row in lineGrid.Rows)
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
                    row.Cells[lineNameColumn.Index].Value = drawing.Name;
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

        /// <summary>The groups ticked for release, in plan order.</summary>
        internal IReadOnlyList<SetReleaseGroup> ChosenGroups() =>
            plan.Groups.Where(g => g.CanRelease && !excluded.Contains(g.Key)).ToList();

        /// <summary>The chosen groups that merge into a draft, with that draft.</summary>
        internal IReadOnlyDictionary<SetReleaseGroup, NestRecord> ChosenMerges() =>
            ChosenGroups().Select(g => (Group: g, Draft: TargetOf(g))).Where(m => m.Draft != null)
                .ToDictionary(m => m.Group, m => m.Draft);

        // A chosen group whose draft is open in a tab; Make Nests waits for it.
        private NestRecord OpenTarget() => ChosenMerges().Values.FirstOrDefault(d => IsNestOpen(d.Id));

        // Every chosen group knows whether a draft matches it, so none becomes a new nest by accident.
        private bool DraftsKnown(IReadOnlyList<SetReleaseGroup> chosen) => chosen.All(g => drafts.ContainsKey(g.Key));

        /// <summary>
        /// Makes or merges one Draft nest per ticked group, opens each in its own tab and records
        /// the release. Every group's outcome is shown in the grid.
        /// </summary>
        internal async Task ReleaseAsync()
        {
            if (!IsInitialized || running || Result != null || !lineGrid.EndEdit() || !groupGrid.EndEdit())
                return;
            var chosen = ChosenGroups();
            if (chosen.Count == 0 || !DraftsKnown(chosen))
                return;
            if (OpenTarget() is { } open)
            {
                UpdateNotes();
                UpdateCommands();
                ShowMessage($"'{open.Name}' is open in a tab. Save and close it to merge into it, or choose {NewNestChoice} for its material.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var merges = ChosenMerges();
            var nestAfter = nestAfterReleaseCheckBox.Checked;
            var live = nestAfter ? NestingHost : null;
            running = true;
            cancellation = new CancellationTokenSource();
            UpdateCommands();
            session = new SetReleaseSession(sets, library, nests, serverUrl, ReadPackage, WriteNest, CreateNest, ReadNest,
                !nestAfter ? null : live == null ? Renest : LiveRenest(live));
            var progress = new Progress<SetReleaseProgress>(ShowProgress);
            if (live != null)
            {
                live.Begin();
                NestingStarted?.Invoke(this, EventArgs.Empty);
            }
            try
            {
                Result = await session.RunAsync(plan, chosen, Environment.UserName, notesBox.Text, progress, cancellation.Token, merges);
                // A nest nested live is already in its tab, which this binds to its saved record.
                foreach (var done in Result.Created)
                    openNest(done.Nest, done.Record);
            }
            finally
            {
                running = false;
                live?.End(Result);
            }

            if (!IsDisposed)
            {
                ShowOutcome();
                if (Result.Created.Count > 0)
                    NestsOpened?.Invoke(this, EventArgs.Empty);
            }
            MakeNestsFinished?.Invoke(this, EventArgs.Empty);
        }

        // The host's Auto Nest stops the whole release, as Stop here does.
        private Func<Nest, CancellationToken, Task<SetReleaseRenest>> LiveRenest(IReleaseNestingHost live) =>
            async (nest, token) =>
            {
                try
                {
                    return await live.RenestAsync(nest, token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    cancellation.Cancel();
                    throw;
                }
            };

        /// <summary>
        /// True when the last Make Nests needs the operator here: a group failed or was
        /// refused, or the release was not recorded.
        /// </summary>
        public bool NeedsAttention =>
            Result != null && (Result.Release == null
                || Result.Groups.Any(g => g.Outcome is SetReleaseGroupOutcome.Failed or SetReleaseGroupOutcome.Refused));

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
            headingLabel.Text = Heading();
            customerValue.Text = CustomerText();
            populating = true;
            groupGrid.Rows.Clear();
            foreach (var group in plan.Groups)
            {
                var row = new DataGridViewRow { Tag = group };
                row.CreateCells(groupGrid,
                    group.CanRelease && !excluded.Contains(group.Key),
                    GroupText(group),
                    group.TotalQuantity,
                    null,
                    "");
                row.Cells[groupColumn.Index].ToolTipText = string.Join(Environment.NewLine,
                    group.Items.Take(20).Select(i => $"{i.Drawing.Name} ×{i.Quantity}"))
                    + (group.Items.Count > 20 ? $"{Environment.NewLine}...and {group.Items.Count - 20} more" : "");
                groupGrid.Rows.Add(row);
                FillTargets(row, group);
                row.Cells[statusColumn.Index].Value = GroupNote(group);
            }
            populating = false;
            UpdateCommands();
            _ = LoadDraftsAsync();
        }

        // "Release set 'Kit A' ×2", "Release 'Kit A' ×2, 'Kit B' ×1 and 'SYN-A' ×4".
        private string Heading()
        {
            if (plan.Lines.Count + plan.Drawings.Count == 1)
                return plan.Lines.Count == 1
                    ? $"Release set '{plan.Lines[0].Set.Name}' ×{plan.Lines[0].Quantity}"
                    : $"Release drawing '{plan.Drawings[0].Drawing.Name}' ×{plan.Drawings[0].Quantity}";
            const int named = 3;
            var lines = plan.Lines.Select(l => $"'{l.Set.Name}' ×{l.Quantity}")
                .Concat(plan.Drawings.Select(d => $"'{d.Drawing.Name}' ×{d.Quantity}"))
                .ToList();
            if (lines.Count == 0)
                return "Release";
            if (lines.Count <= named + 1)
                return $"Release {string.Join(", ", lines.Take(lines.Count - 1))} and {lines[^1]}";
            return $"Release {string.Join(", ", lines.Take(named))} and {lines.Count - named} more";
        }

        // The release takes the one customer every line names; several leave it blank.
        private string CustomerText()
        {
            if (plan.Lines.Count + plan.Drawings.Count == 0)
                return "—";
            if (plan.Customer.Length > 0)
                return plan.Customer;
            var named = plan.Lines.Select(l => l.Set.Customer).Concat(plan.Drawings.Select(d => d.Drawing.Customer))
                .Select(c => (c ?? "").Trim()).Distinct(StringComparer.Ordinal).Count();
            return named > 1 ? "Several (left blank on the release)" : "—";
        }

        // The target list of one group: each draft it could merge into, newest first, then a new nest.
        private void FillTargets(DataGridViewRow row, SetReleaseGroup group)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[targetColumn.Index];
            cell.Items.Clear();
            var choice = TargetChoice(group);
            if (group.CanRelease && drafts.TryGetValue(group.Key, out var candidates))
            {
                foreach (var draft in candidates.OrderByDescending(d => d.SavedAt))
                    cell.Items.Add(DraftChoice(draft, candidates));
                cell.Items.Add(NewNestChoice);
            }
            else if (choice.Length > 0)
            {
                cell.Items.Add(choice);
            }
            cell.Value = choice;
            // Nothing to choose until the drafts are known, or with only a new nest to make.
            cell.ReadOnly = !group.CanRelease || cell.Items.Count < 2;
            cell.DisplayStyle = cell.ReadOnly ? DataGridViewComboBoxDisplayStyle.Nothing : DataGridViewComboBoxDisplayStyle.DropDownButton;
        }

        // The target cell's text: the draft, a new nest, or blank while looking or refused.
        private string TargetChoice(SetReleaseGroup group)
        {
            if (!group.CanRelease)
                return "";
            if (!drafts.TryGetValue(group.Key, out var candidates))
                return "Looking...";
            var draft = TargetOf(group);
            return draft == null ? NewNestChoice : DraftChoice(draft, candidates);
        }

        /// <summary>Looks up, once per group, the Draft nests it could merge into, then fills the lists.</summary>
        internal async Task LoadDraftsAsync()
        {
            if (loadingDrafts || plan == null || nests == null)
                return;
            loadingDrafts = true;
            try
            {
                while (plan.Groups.FirstOrDefault(g => g.CanRelease && !drafts.ContainsKey(g.Key)) is { } group)
                {
                    try
                    {
                        drafts[group.Key] = await SetReleasePlan.FindDraftsAsync(nests, group);
                        lookupErrors.Remove(group.Key);
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                        or System.Text.Json.JsonException or NotSupportedException)
                    {
                        drafts[group.Key] = Array.Empty<NestRecord>();
                        lookupErrors[group.Key] = ex.Message;
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
                {
                    FillTargets(row, group);
                    row.Cells[statusColumn.Index].Value = GroupNote(group);
                }
            }
            populating = false;
            UpdateCommands();
        }

        private void UpdateNotes()
        {
            populating = true;
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                if (row.Tag is SetReleaseGroup group)
                    row.Cells[statusColumn.Index].Value = GroupNote(group);
            }
            populating = false;
        }

        /// <summary>What Make Nests will do with <paramref name="group"/>, or why it can't.</summary>
        internal string GroupNote(SetReleaseGroup group)
        {
            if (!group.CanRelease)
                return group.Refusal;
            var notes = new List<string>();
            if (excluded.Contains(group.Key))
                notes.Add("Left out of this release.");
            else if (!drafts.TryGetValue(group.Key, out var candidates))
                notes.Add("Looking for a Draft nest to merge into...");
            else if (TargetOf(group) is { } draft)
            {
                var sheets = $"{draft.PlateCount} sheet{(draft.PlateCount == 1 ? "" : "s")}";
                if (IsNestOpen(draft.Id))
                    notes.Add($"{draft.Name} is open — save and close it to merge into it.");
                else if (nestAfterReleaseCheckBox.Checked)
                    notes.Add($"→ merges into {draft.Name}; the whole draft ({sheets}) is nested again.");
                else
                    notes.Add($"→ merges into {draft.Name}; its {sheets} {(draft.PlateCount == 1 ? "is" : "are")} cleared, "
                        + "leaving the draft without sheets until it is nested.");
                if (candidates.Count > 1 && !targets.ContainsKey(group.Key))
                    notes.Add($"{candidates.Count} Draft nests match; the newest is chosen.");
            }
            else
            {
                notes.Add(nestAfterReleaseCheckBox.Checked ? "→ new Draft nest, nested with Auto Nest." : "→ new Draft nest.");
                if (lookupErrors.TryGetValue(group.Key, out var error))
                    notes.Add($"Could not look for a Draft nest to merge into: {error}");
                else if (candidates.Count > 0)
                    notes.Add($"{candidates.Count} Draft nest{(candidates.Count == 1 ? " matches" : "s match")}; choose one to merge.");
            }
            notes.AddRange(group.Warnings);
            return string.Join(" ", notes);
        }

        // A draft by its name; when several share it, the saved time tells them apart.
        private static string DraftChoice(NestRecord draft, IReadOnlyList<NestRecord> candidates) =>
            candidates.Count(d => d.Name == draft.Name) > 1
                ? $"{draft.Name} (saved {draft.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)})"
                : draft.Name;

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
            populating = true;
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
            populating = false;

            var done = Result.Created.Count;
            var nestsText = $"{done} nest{(done == 1 ? "" : "s")}";
            if (Result.Release != null)
                statusLabel.Text = $"Done: {nestsText} saved and opened.";
            else if (Result.ReleaseError.Length > 0)
                statusLabel.Text = Result.ReleaseError.TrimEnd('.', ' ')
                    + ". Try Again records it; the nests are not made again.";
            else if (Result.Canceled)
                statusLabel.Text = $"Canceled: {nestsText} saved, release not recorded.";
            else
                statusLabel.Text = done == 0
                    ? "Nothing saved. See What happens."
                    : $"Stopped: {nestsText} saved, release not recorded. See What happens.";
            UpdateCommands();
        }

        private void UpdateCommands()
        {
            var chosen = plan == null ? Array.Empty<SetReleaseGroup>() : ChosenGroups();
            var editable = !running && Result == null;
            lineQuantityColumn.ReadOnly = !editable;
            notesBox.ReadOnly = !editable;
            includeArchivedCheckBox.Enabled = editable;
            includeArchivedCheckBox.Visible = includeArchivedCheckBox.Checked || plan?.SkippedArchived.Count > 0;
            nestAfterReleaseCheckBox.Enabled = editable;
            includeColumn.ReadOnly = !editable;
            targetColumn.ReadOnly = !editable;
            if (editable)
            {
                foreach (DataGridViewRow row in groupGrid.Rows)
                {
                    var refused = row.Tag is SetReleaseGroup { CanRelease: false };
                    row.Cells[includeColumn.Index].ReadOnly = refused;
                    row.Cells[targetColumn.Index].ReadOnly = refused
                        || ((DataGridViewComboBoxCell)row.Cells[targetColumn.Index]).Items.Count < 2;
                }
            }

            var refusedAny = plan != null && plan.Groups.Any(g => !g.CanRelease);
            fixDrawingsButton.Visible = libraryStore != null && refusedAny;
            fixDrawingsButton.Enabled = CanEditDrawings() && RefusedRow() != null;

            hintLabel.Visible = IsEmpty || Result != null;
            hintLabel.Text = Result != null
                ? "Double-click a set or a drawing to start the next release, or Clear."
                : "Double-click a set or a drawing to add it. Adding one again adds to its quantity.";
            clearButton.Text = running ? "Stop" : "Clear";
            clearButton.Enabled = running ? cancellation?.IsCancellationRequested != true : !IsEmpty || Result != null;

            var retry = Result is { Release: null, ReleaseError.Length: > 0 };
            var open = editable ? OpenTarget() : null;
            releaseButton.Text = retry ? "Try Again" : chosen.Count == 0 ? "Make Nests" : $"Make Nests ({chosen.Count})";
            releaseButton.Enabled = IsInitialized && !running
                && (retry || (editable && chosen.Count > 0 && open == null && DraftsKnown(chosen)));
            previewLabel.Text = editable ? Preview(chosen, open) : "";
            previewLabel.Visible = previewLabel.Text.Length > 0;
        }

        // What needs the operator's attention before Make Nests.
        private string Preview(IReadOnlyList<SetReleaseGroup> chosen, NestRecord open)
        {
            if (plan == null || plan.Groups.Count == 0)
                return "";
            var notes = new List<string>();
            if (open != null)
                notes.Add($"Make Nests waits: {open.Name} is open in a tab. Save and close it, or choose {NewNestChoice} for its material.");
            var refused = plan.Groups.Count(g => !g.CanRelease);
            if (refused > 0)
                notes.Add($"{refused} material{(refused == 1 ? " can't" : "s can't")} be released yet: "
                    + (libraryStore == null ? "see What happens." : "choose Fix Drawings."));
            var skipped = plan.SkippedArchived;
            if (skipped.Count > 0)
                notes.Add($"{skipped.Count} archived drawing{(skipped.Count == 1 ? " is" : "s are")} left out: "
                    + string.Join(", ", skipped.Take(5).Select(m => $"'{m.Drawing!.Name}'"))
                    + (skipped.Count > 5 ? $" and {skipped.Count - 5} more" : "") + ".");
            if (chosen.Count > 0 && !DraftsKnown(chosen))
                notes.Add("Looking for Draft nests to merge into...");
            var merges = chosen.Count(g => TargetOf(g) != null);
            if (chosen.Count > 0 && DraftsKnown(chosen))
            {
                var parts = chosen.Sum(g => (long)g.TotalQuantity);
                notes.Insert(0, $"{chosen.Count - merges} new, {merges} merged, {parts:N0} part{(parts == 1 ? "" : "s")}.");
            }
            return string.Join(" ", notes);
        }

        // "Synthetic Steel Grade A 0.25 in, Laser".
        private static string GroupText(SetReleaseGroup group) =>
            group.IsUnassigned ? "(no material or thickness)" : $"{Describe(group)}, {group.CutType}";

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
    }

    /// <summary>
    /// Where Make Nests nests what it releases when Nest after release is ticked: the main
    /// window, which shows each nest in its tab while Auto Nest runs on it.
    /// </summary>
    public interface IReleaseNestingHost
    {
        /// <summary>Make Nests is starting; nesting follows, one nest at a time in plan order.</summary>
        void Begin();

        /// <summary>
        /// Shows <paramref name="nest"/>, built but not saved, and nests it from scratch: every
        /// drawing's required quantity, keeping only a valid layout (else no sheets and a note).
        /// Throws <see cref="OperationCanceledException"/> when the operator stops it, which
        /// stops the release.
        /// </summary>
        Task<SetReleaseRenest> RenestAsync(Nest nest, CancellationToken token);

        /// <summary>
        /// Make Nests has finished (<paramref name="result"/> is null if it threw) and has
        /// already opened what it saved: whatever was shown but not saved goes.
        /// </summary>
        void End(SetReleaseResult result);
    }

    /// <summary>A release being built, kept while the Drawing Sets window is closed.</summary>
    public sealed record ReleaseDraft(IReadOnlyList<SetReleasePlanLine> Sets, IReadOnlyList<SetReleaseDrawingLine> Drawings,
        string Notes, bool IncludeArchived);
}

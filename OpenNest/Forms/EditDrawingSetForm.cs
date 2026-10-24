using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Creates or edits one drawing set through <see cref="DrawingSetEditSession"/>: name,
    /// customer, notes and the ordered members with a quantity each. The drawing library is
    /// browsed in the left pane (every active drawing, a page at a time through
    /// <see cref="DrawingLibraryBrowseSession"/>); Add to Set, a double-click or Enter adds the
    /// selected drawings at the entered quantity, and a drawing already in the set is greyed
    /// and not added twice. Edit Drawing (a double-click on a member, its right-click menu or
    /// the button) reads the member's current library record and edits its details there, so
    /// a drawing without a material or thickness can be fixed in place. History lists a saved
    /// set's revisions. Saving adds a revision on the server, guarded by the revision the set
    /// was read at.
    /// </summary>
    public partial class EditDrawingSetForm : Form
    {
        private readonly DrawingSetEditSession session;
        private readonly IDrawingLibraryStore library;
        private readonly DrawingLibraryBrowseSession librarySession;
        private readonly BrowsePager<DrawingLibraryPage, DrawingSortField> libraryPager;
        private bool loading;
        private bool saving;
        private bool editing;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Shows Edit Drawing and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingLibraryEditSession, DrawingLibraryRecord> EditDrawing { get; set; }

        /// <summary>Shows a set's revision history; replaced by tests.</summary>
        internal Action<DrawingSetRecord> ShowHistory { get; set; }

        /// <summary>The saved revision once the dialog closes with OK; otherwise null.</summary>
        public DrawingSetRecord Saved { get; private set; }

        public EditDrawingSetForm(DrawingSetEditSession session, IDrawingLibraryStore library, string serverUrl)
        {
            InitializeComponent();
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            // A set mixes materials and thicknesses, so the library is never filtered to a nest.
            librarySession = new DrawingLibraryBrowseSession(library, null);
            Disposed += (_, _) => librarySession.Dispose();
            libraryPager = new BrowsePager<DrawingLibraryPage, DrawingSortField>(
                librarySession, libraryGrid, librarySummaryLabel, previousButton, nextButton, "the drawing library")
            {
                Populate = PopulateLibrary,
                Settled = UpdateAddCommand,
            };
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
            EditDrawing = edit =>
            {
                using var form = new EditLibraryDrawingForm(edit);
                return form.ShowDialog(this) == DialogResult.OK ? form.Saved : null;
            };
            ShowHistory = set =>
            {
                using var form = new DrawingSetHistoryForm(session.Store, set);
                form.ShowDialog(this);
            };

            GridStyle.Apply(memberGrid);
            GridStyle.Apply(libraryGrid);
            libraryNameColumn.Tag = DrawingSortField.Name;
            libraryMaterialColumn.Tag = DrawingSortField.MaterialName;
            libraryGradeColumn.Tag = DrawingSortField.MaterialGrade;
            libraryThicknessColumn.Tag = DrawingSortField.Thickness;
            libraryCustomerColumn.Tag = DrawingSortField.Customer;
            quantityBox.Maximum = DrawingSetRules.MaxQuantity;
            libraryGroup.Text = string.IsNullOrEmpty(serverUrl) ? "Drawing library" : $"Drawing library — {serverUrl}";
            loading = true;
            nameBox.Text = session.Name;
            customerBox.Text = session.Customer;
            notesBox.Text = session.Notes;
            loading = false;
            Text = session.IsNew
                ? "New Drawing Set"
                : $"Edit Set '{session.Saved.Name}' — revision {session.Saved.Revision}";
            // A new set has no history until it is saved, and the dialog closes on save.
            historyButton.Visible = !session.IsNew;
            Populate(session.Members.Count > 0 ? 0 : -1);
            UpdateCommands();
        }

        private void Details_TextChanged(object sender, EventArgs e)
        {
            if (loading)
                return;
            session.Name = nameBox.Text;
            session.Customer = customerBox.Text;
            session.Notes = notesBox.Text;
            UpdateCommands();
        }

        private void MemberGrid_CurrentCellChanged(object sender, EventArgs e) => UpdateCommands();

        private void HistoryButton_Click(object sender, EventArgs e)
        {
            if (session.Saved is { } saved)
                ShowHistory(saved);
        }

        private void MemberGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex != quantityColumn.Index)
                return;
            var row = memberGrid.Rows[e.RowIndex];
            if (int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture), NumberStyles.Integer,
                    CultureInfo.CurrentCulture, out var quantity)
                && quantity >= 1 && quantity <= DrawingSetRules.MaxQuantity)
            {
                row.ErrorText = "";
                return;
            }

            row.ErrorText = $"Quantity must be a whole number from 1 to {DrawingSetRules.MaxQuantity:N0}.";
            statusLabel.Text = row.ErrorText;
            e.Cancel = true;
        }

        // A programmatic EndEdit (Save, Remove, Move) commits without validating, so check again.
        private void MemberGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != quantityColumn.Index)
                return;
            var row = memberGrid.Rows[e.RowIndex];
            var cell = row.Cells[e.ColumnIndex];
            if (cell.Value is int quantity && quantity >= 1 && quantity <= DrawingSetRules.MaxQuantity)
            {
                session.SetQuantity(e.RowIndex, quantity);
                row.ErrorText = "";
                UpdateCommands();
                return;
            }

            cell.Value = session.Members[e.RowIndex].Quantity;
            row.ErrorText = $"Quantity must be a whole number from 1 to {DrawingSetRules.MaxQuantity:N0}.";
            UpdateCommands();
            statusLabel.Text = row.ErrorText;
        }

        // Validation refuses bad text before it is parsed; never show the grid's own error dialog.
        private void MemberGrid_DataError(object sender, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

        private async void EditDrawingSetForm_Shown(object sender, EventArgs e) =>
            await RunLibraryAsync(librarySession.RefreshAsync);

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private async void SearchTimer_Tick(object sender, EventArgs e)
        {
            searchTimer.Stop();
            await RunLibraryAsync(() => librarySession.SetSearchAsync(searchBox.Text));
        }

        private async void LibraryGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Sorting a page locally would misrepresent the library; the server sorts all matches.
            if (e.Button == MouseButtons.Left && libraryGrid.Columns[e.ColumnIndex].Tag is DrawingSortField field)
                await RunLibraryAsync(() => librarySession.SortByAsync(field));
        }

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await RunLibraryAsync(librarySession.PreviousPageAsync);

        private async void NextButton_Click(object sender, EventArgs e) =>
            await RunLibraryAsync(librarySession.NextPageAsync);

        private void LibraryGrid_SelectionChanged(object sender, EventArgs e) => UpdateAddCommand();

        private void LibraryGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                AddSelectedDrawings();
        }

        private void AddButton_Click(object sender, EventArgs e) => AddSelectedDrawings();

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && libraryGrid.ContainsFocus && SelectedLibraryDrawings().Count > 0)
            {
                AddSelectedDrawings();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            var index = SelectedIndex();
            if (index < 0 || !memberGrid.EndEdit())
                return;
            session.RemoveAt(index);
            Populate(System.Math.Min(index, session.Members.Count - 1));
            UpdateCommands();
        }

        // The quantity cell edits in place; a double-click anywhere else edits the drawing.
        private async void MemberGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != quantityColumn.Index)
                await EditSelectedDrawingAsync();
        }

        // A right-click selects the row under the pointer, so the menu acts on what was clicked.
        private void MemberGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && SelectedIndex() != e.RowIndex && memberGrid.EndEdit())
                memberGrid.CurrentCell = memberGrid.Rows[e.RowIndex].Cells[drawingColumn.Index];
        }

        private void MemberMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var index = SelectedIndex();
            editDrawingMenuItem.Enabled = !saving && !editing && index >= 0;
            removeMenuItem.Enabled = !saving && !editing && index >= 0;
            e.Cancel = index < 0;
        }

        private async void EditDrawingButton_Click(object sender, EventArgs e) => await EditSelectedDrawingAsync();

        /// <summary>
        /// Reads the selected member's current library record and opens Edit Drawing on it. The
        /// member then shows what was read or saved; the set itself lists drawings by id, so it
        /// is not changed and the next release takes the new revision.
        /// </summary>
        internal async Task EditSelectedDrawingAsync()
        {
            var index = SelectedIndex();
            if (index < 0 || saving || editing || !memberGrid.EndEdit())
                return;

            var member = session.Members[index];
            var name = member.Drawing?.Name ?? member.DrawingId.ToString();
            DrawingLibraryRecord current;
            editing = true;
            UpdateCommands();
            statusLabel.Text = $"Reading '{name}'...";
            try
            {
                current = await library.GetAsync(member.DrawingId);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!IsDisposed)
                {
                    statusLabel.Text = "";
                    ShowMessage($"Could not read '{name}': {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            finally
            {
                editing = false;
                if (!IsDisposed)
                    UpdateCommands();
            }

            if (IsDisposed)
                return;
            statusLabel.Text = "";
            if (current == null)
            {
                ShowMessage($"'{name}' is no longer in the library.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowMemberDrawing(current);
            if (current.Archived)
            {
                ShowMessage($"'{current.Name}' is archived in the library. Restore it there before editing it.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var saved = EditDrawing(new DrawingLibraryEditSession(library, current, Environment.UserName));
            if (saved == null || IsDisposed)
                return;
            ShowMemberDrawing(saved);
            // The library pane may list the old details.
            await RunLibraryAsync(librarySession.RefreshAsync);
            if (!IsDisposed)
                statusLabel.Text = $"Saved '{saved.Name}' revision {saved.Revision} to the library.";
        }

        private void ShowMemberDrawing(DrawingLibraryRecord drawing)
        {
            var index = SelectedIndex();
            if (session.UpdateDrawing(drawing))
                Populate(index);
        }

        private void UpButton_Click(object sender, EventArgs e) => MoveSelected(-1);

        private void DownButton_Click(object sender, EventArgs e) => MoveSelected(1);

        private void MoveSelected(int offset)
        {
            var index = SelectedIndex();
            if (index < 0 || !memberGrid.EndEdit() || !session.Move(index, offset))
                return;
            Populate(index + offset);
            UpdateCommands();
        }

        /// <summary>
        /// Adds the drawings selected in the library pane, in list order, at the entered
        /// quantity, skipping those already in the set.
        /// </summary>
        internal void AddSelectedDrawings()
        {
            var drawings = SelectedLibraryDrawings();
            if (saving || drawings.Count == 0 || !memberGrid.EndEdit())
                return;

            var result = session.AddDrawings(drawings, (int)quantityBox.Value);
            Populate(result.Added.Count > 0 ? session.Members.Count - 1 : SelectedIndex());
            UpdateCommands();
            var added = result.Added.Count switch
            {
                0 => "Nothing added.",
                1 => $"Added '{result.Added[0].Name}'.",
                _ => $"Added {result.Added.Count} drawings.",
            };
            statusLabel.Text = result.AlreadyMembers.Count == 0
                ? added
                : $"{added} Already in the set: {string.Join(", ", result.AlreadyMembers.Select(d => $"'{d.Name}'"))}.";
        }

        private IReadOnlyList<DrawingLibraryRecord> SelectedLibraryDrawings() =>
            libraryPager.SelectedRecords<DrawingLibraryRecord>();

        private Task RunLibraryAsync(Func<Task<bool>> request) => libraryPager.RunAsync(request);

        private void PopulateLibrary(DrawingLibraryPage page)
        {
            libraryGrid.Rows.Clear();
            foreach (var record in page?.Items ?? Array.Empty<DrawingLibraryRecord>())
            {
                var index = libraryGrid.Rows.Add(
                    record.Name,
                    record.MaterialName,
                    record.MaterialGrade,
                    record.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                    record.Customer);
                libraryGrid.Rows[index].Tag = record;
            }

            libraryPager.ShowSortGlyphs();
            MarkMembersInLibrary();
            UpdateAddCommand();
        }

        // Greys the library rows already in the set, so what is left to add stands out.
        private void MarkMembersInLibrary()
        {
            var members = session.Members.Select(m => m.DrawingId).ToHashSet();
            foreach (DataGridViewRow row in libraryGrid.Rows)
            {
                var inSet = row.Tag is DrawingLibraryRecord record && members.Contains(record.Id);
                row.DefaultCellStyle.ForeColor = inSet ? SystemColors.GrayText : Color.Empty;
                row.Cells[libraryNameColumn.Index].ToolTipText = inSet ? "Already in this set" : "";
            }
        }

        private void UpdateAddCommand() =>
            addButton.Enabled = !saving && SelectedLibraryDrawings().Count > 0;

        private async void SaveButton_Click(object sender, EventArgs e) => await SaveAsync();

        /// <summary>Saves the set as a new revision; closes with OK on success.</summary>
        internal async Task SaveAsync()
        {
            if (saving || !memberGrid.EndEdit() || session.GetError() != null || !session.IsChanged)
                return;

            saving = true;
            UpdateCommands();
            statusLabel.Text = "Saving...";
            try
            {
                Saved = await session.SaveAsync();
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
                ShowMessage(ex is DrawingSetSaveConflictException
                    ? ex.Message + Environment.NewLine + Environment.NewLine + "Cancel to reload the set, then edit it again."
                    : $"Could not save the set: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                saving = false;
                if (!IsDisposed)
                    UpdateCommands();
            }
        }

        private void EditDrawingSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (saving)
            {
                e.Cancel = true;
                return;
            }

            if (DialogResult == DialogResult.OK || !HasEdits())
                return;
            if (ShowMessage("Discard the changes to this set?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        }

        private bool HasEdits() =>
            session.IsNew
                ? !string.IsNullOrWhiteSpace(session.Name) || !string.IsNullOrWhiteSpace(session.Customer)
                    || !string.IsNullOrWhiteSpace(session.Notes) || session.Members.Count > 0
                : session.IsChanged;

        private void Populate(int select)
        {
            memberGrid.Rows.Clear();
            foreach (var member in session.Members)
            {
                var drawing = member.Drawing;
                var index = memberGrid.Rows.Add(
                    drawing?.Name ?? member.DrawingId.ToString(),
                    drawing?.MaterialName ?? "",
                    drawing?.MaterialGrade ?? "",
                    drawing == null ? "" : drawing.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                    drawing?.Units.ToString() ?? "",
                    drawing?.CutType.ToString() ?? "",
                    member.Quantity,
                    Warning(drawing));
                memberGrid.Rows[index].Tag = member;
            }

            if (select >= 0 && select < memberGrid.Rows.Count)
                memberGrid.CurrentCell = memberGrid.Rows[select].Cells[drawingColumn.Index];
            else
                memberGrid.ClearSelection();

            MarkMembersInLibrary();
            var parts = session.Members.Sum(m => (long)m.Quantity);
            summaryLabel.Text = session.Members.Count == 0
                ? "No drawings yet. Select drawings in the library on the left and choose Add to Set."
                : $"{session.Members.Count:N0} drawing{(session.Members.Count == 1 ? "" : "s")}, {parts:N0} part{(parts == 1 ? "" : "s")} per set";
        }

        private static string Warning(DrawingLibraryRecord drawing)
        {
            if (drawing == null)
                return "";
            if (drawing.Archived)
                return "Archived in the library: skipped on release unless included";
            if (string.IsNullOrWhiteSpace(drawing.MaterialName) || !(drawing.Thickness > 0))
                return "No material or thickness: double-click to set them before releasing";
            return "";
        }

        private int SelectedIndex() => memberGrid.CurrentRow?.Index ?? -1;

        private void UpdateCommands()
        {
            var index = SelectedIndex();
            var error = session.GetError();
            UpdateAddCommand();
            editDrawingButton.Enabled = !saving && !editing && index >= 0;
            removeButton.Enabled = !saving && index >= 0;
            upButton.Enabled = !saving && index > 0;
            downButton.Enabled = !saving && index >= 0 && index < session.Members.Count - 1;
            saveButton.Enabled = !saving && error == null && session.IsChanged;
            if (!saving)
                statusLabel.Text = error == null ? "" : char.ToUpper(error[0], CultureInfo.CurrentCulture) + error[1..];
        }
    }
}

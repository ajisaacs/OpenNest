using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// Nest &gt; Add from Drawing Library (Database mode). Browses the server drawing library
    /// one bounded page at a time through <see cref="DrawingLibraryBrowseSession"/>, filtered
    /// to the nest's material and thickness unless Show all is ticked. Add downloads the
    /// listed revision's exact package, plans it with <see cref="LibraryDrawingInsert"/>,
    /// shows any warning or refusal first and only then changes the nest. Edit Settings
    /// changes the selected drawing's notes and nesting settings (Cut first, priority,
    /// rotation, color) on the server as a new revision with the same package.
    /// </summary>
    public partial class DrawingLibraryForm : Form
    {
        private const string LoadingText = "Loading...";

        private readonly IDrawingLibraryStore library;
        private readonly string serverUrl;
        private readonly Nest nest;
        private readonly DrawingLibraryBrowseSession session;
        private bool adding;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Shows the settings editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingLibraryEditSession, DrawingLibraryRecord> EditDrawing { get; set; }

        /// <summary>
        /// The library drawings this dialog added or raised the quantity of, in order. The
        /// dialog stays open after each add, so callers check this rather than the dialog result.
        /// </summary>
        public List<Drawing> AddedDrawings { get; } = new();

        public DrawingLibraryForm(IDrawingLibraryStore library, string serverUrl, Nest nest)
        {
            InitializeComponent();
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            this.nest = nest ?? throw new ArgumentNullException(nameof(nest));
            session = new DrawingLibraryBrowseSession(library, nest);
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
            EditDrawing = edit =>
            {
                using var form = new EditLibraryDrawingForm(edit);
                return form.ShowDialog(this) == DialogResult.OK ? form.Saved : null;
            };

            GridStyle.Apply(drawingGrid);
            nameColumn.Tag = DrawingSortField.Name;
            customerColumn.Tag = DrawingSortField.Customer;
            materialColumn.Tag = DrawingSortField.MaterialName;
            gradeColumn.Tag = DrawingSortField.MaterialGrade;
            thicknessColumn.Tag = DrawingSortField.Thickness;
            cutTypeColumn.Tag = DrawingSortField.CutType;
            revisionColumn.Tag = DrawingSortField.Revision;
            savedColumn.Tag = DrawingSortField.SavedAt;

            Text = $"Add from Drawing Library — {this.serverUrl}";
            showAllCheckBox.Enabled = session.IsFilteredToNest;
            UpdateFilterLabel();
            UpdateNavigation();
        }

        private async void DrawingLibraryForm_Shown(object sender, EventArgs e) =>
            await RunAsync(session.RefreshAsync);

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        private async void SearchTimer_Tick(object sender, EventArgs e)
        {
            searchTimer.Stop();
            await RunAsync(() => session.SetSearchAsync(searchBox.Text));
        }

        private async void ShowAllCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            var showAll = showAllCheckBox.Checked;
            UpdateFilterLabel(showAll);
            await RunAsync(() => session.SetShowAllAsync(showAll));
        }

        private async void DrawingGrid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Sorting a page locally would misrepresent the library; the server sorts all matches.
            if (e.Button == MouseButtons.Left && drawingGrid.Columns[e.ColumnIndex].Tag is DrawingSortField field)
                await RunAsync(() => session.SortByAsync(field));
        }

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await RunAsync(session.PreviousPageAsync);

        private async void NextButton_Click(object sender, EventArgs e) =>
            await RunAsync(session.NextPageAsync);

        private void DrawingGrid_SelectionChanged(object sender, EventArgs e)
        {
            notesBox.Text = SelectedRecord()?.Notes ?? "";
            UpdateCommands();
        }

        private async void DrawingGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                await AddSelectedAsync();
        }

        private async void AddButton_Click(object sender, EventArgs e) => await AddSelectedAsync();

        private async void EditButton_Click(object sender, EventArgs e) => await EditSelectedAsync();

        /// <summary>
        /// Edits the selected drawing's notes and nesting settings as a new library revision,
        /// then reloads the page so the list shows the saved revision.
        /// </summary>
        internal async Task EditSelectedAsync()
        {
            var record = SelectedRecord();
            if (record == null || adding || record.Archived)
                return;

            var saved = EditDrawing(new DrawingLibraryEditSession(library, record, Environment.UserName));
            if (saved == null || IsDisposed)
                return;
            await RunAsync(session.RefreshAsync);
            if (!IsDisposed)
                statusLabel.Text = $"Saved '{saved.Name}' revision {saved.Revision}.";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && drawingGrid.ContainsFocus && SelectedRecord() != null)
            {
                _ = AddSelectedAsync();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Downloads the selected revision, plans it against the nest, shows warnings or the
        /// refusal before anything changes, then applies it. The dialog is modal, so the nest
        /// cannot change between the plan and the apply.
        /// </summary>
        internal async Task AddSelectedAsync()
        {
            var record = SelectedRecord();
            if (record == null || adding)
                return;

            adding = true;
            UpdateCommands();
            statusLabel.Text = $"Downloading '{record.Name}' revision {record.Revision}...";
            var quantity = (int)quantityBox.Value;
            try
            {
                var bytes = await library.GetRevisionPackageAsync(record.Id, record.Revision);
                if (IsDisposed)
                    return;
                if (bytes == null)
                {
                    statusLabel.Text = "";
                    ShowMessage($"Revision {record.Revision} of '{record.Name}' is no longer on the server.",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    await RunAsync(session.RefreshAsync);
                    return;
                }

                var contents = await Task.Run(() => DrawingPackage.Read(bytes));
                if (IsDisposed)
                    return;

                var insert = LibraryDrawingInsert.Plan(nest, serverUrl, record,
                    new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings), quantity);
                if (insert.IsRefused)
                {
                    statusLabel.Text = "";
                    ShowMessage(insert.Refusal, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (insert.Warnings.Count > 0
                    && ShowMessage(string.Join(Environment.NewLine + Environment.NewLine, insert.Warnings)
                        + Environment.NewLine + Environment.NewLine + "Add it to the nest anyway?",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                {
                    statusLabel.Text = "";
                    return;
                }

                insert.Apply();
                if (!AddedDrawings.Contains(insert.Drawing))
                    AddedDrawings.Add(insert.Drawing);
                statusLabel.Text = insert.Kind == LibraryInsertKind.AddQuantity
                    ? $"Added {quantity} to '{insert.Drawing.Name}' (now {insert.Drawing.Quantity.Required})."
                    : $"Added '{insert.Drawing.Name}' revision {record.Revision}, quantity {quantity}.";
            }
            catch (ServerTooOldException ex)
            {
                Fail(ex.Message);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                // InvalidDataException (an unreadable package) is an IOException too.
                Fail($"Could not add '{record.Name}': {ex.Message}");
            }
            finally
            {
                adding = false;
                if (!IsDisposed)
                    UpdateCommands();
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
                    drawingGrid.Rows.Clear();
                    summaryLabel.Text = ex is ServerTooOldException
                        ? ex.Message
                        : $"Could not load the drawing library: {ex.Message}";
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

        private void Populate(DrawingLibraryPage page)
        {
            drawingGrid.Rows.Clear();
            foreach (var record in page?.Items ?? Array.Empty<DrawingLibraryRecord>())
            {
                var index = drawingGrid.Rows.Add(
                    record.Name,
                    record.Customer,
                    record.MaterialName,
                    record.MaterialGrade,
                    FormatThickness(record.Thickness),
                    record.CutType.ToString(),
                    record.Settings?.CutFirst == true ? "Yes" : "",
                    record.Revision,
                    record.SavedAt == default ? "" : record.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                drawingGrid.Rows[index].Tag = record;
            }

            foreach (DataGridViewColumn column in drawingGrid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Tag is DrawingSortField field && field == session.Sort
                    ? session.Descending ? SortOrder.Descending : SortOrder.Ascending
                    : SortOrder.None;
            }

            notesBox.Text = SelectedRecord()?.Notes ?? "";
        }

        private DrawingLibraryRecord SelectedRecord() =>
            drawingGrid.CurrentRow?.Tag as DrawingLibraryRecord;

        private void UpdateNavigation()
        {
            previousButton.Enabled = !session.IsLoading && session.CanGoPrevious;
            nextButton.Enabled = !session.IsLoading && session.CanGoNext;
        }

        private void UpdateCommands()
        {
            addButton.Enabled = !adding && SelectedRecord() != null;
            editButton.Enabled = !adding && SelectedRecord() is { Archived: false };
        }

        private void UpdateFilterLabel(bool showAll = false)
        {
            if (!session.IsFilteredToNest && !showAll)
            {
                filterLabel.Text = "This nest has no material or thickness; showing every drawing.";
                return;
            }

            var parts = new[]
            {
                session.Thickness is { } thickness ? FormatThickness(thickness) : "",
                session.MaterialName,
                session.MaterialGrade,
            }.Where(part => part.Length > 0);
            filterLabel.Text = showAll
                ? "Showing every drawing, including other materials and thicknesses."
                : $"Showing drawings for this nest: {string.Join(" ", parts)}";
        }

        private static string FormatThickness(double value) =>
            value.ToString("0.####", CultureInfo.CurrentCulture);
    }
}

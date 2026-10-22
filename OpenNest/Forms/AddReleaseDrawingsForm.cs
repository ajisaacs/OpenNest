using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Release &gt; Add Drawings: browses every active library drawing a page at a time, as the
    /// set editor's library pane does (<see cref="DrawingLibraryBrowseSession"/>, never filtered
    /// to a nest), and adds the selected drawings to the release dialog at the entered quantity.
    /// It stays open so several drawings can be added at different quantities; a drawing the
    /// release already lists is greyed.
    /// </summary>
    public partial class AddReleaseDrawingsForm : Form
    {
        private const string LoadingText = "Loading...";

        private readonly DrawingLibraryBrowseSession librarySession;
        private readonly Func<IReadOnlyList<DrawingLibraryRecord>, int, string> add;
        private readonly Func<Guid, bool> isListed;

        /// <param name="add">Adds the drawings at a quantity and returns what happened, for the status line.</param>
        /// <param name="isListed">True for a drawing the release already lists on its own.</param>
        public AddReleaseDrawingsForm(IDrawingLibrary library, string serverUrl,
            Func<IReadOnlyList<DrawingLibraryRecord>, int, string> add, Func<Guid, bool> isListed)
        {
            InitializeComponent();
            ArgumentNullException.ThrowIfNull(library);
            this.add = add ?? throw new ArgumentNullException(nameof(add));
            this.isListed = isListed ?? throw new ArgumentNullException(nameof(isListed));
            // A release mixes materials and thicknesses, so the library is never filtered to a nest.
            librarySession = new DrawingLibraryBrowseSession(library, new Nest());

            GridStyle.Apply(libraryGrid);
            libraryNameColumn.Tag = DrawingSortField.Name;
            libraryMaterialColumn.Tag = DrawingSortField.MaterialName;
            libraryGradeColumn.Tag = DrawingSortField.MaterialGrade;
            libraryThicknessColumn.Tag = DrawingSortField.Thickness;
            libraryCutTypeColumn.Tag = DrawingSortField.CutType;
            libraryCustomerColumn.Tag = DrawingSortField.Customer;
            quantityBox.Maximum = SetReleaseRules.MaxDrawingQuantity;
            if (!string.IsNullOrEmpty(serverUrl))
                Text = $"Add Drawings to the Release — {serverUrl}";
        }

        /// <summary>Loads the first page; the dialog does this when shown.</summary>
        internal Task RefreshAsync() => RunLibraryAsync(librarySession.RefreshAsync);

        private async void AddReleaseDrawingsForm_Shown(object sender, EventArgs e) => await RefreshAsync();

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
            if (keyData == Keys.Enter && libraryGrid.ContainsFocus && SelectedDrawings().Count > 0)
            {
                AddSelectedDrawings();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Adds the selected drawings, in list order, at the entered quantity.</summary>
        internal void AddSelectedDrawings()
        {
            var drawings = SelectedDrawings();
            if (drawings.Count == 0)
                return;
            statusLabel.Text = add(drawings, (int)quantityBox.Value);
            MarkListed();
        }

        /// <summary>The quantity the next Add uses.</summary>
        internal int Quantity
        {
            get => (int)quantityBox.Value;
            set => quantityBox.Value = value;
        }

        /// <summary>Selects the listed drawings with these names, for tests.</summary>
        internal void Select(params string[] names)
        {
            libraryGrid.ClearSelection();
            foreach (DataGridViewRow row in libraryGrid.Rows)
                row.Selected = row.Tag is DrawingLibraryRecord record && names.Contains(record.Name);
        }

        /// <summary>The status line, for tests.</summary>
        internal string Status => statusLabel.Text;

        // Every selected row in list order; the current row alone when nothing is selected.
        private IReadOnlyList<DrawingLibraryRecord> SelectedDrawings()
        {
            var picked = libraryGrid.SelectedRows.Cast<DataGridViewRow>()
                .OrderBy(row => row.Index)
                .Select(row => row.Tag as DrawingLibraryRecord)
                .Where(record => record != null)
                .ToList();
            if (picked.Count == 0 && libraryGrid.CurrentRow?.Tag is DrawingLibraryRecord current)
                picked.Add(current);
            return picked;
        }

        private async Task RunLibraryAsync(Func<Task<bool>> request)
        {
            librarySummaryLabel.Text = LoadingText;
            try
            {
                var started = request();
                UpdateLibraryNavigation();
                if (await started && !IsDisposed)
                    PopulateLibrary(librarySession.Page);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    libraryGrid.Rows.Clear();
                    librarySummaryLabel.Text = ex is ServerTooOldException
                        ? ex.Message
                        : $"Could not load the drawing library: {ex.Message}";
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    UpdateLibraryNavigation();
                    // A request that was declined or superseded renders nothing of its own.
                    if (!librarySession.IsLoading && librarySummaryLabel.Text == LoadingText)
                        librarySummaryLabel.Text = librarySession.Summary;
                    UpdateAddCommand();
                }
            }
        }

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
                    record.CutType.ToString(),
                    record.Customer);
                libraryGrid.Rows[index].Tag = record;
            }

            foreach (DataGridViewColumn column in libraryGrid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Tag is DrawingSortField field && field == librarySession.Sort
                    ? librarySession.Descending ? SortOrder.Descending : SortOrder.Ascending
                    : SortOrder.None;
            }

            MarkListed();
            UpdateAddCommand();
        }

        // Greys the drawings the release already lists, so what is left to add stands out.
        private void MarkListed()
        {
            foreach (DataGridViewRow row in libraryGrid.Rows)
            {
                var listed = row.Tag is DrawingLibraryRecord record && isListed(record.Id);
                row.DefaultCellStyle.ForeColor = listed ? SystemColors.GrayText : Color.Empty;
                row.Cells[libraryNameColumn.Index].ToolTipText = listed ? "Already in this release; change its quantity there" : "";
            }
        }

        private void UpdateLibraryNavigation()
        {
            previousButton.Enabled = !librarySession.IsLoading && librarySession.CanGoPrevious;
            nextButton.Enabled = !librarySession.IsLoading && librarySession.CanGoNext;
        }

        private void UpdateAddCommand() => addButton.Enabled = SelectedDrawings().Count > 0;
    }
}

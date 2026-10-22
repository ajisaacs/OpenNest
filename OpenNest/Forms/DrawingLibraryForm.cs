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
    /// rotation, color) on the server as a new revision with the same package. With a set
    /// store, the drawing grid's menu lists the sets a drawing is in, adds it to a set, lists
    /// the releases that had it on its own and, once release is enabled, releases it; sets
    /// themselves are browsed and released in <see cref="DrawingSetsForm"/>.
    /// The details pane previews the selected revision's geometry, downloaded shortly after
    /// the selection settles and cached per revision for the dialog's lifetime. The In This
    /// Nest tab lists the nest's drawings and edits their required quantities in place.
    /// </summary>
    public partial class DrawingLibraryForm : Form
    {
        private const string LoadingText = "Loading...";
        private const int DetailsWidth = 380;
        private const int PreviewCacheLimit = 64;
        private const int MaxQuantity = 99999;

        private readonly IDrawingLibraryStore library;
        private readonly string serverUrl;
        private readonly Nest nest;
        private readonly IDrawingSetStore sets;
        private readonly DrawingLibraryBrowseSession session;
        private readonly Dictionary<(Guid Id, int Revision), Drawing> previews = new();
        private CancellationTokenSource previewCancel;
        private (Guid Id, int Revision) previewKey;
        private Task<Drawing> previewLoad;
        private bool populatingNest;
        private bool adding;

        /// <summary>
        /// Reads the drawing a record's package holds for the preview, or null when the
        /// revision is gone; replaced by tests.
        /// </summary>
        internal Func<DrawingLibraryRecord, CancellationToken, Task<Drawing>> LoadPreview { get; set; }

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Shows the settings editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingLibraryEditSession, DrawingLibraryRecord> EditDrawing { get; set; }

        /// <summary>
        /// Chooses the set for Add to Set: Ok false when cancelled, otherwise the listed set or
        /// null for a new one; replaced by tests.
        /// </summary>
        internal Func<(bool Ok, DrawingSetRecord Listed)> ChooseSet { get; set; }

        /// <summary>Shows the set editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingSetEditSession, DrawingSetRecord> EditSet { get; set; }

        /// <summary>
        /// Shows the release dialog for a drawing read from the library, at a quantity; set by
        /// <see cref="EnableRelease"/> and replaced by tests.
        /// </summary>
        internal Action<DrawingLibraryRecord, int> ShowRelease { get; set; }

        /// <summary>Shows the releases that had a drawing on its own; replaced by tests.</summary>
        internal Action<DrawingLibraryRecord> ShowReleases { get; set; }

        /// <summary>
        /// The library drawings this dialog added or raised the quantity of, in order. The
        /// dialog stays open after each add, so callers check this rather than the dialog result.
        /// </summary>
        public List<Drawing> AddedDrawings { get; } = new();

        /// <summary>
        /// True once the In This Nest tab changed a drawing's required quantity; like an add,
        /// the change is already applied to the nest when the dialog closes.
        /// </summary>
        public bool QuantitiesChanged { get; private set; }

        /// <param name="sets">Enables Used in Sets and Add to Set; null hides them.</param>
        public DrawingLibraryForm(IDrawingLibraryStore library, string serverUrl, Nest nest, IDrawingSetStore sets = null)
        {
            InitializeComponent();
            Load += (_, _) =>
            {
                DialogBounds.FitToOwnerScreen(this);
                FitDetailsPane();
            };
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            this.nest = nest ?? throw new ArgumentNullException(nameof(nest));
            this.sets = sets;
            session = new DrawingLibraryBrowseSession(library, nest);
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
            EditDrawing = edit =>
            {
                using var form = new EditLibraryDrawingForm(edit);
                return form.ShowDialog(this) == DialogResult.OK ? form.Saved : null;
            };
            ChooseSet = () =>
            {
                using var form = new ChooseDrawingSetForm(sets, library, this.serverUrl);
                return form.ShowDialog(this) == DialogResult.OK ? (true, form.PickedSet) : (false, null);
            };
            EditSet = edit =>
            {
                using var form = new EditDrawingSetForm(edit, library, this.serverUrl);
                return form.ShowDialog(this) == DialogResult.OK ? form.Saved : null;
            };
            ShowReleases = record =>
            {
                using var form = new SetReleasesForm(sets, record);
                form.ShowDialog(this);
            };
            LoadPreview = async (record, token) =>
            {
                var bytes = await library.GetRevisionPackageAsync(record.Id, record.Revision, token);
                return bytes == null ? null : await Task.Run(() => DrawingPackage.Read(bytes).Drawing, token);
            };

            GridStyle.Apply(drawingGrid);
            GridStyle.Apply(nestGrid);
            nameColumn.Tag = DrawingSortField.Name;
            customerColumn.Tag = DrawingSortField.Customer;
            materialColumn.Tag = DrawingSortField.MaterialName;
            gradeColumn.Tag = DrawingSortField.MaterialGrade;
            thicknessColumn.Tag = DrawingSortField.Thickness;
            cutTypeColumn.Tag = DrawingSortField.CutType;
            revisionColumn.Tag = DrawingSortField.Revision;
            savedColumn.Tag = DrawingSortField.SavedAt;

            Text = $"Add from Drawing Library — {this.serverUrl}";

            if (sets != null)
                drawingGrid.ContextMenuStrip = drawingMenu;
            showAllCheckBox.Enabled = session.IsFilteredToNest;
            UpdateFilterLabel();
            UpdateNavigation();
            ShowDetails();
            RefreshNestTab();
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
            ShowDetails();
            UpdateCommands();
        }

        // The grid takes the spare width; the details pane keeps its width as the dialog resizes.
        private void FitDetailsPane()
        {
            splitContainer.Panel1MinSize = 320;
            splitContainer.Panel2MinSize = 300;
            var available = splitContainer.Width - splitContainer.SplitterWidth;
            if (available >= splitContainer.Panel1MinSize + splitContainer.Panel2MinSize)
            {
                splitContainer.SplitterDistance = System.Math.Clamp(available - DetailsWidth,
                    splitContainer.Panel1MinSize, available - splitContainer.Panel2MinSize);
            }
        }

        /// <summary>Fills the details pane for the selected record and schedules its preview.</summary>
        private void ShowDetails()
        {
            var record = SelectedRecord();
            previewTimer.Stop();
            previewCancel?.Cancel();

            detailNameLabel.Text = record?.Name ?? "No drawing selected";
            customerValue.Text = record == null ? "" : OrDash(record.Customer);
            materialValue.Text = record == null ? "" : OrDash($"{record.MaterialName} {record.MaterialGrade}".Trim());
            thicknessValue.Text = record == null ? "" : $"{FormatThickness(record.Thickness)} {UnitSuffix(record.Units)}";
            cuttingValue.Text = record == null ? "" : record.Settings?.CutFirst == true ? $"{record.CutType}, cut first" : record.CutType.ToString();
            revisionValue.Text = record == null ? "" : FormatRevision(record);
            notesBox.Text = record?.Notes ?? "";

            if (record == null)
            {
                previewBox.ShowDrawing(null);
                previewBox.SetInfo("Select a drawing to preview it.");
            }
            else if (previews.TryGetValue((record.Id, record.Revision), out var cached))
            {
                ShowPreview(cached);
            }
            else
            {
                previewBox.ShowDrawing(null);
                previewBox.SetInfo("Loading preview...");
                // Arrowing through the list should not download every row it passes.
                previewTimer.Start();
            }
        }

        private async void PreviewTimer_Tick(object sender, EventArgs e) =>
            await LoadSelectedPreviewAsync();

        /// <summary>
        /// Downloads and shows the selected revision's geometry unless it is cached. A newer
        /// selection cancels the request, and a result for a row no longer selected is only cached.
        /// </summary>
        internal async Task LoadSelectedPreviewAsync()
        {
            previewTimer.Stop();
            var record = SelectedRecord();
            if (record == null)
                return;

            var key = (record.Id, record.Revision);
            if (previews.TryGetValue(key, out var cached))
            {
                ShowPreview(cached);
                return;
            }

            // A request already loading this revision is shared rather than restarted.
            if (previewLoad == null || previewKey != key || previewCancel.IsCancellationRequested)
            {
                previewCancel?.Cancel();
                previewCancel = new CancellationTokenSource();
                previewKey = key;
                previewLoad = LoadPreview(record, previewCancel.Token);
            }

            var cancel = previewCancel;
            Drawing drawing = null;
            Exception failure = null;
            try
            {
                drawing = await previewLoad;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The preview is best-effort: whatever fails here, the dialog keeps working.
                failure = ex;
            }

            if (IsDisposed)
                return;
            // The await resumes on the dialog's thread only when that thread has a WinForms context.
            if (!InvokeRequired)
            {
                ApplyPreview(record, cancel, drawing, failure);
                return;
            }

            try
            {
                BeginInvoke(new Action(() => ApplyPreview(record, cancel, drawing, failure)));
            }
            catch (InvalidOperationException)
            {
                // The dialog closed meanwhile.
            }
        }

        private void ApplyPreview(DrawingLibraryRecord record, CancellationTokenSource cancel, Drawing drawing, Exception failure)
        {
            if (cancel.IsCancellationRequested || IsDisposed)
                return;
            if (failure != null)
            {
                previewBox.ShowDrawing(null);
                previewBox.SetInfo("Preview unavailable.", failure.Message);
                return;
            }

            var key = (record.Id, record.Revision);
            if (drawing != null)
            {
                // The package leaves color to the record; GetNextColor would advance the nest's palette.
                var color = System.Drawing.Color.FromArgb(record.Settings?.ColorArgb ?? 0);
                drawing.Color = color.A != 0 ? color : System.Drawing.Color.SteelBlue;
                if (previews.Count >= PreviewCacheLimit)
                    previews.Clear();
                previews[key] = drawing;
            }

            if (SelectedRecord() is { } current && (current.Id, current.Revision) == key)
                ShowPreview(drawing);
        }

        private void ShowPreview(Drawing drawing)
        {
            previewBox.ShowDrawing(drawing);
            if (drawing?.Program == null)
            {
                previewBox.SetInfo("This revision is no longer on the server.");
                return;
            }

            var size = drawing.Program.BoundingBox().Size;
            var units = UnitSuffix(SelectedRecord()?.Units ?? Units.Inches);
            previewBox.SetInfo($"{size.Length:0.###} x {size.Width:0.###} {units}");
        }

        private async void DrawingGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                await AddSelectedAsync();
        }

        private async void AddButton_Click(object sender, EventArgs e) => await AddSelectedAsync();

        /// <summary>
        /// Lets the drawing menu release a drawing on its own: nests are saved through
        /// <paramref name="nests"/> and each is opened with <paramref name="openNest"/>; a nest
        /// <paramref name="isNestOpen"/> reports as open in a tab is never merged into.
        /// </summary>
        public void EnableRelease(INestRepository nests, Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null)
        {
            if (sets == null)
                return;
            ArgumentNullException.ThrowIfNull(nests);
            ArgumentNullException.ThrowIfNull(openNest);
            ShowRelease = (record, quantity) =>
            {
                using var form = new ReleaseSetForm(sets, library, nests, serverUrl, Array.Empty<DrawingSetRecord>(),
                    new[] { new SetReleaseDrawingLine(record, quantity) }, openNest)
                { IsNestOpen = isNestOpen ?? (_ => false) };
                form.ShowDialog(this);
            };
            releaseSeparator.Visible = true;
            releaseMenuItem.Visible = true;
        }

        // A right-click selects the row under the pointer, so the menu acts on what was clicked.
        private void DrawingGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !drawingGrid.Rows[e.RowIndex].Selected)
                drawingGrid.CurrentCell = drawingGrid.Rows[e.RowIndex].Cells[e.ColumnIndex >= 0 ? e.ColumnIndex : 0];
        }

        private void DrawingMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var record = SelectedRecord();
            usedInSetsMenuItem.Enabled = record != null && !adding;
            addToSetMenuItem.Enabled = record != null && !adding;
            releaseMenuItem.Enabled = record is { Archived: false } && !adding && ShowRelease != null;
            releasesMenuItem.Enabled = record != null && !adding;
        }

        private async void ReleaseMenuItem_Click(object sender, EventArgs e) => await ReleaseSelectedAsync();

        private void ReleasesMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedRecord() is { } record && sets != null)
                ShowReleases(record);
        }

        /// <summary>
        /// Reads the selected drawing's current record and opens the release dialog with it at
        /// the entered quantity; more drawings and sets can be added there.
        /// </summary>
        internal async Task ReleaseSelectedAsync()
        {
            var listed = SelectedRecord();
            if (listed == null || ShowRelease == null || adding)
                return;

            DrawingLibraryRecord current;
            adding = true;
            UpdateCommands();
            statusLabel.Text = $"Reading '{listed.Name}'...";
            try
            {
                current = await library.GetAsync(listed.Id);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                Fail($"Could not read '{listed.Name}': {ex.Message}");
                return;
            }
            finally
            {
                adding = false;
                if (!IsDisposed)
                    UpdateCommands();
            }

            if (IsDisposed)
                return;
            statusLabel.Text = "";
            if (current == null || current.Archived)
            {
                ShowMessage(current == null
                    ? $"'{listed.Name}' is no longer in the library."
                    : $"'{listed.Name}' was archived. Restore it to release it.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowRelease(current, (int)quantityBox.Value);
        }

        private async void UsedInSetsMenuItem_Click(object sender, EventArgs e) => await ShowUsedInSetsAsync();

        private async void AddToSetMenuItem_Click(object sender, EventArgs e) => await AddSelectedToSetAsync();

        /// <summary>Lists the drawing sets whose current revision contains the selected drawing.</summary>
        internal async Task ShowUsedInSetsAsync()
        {
            var record = SelectedRecord();
            if (record == null || sets == null || adding)
                return;

            adding = true;
            UpdateCommands();
            statusLabel.Text = $"Finding the sets that use '{record.Name}'...";
            try
            {
                var page = await sets.GetSetsContainingAsync(record.Id);
                if (IsDisposed)
                    return;
                statusLabel.Text = "";
                var items = page?.Items ?? Array.Empty<DrawingSetRecord>();
                if (items.Count == 0)
                {
                    ShowMessage($"'{record.Name}' is not in any drawing set.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var lines = items.Select(set => "- " + set.Name
                    + (string.IsNullOrWhiteSpace(set.Customer) ? "" : $" ({set.Customer})")
                    + (set.Archived ? " (archived)" : ""));
                var more = page.Total > items.Count ? $"{Environment.NewLine}...and {page.Total - items.Count:N0} more." : "";
                ShowMessage($"'{record.Name}' is in {page.Total:N0} drawing set{(page.Total == 1 ? "" : "s")}:"
                    + Environment.NewLine + string.Join(Environment.NewLine, lines) + more,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                // ServerTooOldException is an IOException too.
                Fail($"Could not list the sets that use '{record.Name}': {ex.Message}");
            }
            finally
            {
                adding = false;
                if (!IsDisposed)
                    UpdateCommands();
            }
        }

        /// <summary>
        /// Adds the selected drawing, with the entered quantity, to a chosen set (or a new one)
        /// and opens the set editor so the operator reviews and saves it.
        /// </summary>
        internal async Task AddSelectedToSetAsync()
        {
            var record = SelectedRecord();
            if (record == null || sets == null || adding)
                return;

            var (ok, listed) = ChooseSet();
            if (!ok || IsDisposed)
                return;

            DrawingSetRecord current = null;
            if (listed != null)
            {
                adding = true;
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
                    adding = false;
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
                        : $"Set '{listed.Name}' was archived. Restore it before adding drawings.",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var edit = new DrawingSetEditSession(sets, current, Environment.UserName);
            var added = edit.AddDrawings(new[] { record }, (int)quantityBox.Value);
            if (added.Added.Count == 0)
            {
                ShowMessage($"'{record.Name}' is already in set '{current.Name}'. Edit the set to change its quantity.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var saved = EditSet(edit);
            if (saved == null || IsDisposed)
                return;
            statusLabel.Text = $"Added '{record.Name}' to set '{saved.Name}' (revision {saved.Revision}).";
        }

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
                RefreshNestTab();
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

            ShowDetails();
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

        /// <summary>
        /// Lists the nest's drawings (cutoffs excluded) with their required, nested and
        /// remaining quantities, and counts them on the tab.
        /// </summary>
        private void RefreshNestTab()
        {
            if (nestGrid.IsCurrentCellInEditMode)
                nestGrid.CancelEdit();

            populatingNest = true;
            try
            {
                nestGrid.Rows.Clear();
                foreach (var drawing in nest.Drawings.Where(drawing => !drawing.IsCutOff))
                {
                    var index = nestGrid.Rows.Add(
                        drawing.Name,
                        LibraryText(drawing),
                        drawing.Quantity.Required,
                        drawing.Quantity.Nested,
                        drawing.Quantity.Remaining);
                    nestGrid.Rows[index].Tag = drawing;
                }
            }
            finally
            {
                populatingNest = false;
            }

            nestTab.Text = $"In This Nest ({nestGrid.Rows.Count:N0})";
            UpdateNestSummary();
        }

        // One click on a Required cell starts editing it; the other columns are read-only.
        private void NestGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == nestRequiredColumn.Index)
                nestGrid.BeginEdit(true);
        }

        private void NestGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != nestRequiredColumn.Index)
                return;

            var valid = int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture),
                NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
                && value >= 0 && value <= MaxQuantity;
            nestGrid.Rows[e.RowIndex].ErrorText = valid ? "" : $"Enter a whole number from 0 to {MaxQuantity:N0}.";
            e.Cancel = !valid;
        }

        private void NestGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (populatingNest || e.RowIndex < 0 || e.ColumnIndex != nestRequiredColumn.Index)
                return;

            var row = nestGrid.Rows[e.RowIndex];
            if (row.Tag is not Drawing drawing)
                return;

            // CellValidating runs only when focus leaves the cell; a commit by EndEdit skips it.
            if (row.Cells[e.ColumnIndex].Value is int value && value >= 0 && value <= MaxQuantity)
            {
                SetRequiredQuantity(row, drawing, value);
                return;
            }

            populatingNest = true;
            try
            {
                row.Cells[e.ColumnIndex].Value = drawing.Quantity.Required;
            }
            finally
            {
                populatingNest = false;
            }
        }

        private void SetRequiredQuantity(DataGridViewRow row, Drawing drawing, int value)
        {
            if (drawing.Quantity.Required == value)
                return;

            var before = drawing.Quantity.Required;
            drawing.Quantity.Required = value;
            QuantitiesChanged = true;
            populatingNest = true;
            try
            {
                row.Cells[nestRemainingColumn.Index].Value = drawing.Quantity.Remaining;
            }
            finally
            {
                populatingNest = false;
            }

            statusLabel.Text = $"'{drawing.Name}' now requires {value:N0} (was {before:N0}).";
            UpdateNestSummary();
        }

        private void UpdateNestSummary()
        {
            var drawings = nest.Drawings.Where(drawing => !drawing.IsCutOff).ToList();
            nestSummaryLabel.Text = drawings.Count == 0
                ? "This nest has no drawings yet. Add them from the Drawings tab."
                : $"{drawings.Count:N0} drawing{(drawings.Count == 1 ? "" : "s")}: "
                    + $"{drawings.Sum(drawing => drawing.Quantity.Required):N0} parts required, "
                    + $"{drawings.Sum(drawing => drawing.Quantity.Nested):N0} nested, "
                    + $"{drawings.Sum(drawing => drawing.Quantity.Remaining):N0} remaining.";
        }

        private string LibraryText(Drawing drawing)
        {
            if (drawing.Library == null)
                return "Not from the library";
            return string.Equals(drawing.Library.ServerKey, serverUrl, StringComparison.OrdinalIgnoreCase)
                ? $"Revision {drawing.Library.Revision}"
                : $"Revision {drawing.Library.Revision} (other server)";
        }

        private static string FormatRevision(DrawingLibraryRecord record)
        {
            var text = record.Revision.ToString(CultureInfo.CurrentCulture);
            if (record.SavedAt != default)
                text += $", saved {record.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
            if (!string.IsNullOrWhiteSpace(record.SavedBy))
                text += $" by {record.SavedBy}";
            return text;
        }

        private static string UnitSuffix(Units units) => units == Units.Millimeters ? "mm" : "in";

        private static string OrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    }
}

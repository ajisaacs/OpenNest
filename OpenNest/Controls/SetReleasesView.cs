using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// The Releases tab of <see cref="DrawingSetsForm"/>: every release on the nest server,
    /// newest first, a bounded page at a time, each with what it released ("Kit A ×2; Bracket
    /// ×4") and its progress ("2 of 3 cut"), and the nests of the selected release with what
    /// the release did to each and their current status, or Deleted. A filter narrows the list
    /// to the releases that include one set, or one library drawing on its own; the filter
    /// shows above the list until Clear Filter. The server joins every nest's current metadata
    /// on each read. New Release opens the release dialog empty; the list re-reads once a
    /// release is recorded. Undo Release deletes the nests a release created and restores the
    /// drafts it merged into, all or nothing; nests changed since can be left as they are.
    /// </summary>
    public partial class SetReleasesView : UserControl
    {
        private const int PageSize = 100;

        private readonly int releasedWidth;
        private readonly DataGridViewCellStyle releasedStyle;
        private IDrawingSetStore sets;
        private DrawingSetRecord set;
        private DrawingLibraryRecord drawing;
        private SetReleasePage page;
        private int offset;
        private int pageRequest;
        private bool undoing;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>
        /// Shows the release dialog empty and returns true when it recorded a release; set by
        /// <see cref="EnableRelease"/> and replaced by tests.
        /// </summary>
        internal Func<bool> ShowNewRelease { get; set; }

        public SetReleasesView()
        {
            InitializeComponent();
            GridStyle.Apply(releaseGrid);
            GridStyle.Apply(nestGrid);
            releasedWidth = multiplierColumn.Width;
            releasedStyle = multiplierColumn.DefaultCellStyle;
            ApplyFilter();
        }

        /// <summary>True after a page of releases has been requested at least once.</summary>
        public bool HasLoaded { get; private set; }

        /// <summary>The set the list is narrowed to, or null.</summary>
        public DrawingSetRecord FilterSet => set;

        /// <summary>The drawing the list is narrowed to, or null.</summary>
        public DrawingLibraryRecord FilterDrawing => drawing;

        public void Initialize(IDrawingSetStore sets)
        {
            if (this.sets != null)
                throw new InvalidOperationException("The releases view is already initialized.");
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            ShowMessage = (text, buttons, icon) => MessageBox.Show(FindForm(), text, FindForm()?.Text ?? "Releases", buttons, icon);
        }

        /// <summary>
        /// Shows New Release: the release dialog starts empty, saves nests through
        /// <paramref name="nests"/> and opens each with <paramref name="openNest"/>; a nest
        /// <paramref name="isNestOpen"/> reports as open in a tab is never merged into.
        /// </summary>
        public void EnableRelease(IDrawingLibrary library, INestRepository nests, string serverUrl,
            Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null)
        {
            ArgumentNullException.ThrowIfNull(library);
            ArgumentNullException.ThrowIfNull(nests);
            ArgumentNullException.ThrowIfNull(openNest);
            if (sets == null)
                throw new InvalidOperationException("New Release needs an initialized releases view.");
            ShowNewRelease = () =>
            {
                using var form = new ReleaseSetForm(sets, library, nests, serverUrl, Array.Empty<DrawingSetRecord>(),
                    Array.Empty<SetReleaseDrawingLine>(), openNest)
                { IsNestOpen = isNestOpen ?? (_ => false) };
                form.ShowDialog(FindForm());
                return form.Result?.Release != null;
            };
            newReleaseButton.Visible = true;
        }

        /// <summary>
        /// Narrows the list to the releases that include <paramref name="set"/>, or that had
        /// <paramref name="drawing"/> on its own outside any set; both null lists every release.
        /// The next read starts at the newest page.
        /// </summary>
        public void SetFilter(DrawingSetRecord set, DrawingLibraryRecord drawing)
        {
            if (set != null && drawing != null)
                throw new ArgumentException("Filter by a set or a drawing, not both.");
            this.set = set;
            this.drawing = drawing;
            offset = 0;
            ApplyFilter();
        }

        /// <summary>Re-reads the current page (the first call reads the newest).</summary>
        public Task RefreshAsync() => LoadPageAsync(offset);

        /// <summary>Lists every release again, from the newest.</summary>
        internal Task ClearFilterAsync()
        {
            SetFilter(null, null);
            return LoadPageAsync(0);
        }

        private async void ClearFilterButton_Click(object sender, EventArgs e) => await ClearFilterAsync();

        // Per set the release's revision of it and its quantity; per drawing its quantity; across
        // all releases, everything each one had.
        private void ApplyFilter()
        {
            var all = set == null && drawing == null;
            revisionColumn.Visible = set != null;
            multiplierColumn.HeaderText = all ? "Released" : "Quantity";
            multiplierColumn.Width = all ? 320 : releasedWidth;
            multiplierColumn.DefaultCellStyle = all ? null : releasedStyle;
            filterTable.Visible = !all;
            filterLabel.Text = set != null ? $"Releases of set '{set.Name}'"
                : drawing != null ? $"Releases of drawing '{drawing.Name}' on its own"
                : "";
        }

        private async void NewReleaseButton_Click(object sender, EventArgs e) => await NewReleaseAsync();

        /// <summary>Opens the empty release dialog and, once it recorded a release, shows the newest page.</summary>
        internal async Task NewReleaseAsync()
        {
            if (ShowNewRelease == null || undoing)
                return;
            var recorded = ShowNewRelease();
            if (recorded && !IsDisposed)
                await LoadPageAsync(0);
        }

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await LoadPageAsync(System.Math.Max(0, offset - PageSize));

        private async void NextButton_Click(object sender, EventArgs e) => await LoadPageAsync(offset + PageSize);

        private void ReleaseGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            ShowNests(releaseGrid.CurrentRow?.Tag as SetReleaseRecord);
            UpdateUndo();
        }

        private async void UndoButton_Click(object sender, EventArgs e) => await UndoSelectedAsync();

        private SetReleaseRecord SelectedRelease => releaseGrid.CurrentRow?.Tag as SetReleaseRecord;

        private void UpdateUndo() => undoButton.Enabled = !undoing && SelectedRelease is { IsUndone: false };

        /// <summary>
        /// Confirms, then undoes the selected release. When nests changed since it, lists them and
        /// offers to undo the others, leaving those as they are.
        /// </summary>
        internal async Task UndoSelectedAsync()
        {
            if (undoing || SelectedRelease is not { IsUndone: false } release)
                return;
            var created = release.Nests.Count(n => n.Kind == SetReleaseNestKind.Created);
            var merged = release.Nests.Count - created;
            var question = "Undo this release?"
                + (created == 0 ? "" : $"\n\n{created} nest{(created == 1 ? " it created is" : "s it created are")} deleted (they can be recovered).")
                + (merged == 0 ? "" : $"\n\n{merged} draft{(merged == 1 ? " it merged into goes" : "s it merged into go")} back to how {(merged == 1 ? "it was" : "they were")} before.");
            if (ShowMessage(question, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            undoing = true;
            UpdateUndo();
            try
            {
                var request = new SetReleaseUndoRequest { UndoneBy = Environment.UserName };
                try
                {
                    await sets.UndoReleaseAsync(release.Id, request);
                }
                catch (SetReleaseUndoConflictException conflict) when (conflict.Conflict.Reason == SetReleaseUndoRefusal.NestsChanged
                    && conflict.Conflict.ChangedNests.Count < release.Nests.Count)
                {
                    if (IsDisposed || ShowMessage(SetReleaseText.UndoRefusal(conflict.Conflict) + "\n\nUndo the others now?",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    request.SkipNests = conflict.Conflict.ChangedNests.Select(n => n.Id).ToList();
                    await sets.UndoReleaseAsync(release.Id, request);
                }
            }
            catch (SetReleaseUndoConflictException conflict)
            {
                if (!IsDisposed)
                    ShowMessage(SetReleaseText.UndoRefusal(conflict.Conflict), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException
                || ex is System.Text.Json.JsonException)
            {
                if (!IsDisposed)
                    ShowMessage($"Could not undo the release: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                undoing = false;
            }

            if (!IsDisposed)
                await LoadPageAsync(offset);
        }

        /// <summary>Reads one page of releases; a later request discards an earlier one's result.</summary>
        internal async Task LoadPageAsync(int requestedOffset)
        {
            if (sets == null)
                return;
            HasLoaded = true;
            var request = ++pageRequest;
            var (filterSet, filterDrawing) = (set, drawing);
            previousButton.Enabled = nextButton.Enabled = false;
            summaryLabel.Text = "Loading...";
            try
            {
                var loaded = await sets.QueryReleasesAsync(setId: filterSet?.Id, drawingId: filterDrawing?.Id, offset: requestedOffset, limit: PageSize);
                if (IsDisposed || request != pageRequest)
                    return;

                page = loaded;
                offset = requestedOffset;
                releaseGrid.Rows.Clear();
                nestGrid.Rows.Clear();
                foreach (var release in loaded.Items)
                {
                    // Tagged before it is added: adding the first row selects it, which shows its nests.
                    // This set's or drawing's line, with the release's other sets and drawings
                    // counted; the all-releases view names everything the release had.
                    var revision = 0;
                    string released;
                    if (filterSet == null && filterDrawing == null)
                    {
                        released = Contents(release);
                    }
                    else
                    {
                        int quantity, otherSets, otherDrawings;
                        if (filterSet != null)
                        {
                            var line = release.Sets.First(s => s.SetId == filterSet.Id);
                            (revision, quantity) = (line.SetRevision, line.Quantity);
                            (otherSets, otherDrawings) = (release.Sets.Count - 1, release.Drawings.Count);
                        }
                        else
                        {
                            var line = release.Drawings.First(d => d.DrawingId == filterDrawing.Id);
                            quantity = line.Quantity;
                            (otherSets, otherDrawings) = (release.Sets.Count, release.Drawings.Count - 1);
                        }
                        var others = new[]
                        {
                            otherSets == 0 ? null : $"{otherSets} {(filterSet != null ? "other " : "")}{(otherSets == 1 ? "set" : "sets")}",
                            otherDrawings == 0 ? null : $"{otherDrawings} {(filterDrawing != null ? "other " : "")}{(otherDrawings == 1 ? "drawing" : "drawings")}",
                        }.Where(o => o != null).ToList();
                        released = others.Count == 0 ? $"×{quantity}" : $"×{quantity} (+{string.Join(", ", others)})";
                    }
                    var row = new DataGridViewRow { Tag = release };
                    row.CreateCells(releaseGrid,
                        release.ReleasedAt == default ? "" : release.ReleasedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        release.ReleasedBy,
                        revision,
                        released,
                        release.Nests.Count,
                        SetReleaseText.Progress(release),
                        release.Notes);
                    releaseGrid.Rows.Add(row);
                }

                // The grid only sometimes makes the first row current by itself (it depends on focus).
                if (releaseGrid.CurrentCell == null && releaseGrid.Rows.Count > 0)
                    releaseGrid.CurrentCell = releaseGrid.Rows[0].Cells[0];

                summaryLabel.Text = loaded.Total == 0
                    ? filterSet != null ? "This set has not been released yet."
                        : filterDrawing != null ? "This drawing has not been released on its own yet."
                        : "Nothing has been released yet."
                    : $"Releases {offset + 1:N0}-{offset + loaded.Items.Count:N0} of {loaded.Total:N0}";
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!IsDisposed && request == pageRequest)
                    summaryLabel.Text = $"Could not read the releases: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed && request == pageRequest)
                {
                    previousButton.Enabled = offset > 0;
                    UpdateUndo();
                    nextButton.Enabled = page != null && offset + page.Items.Count < page.Total;
                }
            }
        }

        /// <summary>"Kit A ×2, Kit B; Bracket ×4": the sets (kits past one) and drawings a release had.</summary>
        internal static string Contents(SetReleaseRecord release)
        {
            var sets = release.Sets.Select(s =>
                (string.IsNullOrEmpty(s.SetName) ? $"set {s.SetId}" : s.SetName) + (s.Quantity == 1 ? "" : $" ×{s.Quantity}"));
            var drawings = release.Drawings.Select(d =>
                (string.IsNullOrEmpty(d.DrawingName) ? $"drawing {d.DrawingId}" : d.DrawingName) + $" ×{d.Quantity}");
            return string.Join("; ", new[] { string.Join(", ", sets), string.Join(", ", drawings) }.Where(p => p.Length > 0));
        }

        private void ShowNests(SetReleaseRecord release)
        {
            nestGrid.Rows.Clear();
            if (release == null)
            {
                nestsGroup.Text = "Nests of the selected release";
                return;
            }

            foreach (var nest in release.Nests)
            {
                var record = nest.Nest;
                nestGrid.Rows.Add(
                    record?.Name ?? nest.NestId.ToString(),
                    SetReleaseText.Group(nest),
                    SetReleaseText.Change(nest),
                    SetReleaseText.NestState(nest),
                    record?.Customer ?? "",
                    record == null || record.DateModified == default
                        ? ""
                        : record.DateModified.ToString("g", CultureInfo.CurrentCulture));
            }

            nestsGroup.Text = $"Nests of the release on {release.ReleasedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}: "
                + SetReleaseText.Progress(release);
        }
    }
}

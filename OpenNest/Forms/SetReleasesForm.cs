using System;
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
    /// Drawing Sets &gt; Releases: the releases that include one set, or one library drawing on
    /// its own (Drawings tab &gt; Releases), newest first, a bounded
    /// page at a time, each with its progress ("2 of 3 cut"), and the nests of the selected
    /// release with what the release did to each and their current status, or Deleted. The
    /// server joins every nest's current metadata on each read. Undo Release deletes the nests
    /// a release created and restores the drafts it merged into, all or nothing; nests changed
    /// since can be left as they are.
    /// </summary>
    public partial class SetReleasesForm : Form
    {
        private const int PageSize = 100;

        private readonly IDrawingSetStore sets;
        private readonly DrawingSetRecord set;
        private readonly DrawingLibraryRecord drawing;
        private SetReleasePage page;
        private int offset;
        private int pageRequest;
        private bool undoing;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        public SetReleasesForm(IDrawingSetStore sets, DrawingSetRecord set)
            : this(sets)
        {
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            Text = $"Releases of '{set.Name}'";
        }

        /// <summary>The releases that had <paramref name="drawing"/> on its own, outside any set.</summary>
        public SetReleasesForm(IDrawingSetStore sets, DrawingLibraryRecord drawing)
            : this(sets)
        {
            this.drawing = drawing ?? throw new ArgumentNullException(nameof(drawing));
            Text = $"Releases of drawing '{drawing.Name}'";
            revisionColumn.Visible = false;
            multiplierColumn.HeaderText = "Quantity";
        }

        private SetReleasesForm(IDrawingSetStore sets)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            GridStyle.Apply(releaseGrid);
            GridStyle.Apply(nestGrid);
            ShowMessage = (text, buttons, icon) => MessageBox.Show(this, text, Text, buttons, icon);
        }

        private async void SetReleasesForm_Shown(object sender, EventArgs e) => await LoadPageAsync(0);

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
            var request = ++pageRequest;
            previousButton.Enabled = nextButton.Enabled = false;
            summaryLabel.Text = "Loading...";
            try
            {
                var loaded = await sets.QueryReleasesAsync(setId: set?.Id, drawingId: drawing?.Id, offset: requestedOffset, limit: PageSize);
                if (IsDisposed || request != pageRequest)
                    return;

                page = loaded;
                offset = requestedOffset;
                releaseGrid.Rows.Clear();
                nestGrid.Rows.Clear();
                foreach (var release in loaded.Items)
                {
                    // Tagged before it is added: adding the first row selects it, which shows its nests.
                    // This set's or drawing's line; the release's other sets and drawings are counted.
                    int revision, quantity, otherSets, otherDrawings;
                    if (set != null)
                    {
                        var line = release.Sets.First(s => s.SetId == set.Id);
                        (revision, quantity) = (line.SetRevision, line.Quantity);
                        (otherSets, otherDrawings) = (release.Sets.Count - 1, release.Drawings.Count);
                    }
                    else
                    {
                        var line = release.Drawings.First(d => d.DrawingId == drawing.Id);
                        (revision, quantity) = (0, line.Quantity);
                        (otherSets, otherDrawings) = (release.Sets.Count, release.Drawings.Count - 1);
                    }
                    var others = new[]
                    {
                        otherSets == 0 ? null : $"{otherSets} {(set != null ? "other " : "")}{(otherSets == 1 ? "set" : "sets")}",
                        otherDrawings == 0 ? null : $"{otherDrawings} {(drawing != null ? "other " : "")}{(otherDrawings == 1 ? "drawing" : "drawings")}",
                    }.Where(o => o != null).ToList();
                    var row = new DataGridViewRow { Tag = release };
                    row.CreateCells(releaseGrid,
                        release.ReleasedAt == default ? "" : release.ReleasedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        release.ReleasedBy,
                        revision,
                        others.Count == 0 ? $"×{quantity}" : $"×{quantity} (+{string.Join(", ", others)})",
                        release.Nests.Count,
                        SetReleaseText.Progress(release),
                        release.Notes);
                    releaseGrid.Rows.Add(row);
                }

                // The grid only sometimes makes the first row current by itself (it depends on focus).
                if (releaseGrid.CurrentCell == null && releaseGrid.Rows.Count > 0)
                    releaseGrid.CurrentCell = releaseGrid.Rows[0].Cells[0];

                summaryLabel.Text = loaded.Total == 0
                    ? set != null ? "This set has not been released yet." : "This drawing has not been released on its own yet."
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

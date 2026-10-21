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
    /// Drawing Sets &gt; Releases: one set's releases, newest first, a bounded page at a time,
    /// each with its progress ("2 of 3 cut"), and the nests of the selected release with their
    /// current status, or Deleted. The server joins every nest's current metadata on each read.
    /// </summary>
    public partial class SetReleasesForm : Form
    {
        private const int PageSize = 100;

        private readonly IDrawingSetStore sets;
        private readonly DrawingSetRecord set;
        private SetReleasePage page;
        private int offset;
        private int pageRequest;

        public SetReleasesForm(IDrawingSetStore sets, DrawingSetRecord set)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            GridStyle.Apply(releaseGrid);
            GridStyle.Apply(nestGrid);
            Text = $"Releases of '{set.Name}'";
        }

        private async void SetReleasesForm_Shown(object sender, EventArgs e) => await LoadPageAsync(0);

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await LoadPageAsync(System.Math.Max(0, offset - PageSize));

        private async void NextButton_Click(object sender, EventArgs e) => await LoadPageAsync(offset + PageSize);

        private void ReleaseGrid_CurrentCellChanged(object sender, EventArgs e) =>
            ShowNests(releaseGrid.CurrentRow?.Tag as SetReleaseRecord);

        /// <summary>Reads one page of releases; a later request discards an earlier one's result.</summary>
        internal async Task LoadPageAsync(int requestedOffset)
        {
            var request = ++pageRequest;
            previousButton.Enabled = nextButton.Enabled = false;
            summaryLabel.Text = "Loading...";
            try
            {
                var loaded = await sets.QueryReleasesAsync(setId: set.Id, offset: requestedOffset, limit: PageSize);
                if (IsDisposed || request != pageRequest)
                    return;

                page = loaded;
                offset = requestedOffset;
                releaseGrid.Rows.Clear();
                nestGrid.Rows.Clear();
                foreach (var release in loaded.Items)
                {
                    // Tagged before it is added: adding the first row selects it, which shows its nests.
                    // This set's line; a release of several sets says how many others it had.
                    var line = release.Sets.First(s => s.SetId == set.Id);
                    var others = release.Sets.Count - 1;
                    var row = new DataGridViewRow { Tag = release };
                    row.CreateCells(releaseGrid,
                        release.ReleasedAt == default ? "" : release.ReleasedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        release.ReleasedBy,
                        line.SetRevision,
                        others == 0 ? $"×{line.Quantity}" : $"×{line.Quantity} (+{others} other {(others == 1 ? "set" : "sets")})",
                        release.Nests.Count,
                        SetReleaseText.Progress(release),
                        release.Notes);
                    releaseGrid.Rows.Add(row);
                }

                // The grid only sometimes makes the first row current by itself (it depends on focus).
                if (releaseGrid.CurrentCell == null && releaseGrid.Rows.Count > 0)
                    releaseGrid.CurrentCell = releaseGrid.Rows[0].Cells[0];

                summaryLabel.Text = loaded.Total == 0
                    ? "This set has not been released yet."
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

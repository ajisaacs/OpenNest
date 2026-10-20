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
    /// Drawing Sets &gt; History: one set's immutable revisions, newest first, a bounded page at
    /// a time, and the members of the selected revision. Members are shown with their
    /// drawings' current library details, as the server joins them.
    /// </summary>
    public partial class DrawingSetHistoryForm : Form
    {
        private const int PageSize = 100;

        private readonly IDrawingSetStore sets;
        private readonly DrawingSetRecord set;
        private DrawingSetPage page;
        private int offset;
        private int pageRequest;
        private int memberRequest;

        public DrawingSetHistoryForm(IDrawingSetStore sets, DrawingSetRecord set)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            GridStyle.Apply(revisionGrid);
            GridStyle.Apply(memberGrid);
            Text = $"History of '{set.Name}'";
        }

        private async void DrawingSetHistoryForm_Shown(object sender, EventArgs e) => await LoadPageAsync(0);

        private async void PreviousButton_Click(object sender, EventArgs e) =>
            await LoadPageAsync(System.Math.Max(0, offset - PageSize));

        private async void NextButton_Click(object sender, EventArgs e) => await LoadPageAsync(offset + PageSize);

        private async void RevisionGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            if (revisionGrid.CurrentRow?.Tag is DrawingSetRecord revision)
                await LoadMembersAsync(revision.Revision);
        }

        /// <summary>Reads one page of revisions; a later request discards an earlier one's result.</summary>
        internal async Task LoadPageAsync(int requestedOffset)
        {
            var request = ++pageRequest;
            previousButton.Enabled = nextButton.Enabled = false;
            summaryLabel.Text = "Loading...";
            try
            {
                var loaded = await sets.GetRevisionsAsync(set.Id, requestedOffset, PageSize);
                if (IsDisposed || request != pageRequest)
                    return;
                if (loaded == null)
                {
                    summaryLabel.Text = "This set no longer exists on the server.";
                    return;
                }

                page = loaded;
                offset = requestedOffset;
                revisionGrid.Rows.Clear();
                foreach (var revision in loaded.Items)
                {
                    // Tagged before it is added: adding the first row selects it, which loads its members.
                    var row = new DataGridViewRow { Tag = revision };
                    row.CreateCells(revisionGrid,
                        revision.Revision,
                        revision.SavedAt == default ? "" : revision.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        revision.SavedBy,
                        revision.Name,
                        revision.Customer,
                        revision.MemberCount,
                        revision.Archived ? "Archived" : "");
                    revisionGrid.Rows.Add(row);
                }

                // The grid only sometimes makes the first row current by itself (it depends on focus).
                if (revisionGrid.CurrentCell == null && revisionGrid.Rows.Count > 0)
                    revisionGrid.CurrentCell = revisionGrid.Rows[0].Cells[0];

                summaryLabel.Text = loaded.Items.Count == 0
                    ? $"No revisions on this page ({loaded.Total:N0} in total)."
                    : $"Revisions {offset + 1:N0}-{offset + loaded.Items.Count:N0} of {loaded.Total:N0}";
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!IsDisposed && request == pageRequest)
                    summaryLabel.Text = $"Could not read the set history: {ex.Message}";
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

        private async Task LoadMembersAsync(int revision)
        {
            var request = ++memberRequest;
            memberGrid.Rows.Clear();
            membersGroup.Text = $"Drawings in revision {revision}: loading...";
            try
            {
                var record = await sets.GetRevisionAsync(set.Id, revision);
                if (IsDisposed || request != memberRequest)
                    return;
                if (record == null)
                {
                    membersGroup.Text = $"Revision {revision} is no longer on the server.";
                    return;
                }

                foreach (var member in record.Members)
                {
                    var drawing = member.Drawing;
                    memberGrid.Rows.Add(
                        drawing?.Name ?? member.DrawingId.ToString(),
                        member.Quantity,
                        drawing == null ? "" : string.Join(" ", new[] { drawing.MaterialName, drawing.MaterialGrade }.Where(s => !string.IsNullOrWhiteSpace(s))),
                        drawing == null ? "" : drawing.Thickness.ToString("0.####", CultureInfo.CurrentCulture),
                        drawing?.CutType.ToString() ?? "");
                }

                membersGroup.Text = $"Drawings in revision {revision} ({record.Members.Count:N0})";
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (!IsDisposed && request == memberRequest)
                    membersGroup.Text = $"Could not read revision {revision}: {ex.Message}";
            }
        }
    }
}

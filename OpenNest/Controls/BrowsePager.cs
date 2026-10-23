using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Controls
{
    /// <summary>
    /// Drives a grid that pages through a <see cref="BrowseSession{TPage, TSort}"/>: the summary
    /// line, Previous/Next, sort glyphs and the failure text. The owner fills the rows
    /// (<see cref="Populate"/>) and enables its own commands (<see cref="Settled"/>).
    /// </summary>
    internal sealed class BrowsePager<TPage, TSort>
        where TPage : class, IBrowsePage
        where TSort : struct, Enum
    {
        private const string LoadingText = "Loading...";

        private readonly BrowseSession<TPage, TSort> session;
        private readonly DataGridView grid;
        private readonly Label summary;
        private readonly Button previous;
        private readonly Button next;
        private readonly string listName;

        /// <param name="listName">What failed to load, as in "Could not load the drawing sets".</param>
        public BrowsePager(BrowseSession<TPage, TSort> session, DataGridView grid, Label summary,
            Button previous, Button next, string listName)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.grid = grid;
            this.summary = summary;
            this.previous = previous;
            this.next = next;
            this.listName = listName;
        }

        /// <summary>Fills the grid from an applied page.</summary>
        public Action<TPage> Populate { get; init; }

        /// <summary>Runs after every request that leaves the owner open, to enable its commands.</summary>
        public Action Settled { get; init; }

        /// <summary>Runs after a failed request has cleared the grid.</summary>
        public Action Failed { get; init; }

        /// <summary>
        /// Starts a session request and renders the page it applies. A failure clears the grid
        /// and shows why in the summary line; a request that was declined or superseded renders
        /// nothing of its own. Nothing is touched once the owner is disposed.
        /// </summary>
        public async Task RunAsync(Func<Task<bool>> request)
        {
            summary.Text = LoadingText;
            try
            {
                var started = request();
                UpdateNavigation();
                if (await started && !grid.IsDisposed)
                    Populate?.Invoke(session.Page);
            }
            catch (Exception ex)
            {
                if (!grid.IsDisposed)
                {
                    grid.Rows.Clear();
                    summary.Text = ex is ServerTooOldException
                        ? ex.Message
                        : $"Could not load {listName}: {ex.Message}";
                    Failed?.Invoke();
                }
            }
            finally
            {
                if (!grid.IsDisposed)
                {
                    UpdateNavigation();
                    if (!session.IsLoading && summary.Text == LoadingText)
                        summary.Text = session.Summary;
                    Settled?.Invoke();
                }
            }
        }

        public void UpdateNavigation()
        {
            previous.Enabled = !session.IsLoading && session.CanGoPrevious;
            next.Enabled = !session.IsLoading && session.CanGoNext;
        }

        /// <summary>Marks the column whose <see cref="DataGridViewColumn.Tag"/> is the session's sort field.</summary>
        public void ShowSortGlyphs()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = column.Tag is TSort field && EqualityComparer<TSort>.Default.Equals(field, session.Sort)
                    ? session.Descending ? SortOrder.Descending : SortOrder.Ascending
                    : SortOrder.None;
            }
        }

        /// <summary>Every selected row's record in list order; the current row's alone when nothing is selected.</summary>
        public IReadOnlyList<TRecord> SelectedRecords<TRecord>()
            where TRecord : class
        {
            var picked = grid.SelectedRows.Cast<DataGridViewRow>()
                .OrderBy(row => row.Index)
                .Select(row => row.Tag as TRecord)
                .Where(record => record != null)
                .ToList();
            if (picked.Count == 0 && grid.CurrentRow?.Tag is TRecord current)
                picked.Add(current);
            return picked;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// File &gt; Drawing Library Grid: a page of library drawings drawn side by side. Check All
    /// Pages walks every page, timing each drawing's download, label search and paint, and lists
    /// the drawings that fail, run slow or would freeze a preview. The label search is timed
    /// without the paint's cell budget, on a worker with a time limit, so a shape the budget hides
    /// still shows up here.
    /// </summary>
    public partial class DrawingLibraryGridForm : Form
    {
        internal const int PageSize = 48;
        private const int ParallelDownloads = 6;
        private static readonly TimeSpan PrepareTimeout = TimeSpan.FromSeconds(10);
        private const long SlowPrepareMs = 1000;
        private const long SlowPaintMs = 500;

        private readonly IDrawingLibrary library;
        private CancellationTokenSource pageCancel;
        private CancellationTokenSource checkCancel;
        private int offset;
        private int total;

        /// <summary>The problems found, in the order they were listed; read by tests.</summary>
        internal List<GridProblem> Problems { get; } = new();

        /// <summary>Drawings shown since the form opened; read by tests.</summary>
        internal int DrawingsShown { get; private set; }

        public DrawingLibraryGridForm(IDrawingLibrary library, string serverUrl)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            InitializeComponent();
            Text = $"Drawing Library Grid — {serverUrl}";
            Load += (_, _) => DialogBounds.FitToOwnerScreen(this);
            FormClosed += (_, _) =>
            {
                checkCancel?.Cancel();
                pageCancel?.Cancel();
            };
        }

        // A check started first owns the page; loading the first page now would cancel its own.
        private async void DrawingLibraryGridForm_Shown(object sender, EventArgs e)
        {
            if (checkCancel == null)
                await ShowPageAsync(0);
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            await ShowPageAsync(0);
        }

        private async void PreviousButton_Click(object sender, EventArgs e) => await ShowPageAsync(System.Math.Max(0, offset - PageSize));

        private async void NextButton_Click(object sender, EventArgs e) => await ShowPageAsync(offset + PageSize);

        private async void CheckAllButton_Click(object sender, EventArgs e) => await CheckAllPagesAsync();

        private void StopButton_Click(object sender, EventArgs e) => checkCancel?.Cancel();

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (Problems.Count == 0)
                return;
            var text = new StringBuilder("Name\tRevision\tId\tProblem\tms\r\n");
            foreach (var p in Problems)
                text.Append($"{p.Record.Name}\t{p.Record.Revision}\t{p.Record.Id}\t{p.Problem}\t{p.Milliseconds}\r\n");
            Clipboard.SetText(text.ToString());
        }

        // Searching for a listed drawing shows it on its own.
        private async void ProblemList_DoubleClick(object sender, EventArgs e)
        {
            if (problemList.SelectedItems.Count == 0 || checkCancel != null)
                return;
            searchBox.Text = ((GridProblem)problemList.SelectedItems[0].Tag).Record.Name;
            await ShowPageAsync(0);
        }

        /// <summary>Shows one page; false when it was canceled or could not be read.</summary>
        internal async Task<bool> ShowPageAsync(int pageOffset)
        {
            pageCancel?.Cancel();
            var cancel = pageCancel = CancellationTokenSource.CreateLinkedTokenSource(checkCancel?.Token ?? CancellationToken.None);
            var token = cancel.Token;

            DrawingLibraryPage page;
            try
            {
                page = await library.QueryAsync(new DrawingLibraryQuery
                {
                    Search = searchBox.Text,
                    Sort = DrawingSortField.Name,
                    Descending = false,
                    Offset = pageOffset,
                    Limit = PageSize,
                }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Could not read the library: {ex.Message}";
                return false;
            }
            if (token.IsCancellationRequested || IsDisposed)
                return false;

            offset = page.Offset;
            total = page.Total;
            pageLabel.Text = page.Items.Count == 0 ? "No drawings" : $"{offset + 1}-{offset + page.Items.Count} of {total}";
            previousButton.Enabled = offset > 0 && checkCancel == null;
            nextButton.Enabled = offset + page.Items.Count < total && checkCancel == null;

            var tiles = page.Items.Select(record => new DrawingTile(record)).ToList();
            tilePanel.SuspendLayout();
            foreach (Control old in tilePanel.Controls.Cast<Control>().ToList())
                old.Dispose();
            tilePanel.Controls.AddRange(tiles.ToArray());
            tilePanel.ResumeLayout();
            tilePanel.Update();

            // Downloads and label searches overlap; each tile paints on this thread as its drawing arrives.
            // Not disposed: loads of a superseded page still release it.
            var gate = new SemaphoreSlim(ParallelDownloads);
            var loads = tiles.Select(tile => PrepareAsync(tile.Record, gate, token)).ToList();
            var pending = loads.ToList();
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending);
                pending.Remove(done);
                if (token.IsCancellationRequested || IsDisposed)
                    return false;
                Show(tiles[loads.IndexOf(done)], done.Result);
            }
            return !token.IsCancellationRequested;
        }

        private void Show(DrawingTile tile, Prepared prepared)
        {
            if (prepared.Problem != null)
            {
                tile.ShowMessage(prepared.Problem);
                Report(tile.Record, prepared.Problem, prepared.Milliseconds);
                return;
            }
            if (prepared.Milliseconds > SlowPrepareMs)
                Report(tile.Record, "Slow label search", prepared.Milliseconds);

            var watch = Stopwatch.StartNew();
            try
            {
                tile.ShowDrawing(prepared.Drawing);
                // Paint now, so the time is this drawing's.
                tile.Refresh();
            }
            catch (Exception ex)
            {
                tile.ShowMessage("Paint failed");
                Report(tile.Record, $"Paint failed: {ex.Message}", watch.ElapsedMilliseconds);
                return;
            }
            if (watch.ElapsedMilliseconds > SlowPaintMs)
                Report(tile.Record, "Slow paint", watch.ElapsedMilliseconds);
            DrawingsShown++;
        }

        private async Task<Prepared> PrepareAsync(DrawingLibraryRecord record, SemaphoreSlim gate, CancellationToken token)
        {
            try
            {
                await gate.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return new Prepared(null, "Canceled", 0);
            }

            var watch = Stopwatch.StartNew();
            try
            {
                var bytes = await library.GetRevisionPackageAsync(record.Id, record.Revision, token);
                if (bytes == null)
                    return new Prepared(null, "Revision not on the server", 0);

                watch.Restart();
                // A search that never ends keeps its worker; the check moves on without it.
                var drawing = await Task.Run(() =>
                {
                    var read = DrawingPackage.Read(bytes).Drawing;
                    if (read?.Program != null)
                        LayoutPart.ComputeLabelPoint(read, int.MaxValue);
                    return read;
                }).WaitAsync(PrepareTimeout, token);

                if (drawing?.Program == null)
                    return new Prepared(null, "No program", watch.ElapsedMilliseconds);
                var color = Color.FromArgb(record.Settings?.ColorArgb ?? 0);
                drawing.Color = color.A != 0 ? color : Color.SteelBlue;
                return new Prepared(drawing, null, watch.ElapsedMilliseconds);
            }
            catch (TimeoutException)
            {
                return new Prepared(null, $"Hangs: label search over {PrepareTimeout.TotalSeconds:0} s", watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new Prepared(null, "Canceled", 0);
            }
            catch (Exception ex)
            {
                return new Prepared(null, $"Load failed: {ex.Message}", watch.ElapsedMilliseconds);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Shows every page in turn from the first, listing each problem found.</summary>
        internal async Task CheckAllPagesAsync()
        {
            if (checkCancel != null)
                return;
            checkCancel = new CancellationTokenSource();
            Problems.Clear();
            problemList.Items.Clear();
            DrawingsShown = 0;
            checkAllButton.Enabled = false;
            stopButton.Enabled = true;
            searchBox.Enabled = false;
            var watch = Stopwatch.StartNew();
            try
            {
                var next = 0;
                do
                {
                    if (!await ShowPageAsync(next))
                    {
                        // A page the server would not list ends the check with its error shown.
                        if (!checkCancel.IsCancellationRequested)
                            return;
                        break;
                    }
                    next = offset + PageSize;
                    statusLabel.Text = $"Checked {System.Math.Min(next, total)} of {total} drawings, {Problems.Count} problem(s), {watch.Elapsed:h\\:mm\\:ss}";
                } while (next < total && !checkCancel.IsCancellationRequested);

                statusLabel.Text = (checkCancel.IsCancellationRequested ? "Stopped. " : "Done. ")
                    + $"{DrawingsShown} drawing(s) shown, {Problems.Count} problem(s), {watch.Elapsed:h\\:mm\\:ss}";
            }
            finally
            {
                checkCancel.Dispose();
                checkCancel = null;
                if (!IsDisposed)
                {
                    checkAllButton.Enabled = true;
                    stopButton.Enabled = false;
                    searchBox.Enabled = true;
                    previousButton.Enabled = offset > 0;
                    nextButton.Enabled = offset + PageSize < total;
                }
            }
        }

        private void Report(DrawingLibraryRecord record, string problem, long milliseconds)
        {
            var found = new GridProblem(record, problem, milliseconds);
            Problems.Add(found);
            problemList.Items.Add(new ListViewItem(new[]
            {
                record.Name, record.Revision.ToString(), problem, milliseconds.ToString(),
            })
            { Tag = found });
        }

        internal sealed record GridProblem(DrawingLibraryRecord Record, string Problem, long Milliseconds);

        private sealed record Prepared(Drawing Drawing, string Problem, long Milliseconds);

        /// <summary>One drawing: its preview over its name.</summary>
        private sealed class DrawingTile : Panel
        {
            private readonly ShapePreviewControl preview;

            public DrawingLibraryRecord Record { get; }

            public DrawingTile(DrawingLibraryRecord record)
            {
                Record = record;
                Size = new Size(180, 190);
                Margin = new Padding(4);
                BorderStyle = BorderStyle.FixedSingle;

                preview = new ShapePreviewControl { Dock = DockStyle.Fill, TabStop = false };
                preview.SetInfo("Loading...");
                var name = new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 20,
                    Text = record.Name,
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoEllipsis = true,
                };
                Controls.Add(preview);
                Controls.Add(name);
            }

            public void ShowDrawing(Drawing drawing)
            {
                preview.ShowDrawing(drawing);
                preview.SetInfo();
            }

            public void ShowMessage(string message)
            {
                preview.ShowDrawing(null);
                preview.SetInfo(message);
            }
        }
    }
}

using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.Engine.BestFit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class BestFitViewerForm : Form
    {
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int Columns = 5;
        private const int Rows = 3;
        private const int ItemsPerPage = Columns * Rows;

        private static readonly Color KeptBackground = Color.FromArgb(240, 240, 240);
        private static readonly Color DroppedBackground = Color.FromArgb(255, 235, 235);
        private static readonly Color KeptPartColor = Color.FromArgb(50, 120, 190);
        private static readonly Color DroppedPartColor = Color.FromArgb(180, 80, 80);

        private readonly List<Drawing> drawings;
        private readonly Plate plate;

        private Drawing activeDrawing;
        private List<BestFitResult> results;
        private int totalResults;
        private int keptCount;
        private double computeSeconds;
        private double totalSeconds;
        private int currentPage;
        private int pageCount;
        private CancellationTokenSource computeCts;

        public BestFitResult SelectedResult { get; private set; }
        public Drawing SelectedDrawing => activeDrawing;

        public BestFitViewerForm(DrawingCollection drawings, Plate plate)
        {
            this.drawings = drawings.ToList();
            this.plate = plate;
            this.activeDrawing = this.drawings[0];
            DoubleBuffered = true;
            InitializeComponent();

            foreach (var d in drawings)
                cboDrawing.Items.Add(d.Name);
            cboDrawing.SelectedIndex = 0;
            cboDrawing.SelectedIndexChanged += cboDrawing_SelectedIndexChanged;

            navPanel.SizeChanged += (s, ev) => CenterNavControls();
            Shown += BestFitViewerForm_Shown;
        }

        private void BestFitViewerForm_Shown(object sender, EventArgs e)
        {
            LoadResultsAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            computeCts?.Cancel();
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Left || keyData == Keys.PageUp)
            {
                NavigatePage(-1);
                return true;
            }
            if (keyData == Keys.Right || keyData == Keys.PageDown)
            {
                NavigatePage(1);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void cboDrawing_SelectedIndexChanged(object sender, EventArgs e)
        {
            var index = cboDrawing.SelectedIndex;
            if (index < 0 || index >= drawings.Count)
                return;

            activeDrawing = drawings[index];
            LoadResultsAsync();
        }

        private async void LoadResultsAsync()
        {
            computeCts?.Cancel();
            var cts = new CancellationTokenSource();
            computeCts = cts;

            SetLoading(true);

            try
            {
                var drawing = activeDrawing;
                var length = plate.Size.Length;
                var width = plate.Size.Width;
                var spacing = plate.PartSpacing;

                var result = await Task.Run(() => ComputeResults(drawing, length, width, spacing), cts.Token);

                if (cts.Token.IsCancellationRequested)
                    return;

                results = result.Results;
                totalResults = result.TotalResults;
                keptCount = result.KeptCount;
                computeSeconds = result.ComputeSeconds;
                totalSeconds = result.TotalSeconds;
                pageCount = System.Math.Max(1, (int)System.Math.Ceiling(results.Count / (double)ItemsPerPage));

                ShowPage(0);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (cts == computeCts)
                    SetLoading(false);
            }
        }

        private void SetLoading(bool loading)
        {
            Cursor = loading ? Cursors.WaitCursor : Cursors.Default;
            cboDrawing.Enabled = !loading;
            btnPrev.Enabled = !loading;
            btnNext.Enabled = !loading;
            txtPage.Enabled = !loading;

            if (loading)
            {
                Text = "Best-Fit Viewer — Computing...";
                gridPanel.SuspendLayout();
                gridPanel.Controls.Clear();
                gridPanel.ResumeLayout(true);
            }
        }

        private static ComputeResult ComputeResults(Drawing drawing, double length, double width, double spacing)
        {
            var sw = Stopwatch.StartNew();

            var all = BestFitCache.GetOrCompute(drawing, length, width, spacing);

            var computeMs = sw.ElapsedMilliseconds;
            var total = all.Count;
            var kept = 0;

            foreach (var r in all)
            {
                if (r.Keep) kept++;
            }

            sw.Stop();

            return new ComputeResult
            {
                Results = all,
                TotalResults = total,
                KeptCount = kept,
                ComputeSeconds = computeMs / 1000.0,
                TotalSeconds = sw.Elapsed.TotalSeconds
            };
        }

        private void ShowPage(int page)
        {
            currentPage = page;
            var start = page * ItemsPerPage;
            var count = System.Math.Min(ItemsPerPage, results.Count - start);

            SendMessage(gridPanel.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                gridPanel.SuspendLayout();
                gridPanel.Controls.Clear();

                gridPanel.RowCount = Rows;
                gridPanel.RowStyles.Clear();
                for (var i = 0; i < Rows; i++)
                    gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / Rows));

                for (var i = 0; i < count; i++)
                {
                    var result = results[start + i];
                    var cell = CreateCell(result, activeDrawing, start + i + 1);
                    gridPanel.Controls.Add(cell, i % Columns, i / Columns);
                }

                gridPanel.ResumeLayout(true);
            }
            finally
            {
                SendMessage(gridPanel.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                gridPanel.Invalidate(true);
            }

            btnPrev.Enabled = currentPage > 0;
            btnNext.Enabled = currentPage < pageCount - 1;
            txtPage.Text = (currentPage + 1).ToString();
            lblPageCount.Text = string.Format("/ {0}", pageCount);

            Text = string.Format("Best-Fit Viewer — {0} candidates ({1} kept) | Compute: {2:F1}s | Total: {3:F1}s | Showing {4}-{5} of {6}",
                totalResults, keptCount, computeSeconds, totalSeconds,
                start + 1, start + count, results.Count);
        }

        private void btnPrev_Click(object sender, EventArgs e) => NavigatePage(-1);

        private void btnNext_Click(object sender, EventArgs e) => NavigatePage(1);

        private void CenterNavControls()
        {
            var gap = 6;
            var groupWidth = btnPrev.Width + gap + txtPage.Width + gap + lblPageCount.Width + gap + btnNext.Width;
            var x = (navPanel.Width - groupWidth) / 2;
            var midY = navPanel.Height / 2;

            btnPrev.Location = new Point(x, midY - btnPrev.Height / 2);
            x += btnPrev.Width + gap;

            txtPage.Location = new Point(x, midY - txtPage.Height / 2);
            x += txtPage.Width + gap;

            lblPageCount.Location = new Point(x, midY - lblPageCount.Height / 2);
            x += lblPageCount.Width + gap;

            btnNext.Location = new Point(x, midY - btnNext.Height / 2);
        }

        private void NavigatePage(int delta)
        {
            var newPage = currentPage + delta;
            if (newPage >= 0 && newPage < pageCount)
                ShowPage(newPage);
        }

        private void txtPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (int.TryParse(txtPage.Text, out var page) && page >= 1 && page <= pageCount)
                    ShowPage(page - 1);
                else
                    txtPage.Text = (currentPage + 1).ToString();
            }
        }

        private BestFitCell CreateCell(BestFitResult result, Drawing drawing, int rank)
        {
            var kept = result.Keep;
            var bgColor = kept ? KeptBackground : DroppedBackground;
            var partColor = kept ? KeptPartColor : DroppedPartColor;

            var colorScheme = new ColorScheme
            {
                BackgroundColor = bgColor,
                LayoutOutlineColor = Color.Gray,
                LayoutFillColor = bgColor,
                BoundingBoxColor = bgColor,
                RapidColor = Color.DodgerBlue,
                OriginColor = bgColor,
                EdgeSpacingColor = bgColor
            };

            var cell = new BestFitCell(colorScheme);
            cell.PartColor = partColor;
            cell.Dock = DockStyle.Fill;
            cell.Plate.Size = new Geometry.Size(
                result.BoundingHeight,
                result.BoundingWidth);

            var parts = result.BuildParts(drawing);

            foreach (var part in parts)
                cell.Plate.Parts.Add(part);

            cell.SetMetadata(result, rank);

            cell.DoubleClick += (sender, e) =>
            {
                SelectedResult = result;
                DialogResult = DialogResult.OK;
                Close();
            };

            return cell;
        }

        private struct ComputeResult
        {
            public List<BestFitResult> Results;
            public int TotalResults;
            public int KeptCount;
            public double ComputeSeconds;
            public double TotalSeconds;
        }
    }
}

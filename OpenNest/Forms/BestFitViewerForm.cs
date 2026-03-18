using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine.BestFit;

namespace OpenNest.Forms
{
    public partial class BestFitViewerForm : Form
    {
        private const int Columns = 5;
        private const int Rows = 2;
        private const int ItemsPerPage = Columns * Rows;
        private const int MaxResults = 50;

        private static readonly Color KeptColor = Color.FromArgb(0, 0, 100);
        private static readonly Color DroppedColor = Color.FromArgb(100, 0, 0);

        private readonly Drawing drawing;
        private readonly Plate plate;

        private List<BestFitResult> results;
        private int totalResults;
        private int keptCount;
        private double computeSeconds;
        private double totalSeconds;
        private int currentPage;
        private int pageCount;

        public BestFitResult SelectedResult { get; private set; }

        public BestFitViewerForm(Drawing drawing, Plate plate)
        {
            this.drawing = drawing;
            this.plate = plate;
            InitializeComponent();
            Shown += BestFitViewerForm_Shown;
        }

        private void BestFitViewerForm_Shown(object sender, System.EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                ComputeResults();
                ShowPage(0);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
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

        private void ComputeResults()
        {
            var sw = Stopwatch.StartNew();

            var all = BestFitCache.GetOrCompute(
                drawing, plate.Size.Length, plate.Size.Width, plate.PartSpacing);

            computeSeconds = sw.ElapsedMilliseconds / 1000.0;
            totalResults = all.Count;
            keptCount = 0;

            foreach (var r in all)
            {
                if (r.Keep) keptCount++;
            }

            var count = System.Math.Min(totalResults, MaxResults);
            results = all.GetRange(0, count);
            pageCount = System.Math.Max(1, (int)System.Math.Ceiling(count / (double)ItemsPerPage));

            sw.Stop();
            totalSeconds = sw.Elapsed.TotalSeconds;
        }

        private void ShowPage(int page)
        {
            currentPage = page;
            var start = page * ItemsPerPage;
            var count = System.Math.Min(ItemsPerPage, results.Count - start);

            gridPanel.SuspendLayout();
            gridPanel.Controls.Clear();

            gridPanel.RowCount = Rows;
            gridPanel.RowStyles.Clear();
            for (var i = 0; i < Rows; i++)
                gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / Rows));

            for (var i = 0; i < count; i++)
            {
                var result = results[start + i];
                var cell = CreateCell(result, drawing, start + i + 1);
                gridPanel.Controls.Add(cell, i % Columns, i / Columns);
            }

            gridPanel.ResumeLayout(true);

            btnPrev.Enabled = currentPage > 0;
            btnNext.Enabled = currentPage < pageCount - 1;
            lblPage.Text = string.Format("Page {0} / {1}", currentPage + 1, pageCount);

            Text = string.Format("Best-Fit Viewer — {0} candidates ({1} kept) | Compute: {2:F1}s | Total: {3:F1}s | Showing {4}-{5} of {6}",
                totalResults, keptCount, computeSeconds, totalSeconds,
                start + 1, start + count, results.Count);
        }

        private void btnPrev_Click(object sender, System.EventArgs e) => NavigatePage(-1);

        private void btnNext_Click(object sender, System.EventArgs e) => NavigatePage(1);

        private void NavigatePage(int delta)
        {
            var newPage = currentPage + delta;
            if (newPage >= 0 && newPage < pageCount)
                ShowPage(newPage);
        }

        private BestFitCell CreateCell(BestFitResult result, Drawing drawing, int rank)
        {
            var bgColor = result.Keep ? KeptColor : DroppedColor;

            var colorScheme = new ColorScheme
            {
                BackgroundColor = bgColor,
                LayoutOutlineColor = bgColor,
                LayoutFillColor = bgColor,
                BoundingBoxColor = bgColor,
                RapidColor = Color.DodgerBlue,
                OriginColor = bgColor,
                EdgeSpacingColor = bgColor
            };

            var cell = new BestFitCell(colorScheme);
            cell.Dock = DockStyle.Fill;
            cell.Plate.Size = new Geometry.Size(
                result.BoundingWidth,
                result.BoundingHeight);

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
    }
}

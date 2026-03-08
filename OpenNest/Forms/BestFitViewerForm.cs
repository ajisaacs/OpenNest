using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Forms
{
    public partial class BestFitViewerForm : Form
    {
        private const int Columns = 5;
        private const int RowHeight = 300;
        private const int MaxResults = 50;
        private const double ViewerStepSize = 1.0;

        private static readonly Color KeptColor = Color.FromArgb(0, 0, 100);
        private static readonly Color DroppedColor = Color.FromArgb(100, 0, 0);

        private readonly Drawing drawing;
        private readonly Plate plate;

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
                PopulateGrid(drawing, plate);
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
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void PopulateGrid(Drawing drawing, Plate plate)
        {
            var sw = Stopwatch.StartNew();

            var finder = new BestFitFinder(plate.Size.Width, plate.Size.Height);
            var results = finder.FindBestFits(drawing, plate.PartSpacing, ViewerStepSize);

            var findMs = sw.ElapsedMilliseconds;
            var total = results.Count;
            var kept = 0;

            foreach (var r in results)
            {
                if (r.Keep) kept++;
            }

            var count = System.Math.Min(total, MaxResults);
            var rows = (int)System.Math.Ceiling(count / (double)Columns);
            gridPanel.RowCount = rows;
            gridPanel.RowStyles.Clear();

            for (var i = 0; i < rows; i++)
                gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

            gridPanel.SuspendLayout();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var result = results[i];
                    var view = CreateCellView(result, drawing, i + 1);
                    gridPanel.Controls.Add(view, i % Columns, i / Columns);
                }
            }
            finally
            {
                gridPanel.ResumeLayout(true);
            }

            sw.Stop();
            Text = string.Format("Best-Fit Viewer — {0} candidates ({1} kept) | Compute: {2:F1}s | Total: {3:F1}s | Showing {4}",
                total, kept, findMs / 1000.0, sw.Elapsed.TotalSeconds, count);
        }

        private PlateView CreateCellView(BestFitResult result, Drawing drawing, int rank)
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

            var view = new PlateView(colorScheme);
            view.DrawOrigin = false;
            view.DrawBounds = false;
            view.AllowPan = false;
            view.AllowSelect = false;
            view.AllowZoom = false;
            view.AllowDrop = false;
            view.Dock = DockStyle.Fill;
            view.Plate.Size = new Geometry.Size(
                result.BoundingWidth,
                result.BoundingHeight);

            var parts = result.BuildParts(drawing);

            foreach (var part in parts)
                view.Plate.Parts.Add(part);

            view.Paint += (sender, e) =>
            {
                PaintMetadata(e.Graphics, view, result, rank);
            };

            view.Resize += (sender, e) =>
            {
                view.ZoomToFit(false);
            };

            view.DoubleClick += (sender, e) =>
            {
                SelectedResult = result;
                DialogResult = DialogResult.OK;
                Close();
            };

            view.Cursor = Cursors.Hand;

            return view;
        }

        private void PaintMetadata(Graphics g, PlateView view, BestFitResult result, int rank)
        {
            var font = view.Font;
            var brush = Brushes.White;
            var lineHeight = font.GetHeight(g) + 1;

            var lines = new[]
            {
                string.Format("#{0}  {1:F1}x{2:F1}  Area={3:F1}",
                    rank, result.BoundingWidth, result.BoundingHeight, result.RotatedArea),
                string.Format("Util={0:P1}  Rot={1:F1}\u00b0",
                    result.Utilization,
                    Angle.ToDegrees(result.OptimalRotation)),
                result.Keep ? "" : result.Reason
            };

            var y = 2f;

            foreach (var line in lines)
            {
                if (line.Length == 0)
                    continue;
                g.DrawString(line, font, brush, 2, y);
                y += lineHeight;
            }
        }
    }
}

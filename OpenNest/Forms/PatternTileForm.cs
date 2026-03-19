using OpenNest.Controls;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GeoSize = OpenNest.Geometry.Size;

namespace OpenNest.Forms
{
    public enum PatternTileTarget
    {
        CurrentPlate,
        NewPlate
    }

    public class PatternTileResult
    {
        public List<Part> Parts { get; set; }
        public PatternTileTarget Target { get; set; }
        public GeoSize PlateSize { get; set; }
    }

    public partial class PatternTileForm : Form
    {
        private readonly Nest nest;
        private readonly PlateView cellView;
        private readonly PlateView hPreview;
        private readonly PlateView vPreview;
        private readonly Label hLabel;
        private readonly Label vLabel;

        public PatternTileResult Result { get; private set; }

        public PatternTileForm(Nest nest)
        {
            this.nest = nest;
            InitializeComponent();

            // Unit cell editor — plate outline hidden via zero-size plate
            cellView = new PlateView();
            cellView.Plate.Size = new GeoSize(0, 0);
            cellView.Plate.Quantity = 0; // prevent Drawing.Quantity.Nested side-effects
            cellView.DrawOrigin = false;
            cellView.DrawBounds = false; // hide selection bounding box overlay
            cellView.Dock = DockStyle.Fill;
            splitContainer.Panel1.Controls.Add(cellView);

            // Right side: vertical split with horizontal and vertical preview
            var previewSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 250
            };
            splitContainer.Panel2.Controls.Add(previewSplit);

            hLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "Horizontal — 0 parts",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(80, 80, 80),
                Padding = new Padding(4, 0, 0, 0)
            };

            hPreview = CreatePreviewView();
            previewSplit.Panel1.Controls.Add(hPreview);
            previewSplit.Panel1.Controls.Add(hLabel);

            vLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "Vertical — 0 parts",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(80, 80, 80),
                Padding = new Padding(4, 0, 0, 0)
            };

            vPreview = CreatePreviewView();
            previewSplit.Panel2.Controls.Add(vPreview);
            previewSplit.Panel2.Controls.Add(vLabel);

            // Populate drawing dropdowns
            var drawings = nest.Drawings.OrderBy(d => d.Name).ToList();
            cboDrawingA.Items.Add("(none)");
            cboDrawingB.Items.Add("(none)");
            foreach (var d in drawings)
            {
                cboDrawingA.Items.Add(d);
                cboDrawingB.Items.Add(d);
            }
            cboDrawingA.SelectedIndex = 0;
            cboDrawingB.SelectedIndex = 0;

            // Default plate size from nest defaults
            var defaults = nest.PlateDefaults;
            txtPlateSize.Text = defaults.Size.ToString();
            nudPartSpacing.Value = (decimal)defaults.PartSpacing;

            // Wire events
            cboDrawingA.SelectedIndexChanged += OnDrawingChanged;
            cboDrawingB.SelectedIndexChanged += OnDrawingChanged;
            txtPlateSize.TextChanged += OnPlateSettingsChanged;
            nudPartSpacing.ValueChanged += OnPlateSettingsChanged;
            btnAutoArrange.Click += OnAutoArrangeClick;
            btnApply.Click += OnApplyClick;
            cellView.MouseUp += OnCellMouseUp;
        }

        private Drawing SelectedDrawingA =>
            cboDrawingA.SelectedItem as Drawing;

        private Drawing SelectedDrawingB =>
            cboDrawingB.SelectedItem as Drawing;

        private double PartSpacing =>
            (double)nudPartSpacing.Value;

        private bool TryGetPlateSize(out GeoSize size)
        {
            return GeoSize.TryParse(txtPlateSize.Text, out size);
        }

        private void OnDrawingChanged(object sender, EventArgs e)
        {
            RebuildCell();
            RebuildPreview();
            btnAutoArrange.Enabled = SelectedDrawingA != null && SelectedDrawingB != null;
        }

        private void OnPlateSettingsChanged(object sender, EventArgs e)
        {
            UpdatePreviewPlateSize();
            RebuildPreview();
        }

        private void OnCellMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && cellView.Plate.Parts.Count >= 2)
            {
                CompactCellParts();
            }

            RebuildPreview();
        }

        private void RebuildCell()
        {
            cellView.Plate.Parts.Clear();

            var drawingA = SelectedDrawingA;
            var drawingB = SelectedDrawingB;

            if (drawingA == null && drawingB == null)
                return;

            if (drawingA != null)
            {
                var partA = Part.CreateAtOrigin(drawingA);
                cellView.Plate.Parts.Add(partA);
            }

            if (drawingB != null)
            {
                var partB = Part.CreateAtOrigin(drawingB);

                // Place B to the right of A (or at origin if A is null)
                if (drawingA != null && cellView.Plate.Parts.Count > 0)
                {
                    var aBox = cellView.Plate.Parts[0].BoundingBox;
                    partB.Offset(aBox.Right + PartSpacing, 0);
                }

                cellView.Plate.Parts.Add(partB);
            }

            cellView.ZoomToFit();
        }

        private void CompactCellParts()
        {
            var parts = cellView.Plate.Parts.ToList();
            if (parts.Count < 2)
                return;

            CompactTowardCentroid(parts, PartSpacing);
            cellView.Refresh();
        }

        private static void CompactTowardCentroid(List<Part> parts, double spacing)
        {
            // Use a fixed centroid as the attractor — close enough for 2-part cells
            // and avoids oscillation from recomputing each iteration.
            var centroid = parts.GetBoundingBox().Center;
            var syntheticWorkArea = new Box(-10000, -10000, 20000, 20000);

            for (var iteration = 0; iteration < 10; iteration++)
            {
                var totalMoved = 0.0;

                foreach (var part in parts)
                {
                    var partCenter = part.BoundingBox.Center;
                    var dx = centroid.X - partCenter.X;
                    var dy = centroid.Y - partCenter.Y;

                    if (System.Math.Sqrt(dx * dx + dy * dy) < 0.01)
                        continue;

                    var angle = System.Math.Atan2(dy, dx);
                    var single = new List<Part> { part };
                    var obstacles = parts.Where(p => p != part).ToList();

                    totalMoved += Compactor.Push(single, obstacles,
                        syntheticWorkArea, spacing, angle);
                }

                if (totalMoved < 0.01)
                    break;
            }
        }

        private static PlateView CreatePreviewView()
        {
            var view = new PlateView();
            view.Plate.Quantity = 0;
            view.AllowSelect = false;
            view.AllowDrop = false;
            view.DrawBounds = false;
            view.Dock = DockStyle.Fill;
            return view;
        }

        private void UpdatePreviewPlateSize()
        {
            if (!TryGetPlateSize(out var size))
                return;

            hPreview.Plate.Size = size;
            vPreview.Plate.Size = size;
        }

        private Pattern BuildCellPattern()
        {
            var cellParts = cellView.Plate.Parts.ToList();
            if (cellParts.Count == 0)
                return null;

            var pattern = new Pattern();
            foreach (var part in cellParts)
                pattern.Parts.Add(part);
            pattern.UpdateBounds();
            return pattern;
        }

        private void RebuildPreview()
        {
            hPreview.Plate.Parts.Clear();
            vPreview.Plate.Parts.Clear();

            if (!TryGetPlateSize(out var plateSize))
                return;

            hPreview.Plate.Size = plateSize;
            hPreview.Plate.PartSpacing = PartSpacing;
            vPreview.Plate.Size = plateSize;
            vPreview.Plate.PartSpacing = PartSpacing;

            var pattern = BuildCellPattern();
            if (pattern == null)
                return;

            var workArea = new Box(0, 0, plateSize.Width, plateSize.Length);
            var filler = new FillLinear(workArea, PartSpacing);

            var hParts = filler.Fill(pattern, NestDirection.Horizontal);
            foreach (var part in hParts)
                hPreview.Plate.Parts.Add(part);
            hLabel.Text = $"Horizontal — {hParts.Count} parts";
            hPreview.ZoomToFit();

            var vFiller = new FillLinear(workArea, PartSpacing);
            var vParts = vFiller.Fill(pattern, NestDirection.Vertical);
            foreach (var part in vParts)
                vPreview.Plate.Parts.Add(part);
            vLabel.Text = $"Vertical — {vParts.Count} parts";
            vPreview.ZoomToFit();
        }

        private void OnAutoArrangeClick(object sender, EventArgs e)
        {
            var drawingA = SelectedDrawingA;
            var drawingB = SelectedDrawingB;

            if (drawingA == null || drawingB == null)
                return;

            if (!TryGetPlateSize(out var plateSize))
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                var angles = new[] { 0.0, Math.Angle.ToRadians(90), Math.Angle.ToRadians(180), Math.Angle.ToRadians(270) };
                var bestCell = (List<Part>)null;
                var bestArea = double.MaxValue;

                foreach (var angleA in angles)
                {
                    foreach (var angleB in angles)
                    {
                        var partA = Part.CreateAtOrigin(drawingA, angleA);
                        var partB = Part.CreateAtOrigin(drawingB, angleB);
                        partB.Offset(partA.BoundingBox.Right + PartSpacing, 0);

                        var cell = new List<Part> { partA, partB };
                        CompactTowardCentroid(cell, PartSpacing);

                        var finalBox = cell.GetBoundingBox();
                        var area = finalBox.Width * finalBox.Length;

                        if (area < bestArea)
                        {
                            bestArea = area;
                            bestCell = cell;
                        }
                    }
                }

                if (bestCell != null)
                {
                    cellView.Plate.Parts.Clear();
                    foreach (var part in bestCell)
                        cellView.Plate.Parts.Add(part);
                    cellView.ZoomToFit();
                    RebuildPreview();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            var hCount = hPreview.Plate.Parts.Count;
            var vCount = vPreview.Plate.Parts.Count;

            if (hCount == 0 && vCount == 0)
                return;

            if (!TryGetPlateSize(out var plateSize))
                return;

            // Pick which direction to apply — use the one with more parts,
            // or ask if they're equal and both > 0
            NestDirection applyDirection;

            if (hCount > vCount)
                applyDirection = NestDirection.Horizontal;
            else if (vCount > hCount)
                applyDirection = NestDirection.Vertical;
            else
                applyDirection = NestDirection.Horizontal; // tie-break

            var choice = MessageBox.Show(
                $"Apply {applyDirection} pattern ({(applyDirection == NestDirection.Horizontal ? hCount : vCount)} parts) to current plate?" +
                "\n\nYes = Current plate (clears existing parts)\nNo = New plate",
                "Apply Pattern",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
                return;

            // Rebuild a fresh set of tiled parts for the caller
            var pattern = BuildCellPattern();
            if (pattern == null)
                return;

            var filler = new FillLinear(new Box(0, 0, plateSize.Width, plateSize.Length), PartSpacing);
            var tiledParts = filler.Fill(pattern, applyDirection);

            Result = new PatternTileResult
            {
                Parts = tiledParts,
                Target = choice == DialogResult.Yes
                    ? PatternTileTarget.CurrentPlate
                    : PatternTileTarget.NewPlate,
                PlateSize = plateSize
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

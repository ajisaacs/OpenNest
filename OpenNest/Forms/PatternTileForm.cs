using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GeoSize = OpenNest.Geometry.Size;

namespace OpenNest.Forms
{
    public partial class PatternTileForm : Form
    {
        private readonly Nest nest;

        public PatternTileResult Result { get; private set; }

        public PatternTileForm(Nest nest)
        {
            this.nest = nest;
            InitializeComponent();

            // Hide plate outline via zero-size plate
            cellView.Plate.Size = new GeoSize(0, 0);
            cellView.Plate.Quantity = 0;
            hPreview.Plate.Quantity = 0;
            vPreview.Plate.Quantity = 0;

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

            // Format drawing names in dropdowns
            cboDrawingA.FormattingEnabled = true;
            cboDrawingA.Format += ComboDrawing_Format;
            cboDrawingB.FormattingEnabled = true;
            cboDrawingB.Format += ComboDrawing_Format;

            // Wire events
            cboDrawingA.SelectedIndexChanged += OnDrawingChanged;
            cboDrawingB.SelectedIndexChanged += OnDrawingChanged;
            txtPlateSize.TextChanged += OnPlateSettingsChanged;
            nudPartSpacing.ValueChanged += OnPlateSettingsChanged;
            btnAutoArrange.Click += OnAutoArrangeClick;
            btnApply.Click += OnApplyClick;
            cellView.MouseUp += OnCellMouseUp;
        }

        private void ComboDrawing_Format(object sender, ListControlConvertEventArgs e)
        {
            if (e.Value is Drawing d)
                e.Value = d.Name;
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
                cellView.ZoomToFit();
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

                    var direction = new Vector(dx, dy);
                    var len = System.Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0) direction = new Vector(dx / len, dy / len);
                    var single = new List<Part> { part };
                    var obstacles = parts.Where(p => p != part).ToList();

                    totalMoved += Compactor.Push(single, obstacles,
                        syntheticWorkArea, spacing, direction);
                }

                if (totalMoved < 0.01)
                    break;
            }
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

            var workArea = new Box(0, 0, plateSize.Length, plateSize.Width);
            var filler = new FillLinear(workArea, PartSpacing) { Label = "PatternTile-H" };

            var hParts = filler.Fill(pattern, NestDirection.Horizontal);
            foreach (var part in hParts)
                hPreview.Plate.Parts.Add(part);
            hLabel.Text = $"Horizontal — {hParts.Count} parts";
            hPreview.ZoomToFit();

            var vFiller = new FillLinear(workArea, PartSpacing) { Label = "PatternTile-V" };
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

            var filler = new FillLinear(new Box(0, 0, plateSize.Length, plateSize.Width), PartSpacing) { Label = "PatternTile-Apply" };
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
}

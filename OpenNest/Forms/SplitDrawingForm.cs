using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Forms;

public partial class SplitDrawingForm : Form
{
    private readonly Drawing _drawing;
    private readonly List<Entity> _drawingEntities;
    private readonly Box _drawingBounds;
    private readonly List<SplitLine> _splitLines = new();
    private CutOffAxis _currentAxis = CutOffAxis.Vertical;
    private bool _placingLine;

    // Zoom/pan state
    private float _zoom = 1f;
    private PointF _pan;
    private Point _lastMouse;
    private bool _panning;

    // Snap threshold in screen pixels
    private const double SnapThreshold = 5.0;

    public List<Drawing> ResultDrawings { get; private set; }

    public SplitDrawingForm(Drawing drawing)
    {
        InitializeComponent();

        // Enable double buffering on the preview panel to reduce flicker
        typeof(Panel).InvokeMember(
            "DoubleBuffered",
            System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, pnlPreview, new object[] { true });

        _drawing = drawing;
        _drawingEntities = ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => e.Layer != SpecialLayers.Rapid).ToList();
        _drawingBounds = drawing.Program.BoundingBox();

        Text = $"Split Drawing: {drawing.Name}";
        UpdateUI();
        FitToView();
    }

    // --- Split Method Selection ---

    private void OnMethodChanged(object sender, EventArgs e)
    {
        grpAutoFit.Visible = radFitToPlate.Checked;
        grpByCount.Visible = radByCount.Checked;

        if (radFitToPlate.Checked || radByCount.Checked)
            RecalculateAutoSplitLines();
    }

    private void RecalculateAutoSplitLines()
    {
        _splitLines.Clear();

        if (radFitToPlate.Checked)
        {
            var plateW = (double)nudPlateWidth.Value;
            var plateH = (double)nudPlateHeight.Value;
            var spacing = (double)nudEdgeSpacing.Value;
            var overhang = GetCurrentParameters().FeatureOverhang;
            var axisIndex = cboSplitAxis.SelectedIndex;

            if (axisIndex == 1) // Vertical Only — split the part width using the plate size
            {
                var usable = System.Math.Min(plateW, plateH) - 2 * spacing - overhang;
                if (usable > 0)
                {
                    var splits = (int)System.Math.Ceiling(_drawingBounds.Width / usable) - 1;
                    if (splits > 0)
                    {
                        var step = _drawingBounds.Width / (splits + 1);
                        for (var i = 1; i <= splits; i++)
                            _splitLines.Add(new SplitLine(_drawingBounds.X + step * i, CutOffAxis.Vertical));
                    }
                }
            }
            else if (axisIndex == 2) // Horizontal Only — split the part height using the plate size
            {
                var usable = System.Math.Min(plateW, plateH) - 2 * spacing - overhang;
                if (usable > 0)
                {
                    var splits = (int)System.Math.Ceiling(_drawingBounds.Length / usable) - 1;
                    if (splits > 0)
                    {
                        var step = _drawingBounds.Length / (splits + 1);
                        for (var i = 1; i <= splits; i++)
                            _splitLines.Add(new SplitLine(_drawingBounds.Y + step * i, CutOffAxis.Horizontal));
                    }
                }
            }
            else // Auto — both axes
            {
                _splitLines.AddRange(AutoSplitCalculator.FitToPlate(_drawingBounds, plateW, plateH, spacing, overhang));
            }
        }
        else if (radByCount.Checked)
        {
            var hPieces = (int)nudHorizontalPieces.Value;
            var vPieces = (int)nudVerticalPieces.Value;
            _splitLines.AddRange(AutoSplitCalculator.SplitByCount(_drawingBounds, hPieces, vPieces));
        }

        UpdateUI();
        pnlPreview.Invalidate();
    }

    private void OnAutoFitValueChanged(object sender, EventArgs e)
    {
        if (radFitToPlate.Checked)
            RecalculateAutoSplitLines();
    }

    private void OnByCountValueChanged(object sender, EventArgs e)
    {
        if (radByCount.Checked)
            RecalculateAutoSplitLines();
    }

    // --- Split Type Selection ---

    private void OnTypeChanged(object sender, EventArgs e)
    {
        grpTabParams.Visible = radTabs.Checked;
        grpSpikeParams.Visible = radSpike.Checked;
        if (radFitToPlate.Checked)
            RecalculateAutoSplitLines(); // overhang changed
        pnlPreview.Invalidate();
    }

    private SplitParameters GetCurrentParameters()
    {
        var p = new SplitParameters();
        if (radTabs.Checked)
        {
            p.Type = SplitType.WeldGapTabs;
            p.TabWidth = (double)nudTabWidth.Value;
            p.TabHeight = (double)nudTabHeight.Value;
            p.TabCount = (int)nudTabCount.Value;
        }
        else if (radSpike.Checked)
        {
            p.Type = SplitType.SpikeGroove;
            p.SpikeDepth = (double)nudSpikeDepth.Value;
            p.GrooveDepth = (double)nudGrooveDepth.Value;
            p.SpikeWeldGap = (double)nudSpikeWeldGap.Value;
            p.SpikeAngle = (double)nudSpikeAngle.Value;
            p.SpikePairCount = (int)nudSpikePairCount.Value;
        }
        return p;
    }

    // --- Manual Split Line Placement ---

    private void OnPreviewMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && radManual.Checked && _placingLine)
        {
            var pt = ScreenToDrawing(e.Location);
            var snapped = SnapToMidpoint(pt);
            var position = _currentAxis == CutOffAxis.Vertical ? snapped.X : snapped.Y;
            _splitLines.Add(new SplitLine(position, _currentAxis));
            UpdateUI();
            pnlPreview.Invalidate();
        }
        else if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _lastMouse = e.Location;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            _pan.X += e.X - _lastMouse.X;
            _pan.Y += e.Y - _lastMouse.Y;
            _lastMouse = e.Location;
            pnlPreview.Invalidate();
        }

        var drawPt = ScreenToDrawing(e.Location);
        lblCursor.Text = $"Cursor: {drawPt.X:F2}, {drawPt.Y:F2}";
    }

    private void OnPreviewMouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle)
            _panning = false;
    }

    private void OnPreviewMouseWheel(object sender, MouseEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1f : 0.9f;
        _zoom *= factor;
        pnlPreview.Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Space)
        {
            _currentAxis = _currentAxis == CutOffAxis.Vertical ? CutOffAxis.Horizontal : CutOffAxis.Vertical;
            return true;
        }
        if (keyData == Keys.Escape)
        {
            _placingLine = false;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Vector SnapToMidpoint(Vector pt)
    {
        var midX = _drawingBounds.Center.X;
        var midY = _drawingBounds.Center.Y;
        var threshold = SnapThreshold / _zoom;

        if (_currentAxis == CutOffAxis.Vertical && System.Math.Abs(pt.X - midX) < threshold)
            return new Vector(midX, pt.Y);
        if (_currentAxis == CutOffAxis.Horizontal && System.Math.Abs(pt.Y - midY) < threshold)
            return new Vector(pt.X, midY);
        return pt;
    }

    // --- Rendering ---

    private void OnPreviewPaint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(26, 26, 26));

        g.TranslateTransform(_pan.X, _pan.Y);
        g.ScaleTransform(_zoom, -_zoom); // flip Y for CNC coordinates

        // Draw part outline
        using var partPen = new Pen(Color.LightGray, 1f / _zoom);
        DrawEntities(g, _drawingEntities, partPen);

        // Draw split lines
        using var splitPen = new Pen(Color.FromArgb(255, 82, 82), 1f / _zoom);
        splitPen.DashStyle = DashStyle.Dash;
        foreach (var sl in _splitLines)
        {
            if (sl.Axis == CutOffAxis.Vertical)
                g.DrawLine(splitPen, (float)sl.Position, (float)(_drawingBounds.Bottom - 10), (float)sl.Position, (float)(_drawingBounds.Top + 10));
            else
                g.DrawLine(splitPen, (float)(_drawingBounds.Left - 10), (float)sl.Position, (float)(_drawingBounds.Right + 10), (float)sl.Position);
        }

        // Draw piece color overlays
        DrawPieceOverlays(g);
    }

    private void DrawEntities(Graphics g, List<Entity> entities, Pen pen)
    {
        foreach (var entity in entities)
        {
            if (entity is Line line)
                g.DrawLine(pen, (float)line.StartPoint.X, (float)line.StartPoint.Y, (float)line.EndPoint.X, (float)line.EndPoint.Y);
            else if (entity is Arc arc)
            {
                var rect = new RectangleF(
                    (float)(arc.Center.X - arc.Radius),
                    (float)(arc.Center.Y - arc.Radius),
                    (float)(arc.Radius * 2),
                    (float)(arc.Radius * 2));
                var startDeg = (float)OpenNest.Math.Angle.ToDegrees(arc.StartAngle);
                var sweepDeg = (float)OpenNest.Math.Angle.ToDegrees(arc.EndAngle - arc.StartAngle);
                if (rect.Width > 0 && rect.Height > 0)
                    g.DrawArc(pen, rect, startDeg, sweepDeg);
            }
        }
    }

    private static readonly Color[] PieceColors =
    {
        Color.FromArgb(40, 79, 195, 247),
        Color.FromArgb(40, 129, 199, 132),
        Color.FromArgb(40, 255, 183, 77),
        Color.FromArgb(40, 206, 147, 216),
        Color.FromArgb(40, 255, 138, 128),
        Color.FromArgb(40, 128, 222, 234)
    };

    private void DrawPieceOverlays(Graphics g)
    {
        // Simple region overlay based on split lines and bounding box
        var regions = BuildPreviewRegions();
        for (var i = 0; i < regions.Count; i++)
        {
            var color = PieceColors[i % PieceColors.Length];
            using var brush = new SolidBrush(color);
            var r = regions[i];
            g.FillRectangle(brush, (float)r.Left, (float)r.Bottom, (float)r.Width, (float)r.Length);
        }
    }

    private List<Box> BuildPreviewRegions()
    {
        var verticals = _splitLines.Where(l => l.Axis == CutOffAxis.Vertical).OrderBy(l => l.Position).ToList();
        var horizontals = _splitLines.Where(l => l.Axis == CutOffAxis.Horizontal).OrderBy(l => l.Position).ToList();

        var xEdges = new List<double> { _drawingBounds.Left };
        xEdges.AddRange(verticals.Select(v => v.Position));
        xEdges.Add(_drawingBounds.Right);

        var yEdges = new List<double> { _drawingBounds.Bottom };
        yEdges.AddRange(horizontals.Select(h => h.Position));
        yEdges.Add(_drawingBounds.Top);

        var regions = new List<Box>();
        for (var yi = 0; yi < yEdges.Count - 1; yi++)
            for (var xi = 0; xi < xEdges.Count - 1; xi++)
                regions.Add(new Box(xEdges[xi], yEdges[yi], xEdges[xi + 1] - xEdges[xi], yEdges[yi + 1] - yEdges[yi]));

        return regions;
    }

    // --- Coordinate transforms ---

    private Vector ScreenToDrawing(Point screen)
    {
        var x = (screen.X - _pan.X) / _zoom;
        var y = -(screen.Y - _pan.Y) / _zoom; // flip Y
        return new Vector(x, y);
    }

    private void FitToView()
    {
        if (_drawingBounds.Width <= 0 || _drawingBounds.Length <= 0) return;
        var pad = 40f;
        var scaleX = (pnlPreview.Width - pad * 2) / (float)_drawingBounds.Width;
        var scaleY = (pnlPreview.Height - pad * 2) / (float)_drawingBounds.Length;
        _zoom = System.Math.Min(scaleX, scaleY);
        _pan = new PointF(
            pad - (float)_drawingBounds.Left * _zoom,
            pnlPreview.Height - pad + (float)_drawingBounds.Bottom * _zoom);
    }

    // --- OK/Cancel ---

    private void OnOK(object sender, EventArgs e)
    {
        if (_splitLines.Count == 0)
        {
            MessageBox.Show("No split lines defined.", "Split Drawing", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultDrawings = DrawingSplitter.Split(_drawing, _splitLines, GetCurrentParameters());
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancel(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // --- Toolbar ---

    private void OnAddSplitLine(object sender, EventArgs e)
    {
        radManual.Checked = true;
        _placingLine = true;
    }

    private void OnDeleteSplitLine(object sender, EventArgs e)
    {
        if (_splitLines.Count > 0)
        {
            _splitLines.RemoveAt(_splitLines.Count - 1);
            UpdateUI();
            pnlPreview.Invalidate();
        }
    }

    private void UpdateUI()
    {
        var pieceCount = _splitLines.Count == 0 ? 1 : BuildPreviewRegions().Count;
        lblStatus.Text = $"Part: {_drawingBounds.Width:F2} x {_drawingBounds.Length:F2} | {_splitLines.Count} split lines | {pieceCount} pieces";
    }
}

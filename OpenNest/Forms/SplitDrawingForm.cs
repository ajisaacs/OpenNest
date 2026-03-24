using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
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

    // Feature handle drag state
    private int _dragLineIndex = -1;
    private int _dragFeatureIndex = -1;
    private int _hoverLineIndex = -1;
    private int _hoverFeatureIndex = -1;
    private const float HandleRadius = 5f;
    private const double SnapThreshold = 5.0;

    public List<Drawing> ResultDrawings { get; private set; }

    public SplitDrawingForm(Drawing drawing)
    {
        InitializeComponent();

        _drawing = drawing;
        _drawingEntities = ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => e.Layer != SpecialLayers.Rapid).ToList();
        _drawingBounds = drawing.Program.BoundingBox();

        foreach (var entity in _drawingEntities)
            entity.Layer.IsVisible = true;

        pnlPreview.Entities = _drawingEntities;
        pnlPreview.DrawOverlays = PaintOverlays;

        Text = $"Split Drawing: {drawing.Name}";
        UpdateUI();
        pnlPreview.ZoomToFit(true);
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

            if (axisIndex == 1)
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
            else if (axisIndex == 2)
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
            else
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

        InitializeAllFeaturePositions();
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
        InitializeAllFeaturePositions();
        if (radFitToPlate.Checked)
            RecalculateAutoSplitLines();
        pnlPreview.Invalidate();
    }

    private void UpdateSpikeDepth()
    {
        var grooveDepth = (double)nudGrooveDepth.Value;
        var weldGap = (double)nudSpikeWeldGap.Value;
        nudSpikeDepth.Value = (decimal)(grooveDepth + weldGap);
    }

    private void OnSpikeParamChanged(object sender, EventArgs e)
    {
        UpdateSpikeDepth();
        if (radFitToPlate.Checked)
            RecalculateAutoSplitLines();
        pnlPreview.Invalidate();
    }

    private void OnFeatureCountChanged(object sender, EventArgs e)
    {
        InitializeAllFeaturePositions();
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
            p.GrooveDepth = p.SpikeDepth + (double)nudGrooveDepth.Value;
            p.SpikeWeldGap = (double)nudSpikeWeldGap.Value;
            p.SpikeAngle = (double)nudSpikeAngle.Value;
            p.SpikePairCount = (int)nudSpikePairCount.Value;
        }
        return p;
    }

    // --- Feature Position Management ---

    private int GetFeatureCount()
    {
        if (radTabs.Checked) return (int)nudTabCount.Value;
        if (radSpike.Checked) return (int)nudSpikePairCount.Value;
        return 0;
    }

    private void GetExtent(SplitLine sl, out double start, out double end)
    {
        if (sl.Axis == CutOffAxis.Vertical)
        {
            start = _drawingBounds.Bottom;
            end = _drawingBounds.Top;
        }
        else
        {
            start = _drawingBounds.Left;
            end = _drawingBounds.Right;
        }
    }

    private void InitializeFeaturePositions(SplitLine sl)
    {
        var count = GetFeatureCount();
        GetExtent(sl, out var start, out var end);
        var extent = end - start;

        sl.FeaturePositions.Clear();
        if (count <= 0 || extent <= 0) return;

        if (radSpike.Checked)
        {
            var margin = extent * 0.15;
            if (count == 1)
            {
                sl.FeaturePositions.Add(start + extent / 2);
            }
            else
            {
                var usable = extent - 2 * margin;
                for (var i = 0; i < count; i++)
                    sl.FeaturePositions.Add(start + margin + usable * i / (count - 1));
            }
        }
        else
        {
            var spacing = extent / (count + 1);
            for (var i = 0; i < count; i++)
                sl.FeaturePositions.Add(start + spacing * (i + 1));
        }
    }

    private void InitializeAllFeaturePositions()
    {
        foreach (var sl in _splitLines)
            InitializeFeaturePositions(sl);
    }

    // --- Mouse Interaction ---

    private void OnPreviewMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var worldPt = pnlPreview.PointControlToWorld(e.Location);

        // Check for feature handle hit
        var (lineIdx, featIdx) = HitTestFeatureHandle(worldPt);
        if (lineIdx >= 0)
        {
            _dragLineIndex = lineIdx;
            _dragFeatureIndex = featIdx;
            return;
        }

        // Split line placement
        if (radManual.Checked && _placingLine)
        {
            var snapped = SnapToMidpoint(worldPt);
            var position = _currentAxis == CutOffAxis.Vertical ? snapped.X : snapped.Y;
            var sl = new SplitLine(position, _currentAxis);
            InitializeFeaturePositions(sl);
            _splitLines.Add(sl);
            UpdateUI();
            pnlPreview.Invalidate();
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var worldPt = pnlPreview.PointControlToWorld(e.Location);

        if (_dragLineIndex >= 0)
        {
            var sl = _splitLines[_dragLineIndex];
            GetExtent(sl, out var start, out var end);
            var pos = sl.Axis == CutOffAxis.Vertical ? worldPt.Y : worldPt.X;
            pos = System.Math.Max(start, System.Math.Min(end, pos));
            sl.FeaturePositions[_dragFeatureIndex] = pos;
            pnlPreview.Invalidate();
        }
        else
        {
            var (lineIdx, featIdx) = HitTestFeatureHandle(worldPt);
            if (lineIdx != _hoverLineIndex || featIdx != _hoverFeatureIndex)
            {
                _hoverLineIndex = lineIdx;
                _hoverFeatureIndex = featIdx;
                pnlPreview.Cursor = _hoverLineIndex >= 0
                    ? (_splitLines[_hoverLineIndex].Axis == CutOffAxis.Vertical ? Cursors.SizeNS : Cursors.SizeWE)
                    : Cursors.Cross;
                pnlPreview.Invalidate();
            }
        }

        lblCursor.Text = $"Cursor: {worldPt.X:F2}, {worldPt.Y:F2}";
    }

    private void OnPreviewMouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _dragLineIndex >= 0)
        {
            _dragLineIndex = -1;
            _dragFeatureIndex = -1;
            pnlPreview.Invalidate();
        }
    }

    private (int lineIndex, int featureIndex) HitTestFeatureHandle(Vector worldPt)
    {
        if (radStraight.Checked) return (-1, -1);

        var hitRadius = HandleRadius / pnlPreview.ViewScale;
        for (var li = 0; li < _splitLines.Count; li++)
        {
            var sl = _splitLines[li];
            for (var fi = 0; fi < sl.FeaturePositions.Count; fi++)
            {
                var center = GetFeatureHandleWorld(sl, fi);
                var dx = worldPt.X - center.X;
                var dy = worldPt.Y - center.Y;
                if (dx * dx + dy * dy <= hitRadius * hitRadius)
                    return (li, fi);
            }
        }
        return (-1, -1);
    }

    private Vector GetFeatureHandleWorld(SplitLine sl, int featureIndex)
    {
        var pos = sl.FeaturePositions[featureIndex];
        return sl.Axis == CutOffAxis.Vertical
            ? new Vector(sl.Position, pos)
            : new Vector(pos, sl.Position);
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
        var threshold = SnapThreshold / pnlPreview.ViewScale;

        if (_currentAxis == CutOffAxis.Vertical && System.Math.Abs(pt.X - midX) < threshold)
            return new Vector(midX, pt.Y);
        if (_currentAxis == CutOffAxis.Horizontal && System.Math.Abs(pt.Y - midY) < threshold)
            return new Vector(pt.X, midY);
        return pt;
    }

    // --- Rendering (drawn on top of entities via SplitPreview) ---

    private void PaintOverlays(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Piece color overlays
        var regions = BuildPreviewRegions();
        for (var i = 0; i < regions.Count; i++)
        {
            var color = PieceColors[i % PieceColors.Length];
            using var brush = new SolidBrush(color);
            var r = regions[i];
            var tl = pnlPreview.PointWorldToGraph(r.Left, r.Top);
            var br = pnlPreview.PointWorldToGraph(r.Right, r.Bottom);
            g.FillRectangle(brush, System.Math.Min(tl.X, br.X), System.Math.Min(tl.Y, br.Y),
                System.Math.Abs(br.X - tl.X), System.Math.Abs(br.Y - tl.Y));
        }

        // Split lines
        using var splitPen = new Pen(Color.FromArgb(255, 82, 82));
        splitPen.DashStyle = DashStyle.Dash;
        foreach (var sl in _splitLines)
        {
            PointF p1, p2;
            if (sl.Axis == CutOffAxis.Vertical)
            {
                p1 = pnlPreview.PointWorldToGraph(sl.Position, _drawingBounds.Bottom - 10);
                p2 = pnlPreview.PointWorldToGraph(sl.Position, _drawingBounds.Top + 10);
            }
            else
            {
                p1 = pnlPreview.PointWorldToGraph(_drawingBounds.Left - 10, sl.Position);
                p2 = pnlPreview.PointWorldToGraph(_drawingBounds.Right + 10, sl.Position);
            }
            g.DrawLine(splitPen, p1, p2);
        }

        // Feature position handles
        if (!radStraight.Checked)
        {
            for (var li = 0; li < _splitLines.Count; li++)
            {
                var sl = _splitLines[li];
                for (var fi = 0; fi < sl.FeaturePositions.Count; fi++)
                {
                    var center = pnlPreview.PointWorldToGraph(GetFeatureHandleWorld(sl, fi));
                    var isDrag = li == _dragLineIndex && fi == _dragFeatureIndex;
                    var isHover = li == _hoverLineIndex && fi == _hoverFeatureIndex;
                    var fillColor = isDrag ? Color.FromArgb(255, 82, 82)
                                  : isHover ? Color.FromArgb(255, 183, 77)
                                  : Color.White;
                    using var fill = new SolidBrush(fillColor);
                    using var border = new Pen(Color.FromArgb(80, 80, 80));
                    g.FillEllipse(fill, center.X - HandleRadius, center.Y - HandleRadius,
                        HandleRadius * 2, HandleRadius * 2);
                    g.DrawEllipse(border, center.X - HandleRadius, center.Y - HandleRadius,
                        HandleRadius * 2, HandleRadius * 2);
                }
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

    // --- SplitPreview control ---

    private class SplitPreview : EntityView
    {
        public Action<Graphics> DrawOverlays { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawOverlays?.Invoke(e.Graphics);
        }
    }
}

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.Forms;

public partial class SimplifierViewerForm : Form
{
    private EntityView entityView;
    private GeometrySimplifier simplifier;
    private List<Shape> shapes;
    private List<ArcCandidate> candidates;

    public event System.Action<List<Entity>> Applied;

    public SimplifierViewerForm()
    {
        InitializeComponent();
    }

    public void LoadShapes(List<Shape> shapes, EntityView view, double tolerance = 0.004)
    {
        this.shapes = shapes;
        this.entityView = view;
        numTolerance.Value = (decimal)tolerance;
        simplifier = new GeometrySimplifier { Tolerance = tolerance };
        RunAnalysis();
        Show();
        BringToFront();
    }

    private void RunAnalysis()
    {
        candidates = new List<ArcCandidate>();
        for (var i = 0; i < shapes.Count; i++)
        {
            var shapeCandidates = simplifier.Analyze(shapes[i]);
            foreach (var c in shapeCandidates)
                c.ShapeIndex = i;

            var axis = GeometrySimplifier.DetectMirrorAxis(shapes[i]);
            if (axis.IsValid)
                simplifier.Symmetrize(shapeCandidates, axis);

            candidates.AddRange(shapeCandidates);
        }
        RefreshList();
    }

    private void RefreshList()
    {
        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var c in candidates)
        {
            var item = new ListViewItem(c.LineCount.ToString());
            item.Checked = c.IsSelected;
            item.SubItems.Add(c.FittedArc.Radius.ToString("F3"));
            item.SubItems.Add(c.MaxDeviation.ToString("F4"));
            item.SubItems.Add($"{c.BoundingBox.Center.X:F1}, {c.BoundingBox.Center.Y:F1}");
            item.Tag = c;
            listView.Items.Add(item);
        }

        listView.EndUpdate();
        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        var selected = candidates.Count(c => c.IsSelected);
        lblCount.Text = $"{selected} of {candidates.Count} selected";
        btnApply.Enabled = selected > 0;
    }

    private void OnItemSelected(object sender, ListViewItemSelectionChangedEventArgs e)
    {
        if (!e.IsSelected || e.Item.Tag is not ArcCandidate candidate)
        {
            entityView?.ClearSimplifierPreview();
            return;
        }

        // Highlight the candidate lines in the shape
        var shape = shapes[candidate.ShapeIndex];
        var highlightEntities = new List<Entity>();
        for (var i = candidate.StartIndex; i <= candidate.EndIndex; i++)
            highlightEntities.Add(shape.Entities[i]);

        entityView.SimplifierHighlight = highlightEntities;
        entityView.SimplifierPreview = candidate.FittedArc;

        // Build tolerance zone by offsetting each original line both directions
        var tol = simplifier.Tolerance;
        var leftEntities = new List<Entity>();
        var rightEntities = new List<Entity>();
        foreach (var entity in highlightEntities)
        {
            var left = entity.OffsetEntity(tol, OffsetSide.Left);
            var right = entity.OffsetEntity(tol, OffsetSide.Right);
            if (left != null) leftEntities.Add(left);
            if (right != null) rightEntities.Add(right);
        }
        entityView.SimplifierToleranceLeft = leftEntities;
        entityView.SimplifierToleranceRight = rightEntities;

        // Zoom with padding for the tolerance zone
        var padded = new Box(
            candidate.BoundingBox.X - tol * 2,
            candidate.BoundingBox.Y - tol * 2,
            candidate.BoundingBox.Width + tol * 4,
            candidate.BoundingBox.Length + tol * 4);
        entityView.ZoomToArea(padded);
    }

    private void OnItemChecked(object sender, ItemCheckedEventArgs e)
    {
        if (e.Item.Tag is ArcCandidate candidate)
        {
            candidate.IsSelected = e.Item.Checked;
            UpdateCountLabel();
        }
    }

    private void OnToleranceChanged(object sender, System.EventArgs e)
    {
        simplifier.Tolerance = (double)numTolerance.Value;
        entityView?.ClearSimplifierPreview();
        RunAnalysis();
    }

    private void OnApplyClick(object sender, System.EventArgs e)
    {
        var byShape = candidates
            .Where(c => c.IsSelected)
            .GroupBy(c => c.ShapeIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (var i = 0; i < shapes.Count; i++)
        {
            if (byShape.TryGetValue(i, out var selected))
                shapes[i] = simplifier.Apply(shapes[i], selected);
        }

        var entities = shapes.SelectMany(s => s.Entities).ToList();
        entityView?.ClearSimplifierPreview();
        Applied?.Invoke(entities);
        Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        entityView?.ClearSimplifierPreview();
        base.OnFormClosing(e);
    }
}

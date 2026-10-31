using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Dimensions;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public sealed class DimensionDragEventArgs : EventArgs
    {
        public DimensionDragEventArgs(int dimension, double offset)
        {
            Dimension = dimension;
            Offset = offset;
        }

        public int Dimension { get; }

        /// <summary>The new distance from the part's extents, already kept clear of the part.</summary>
        public double Offset { get; }
    }

    public partial class EntityView
    {
        private const int DimensionHitPixels = 6;
        private const int DimensionMinimumPixels = 12;
        private const int DimensionGapPixels = 6;
        private const int DimensionOvershootPixels = 5;
        private const int ArrowLengthPixels = 9;
        private const int ArrowHalfWidthPixels = 3;

        private readonly Font dimensionFont = new Font("Segoe UI", 9f);
        private int? draggedDimension;

        /// <summary>Resolved dimensions to draw, in dimension order; invalid entries are not drawn.</summary>
        public IReadOnlyList<DimensionLayout> DimensionLayouts { get; set; } = Array.Empty<DimensionLayout>();

        public int SelectedDimensionIndex { get; set; } = -1;

        public event EventHandler<DimensionDragEventArgs> DimensionDragged;

        private void RaiseDimensionDragged(int index, Point location)
        {
            var layout = DimensionLayouts.ElementAtOrDefault(index);
            if (layout is not { IsValid: true })
                return;
            var offset = layout.OffsetAt(PointControlToWorld(location), LengthGuiToWorld(DimensionMinimumPixels));
            DimensionDragged?.Invoke(this, new DimensionDragEventArgs(index, offset));
        }

        private int? HitTestDimension(Point location)
        {
            var point = PointControlToWorld(location);
            var tolerance = LengthGuiToWorld(DimensionHitPixels);
            for (var i = 0; i < DimensionLayouts.Count; i++)
            {
                var layout = DimensionLayouts[i];
                if (layout is not { IsValid: true })
                    continue;
                var line = new Line(layout.LineStart, layout.LineEnd);
                if (point.DistanceTo(line.ClosestPointTo(point)) <= tolerance)
                    return i;
            }
            return null;
        }

        private Cursor DimensionCursor(int index) =>
            DimensionLayouts[index].Kind == DimensionKind.OverallHorizontal ? Cursors.SizeNS : Cursors.SizeWE;

        // Dimensions sit outside the part, so fitting the part alone would leave them off screen.
        private Box WithDimensions(Box box)
        {
            var valid = DimensionLayouts.Where(l => l is { IsValid: true }).ToList();
            if (valid.Count == 0)
                return box;

            var points = new List<Vector>
            {
                new(box.Left, box.Bottom),
                new(box.Right, box.Top),
            };
            foreach (var layout in valid)
            {
                points.Add(layout.LineStart);
                points.Add(layout.LineEnd);
            }
            return points.GetBoundingBox();
        }

        private void DrawDimensions(Graphics g)
        {
            if (DimensionLayouts.Count == 0)
                return;

            var color = Color.FromArgb(120, 220, 160);
            var selectedColor = Color.FromArgb(255, 200, 90);
            using var pen = new Pen(color);
            using var selectedPen = new Pen(selectedColor);
            using var brush = new SolidBrush(color);
            using var selectedBrush = new SolidBrush(selectedColor);

            for (var i = 0; i < DimensionLayouts.Count; i++)
            {
                var layout = DimensionLayouts[i];
                if (layout is not { IsValid: true })
                    continue;
                var selected = i == SelectedDimensionIndex;
                DrawDimension(g, layout, selected ? selectedPen : pen, selected ? selectedBrush : brush);
            }
        }

        private void DrawDimension(Graphics g, DimensionLayout layout, Pen pen, Brush brush)
        {
            var from = PointWorldToGraph(layout.LineStart);
            var to = PointWorldToGraph(layout.LineEnd);
            var length = System.Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (length < 1)
                return;
            var along = new PointF((float)((to.X - from.X) / length), (float)((to.Y - from.Y) / length));

            var clearance = LengthGuiToWorld(DimensionGapPixels);
            DrawExtensionLine(g, pen, PointWorldToGraph(layout.ExtensionTip(layout.LineStart, clearance)), from);
            DrawExtensionLine(g, pen, PointWorldToGraph(layout.ExtensionTip(layout.LineEnd, clearance)), to);
            g.DrawLine(pen, from, to);
            DrawArrow(g, brush, from, along);
            DrawArrow(g, brush, to, new PointF(-along.X, -along.Y));

            var mid = PointWorldToGraph(layout.MidPoint);
            var size = g.MeasureString(layout.ValueText, dimensionFont);
            var state = g.Save();
            g.TranslateTransform(mid.X, mid.Y);
            if (layout.Kind == DimensionKind.OverallVertical)
                g.RotateTransform(-90f);
            g.FillRectangle(labelBackBrush, -size.Width / 2f, -size.Height / 2f, size.Width, size.Height);
            g.DrawString(layout.ValueText, dimensionFont, brush, -size.Width / 2f, -size.Height / 2f);
            g.Restore(state);
        }

        // Runs a little past the dimension line, the way drafted extension lines do.
        private static void DrawExtensionLine(Graphics g, Pen pen, PointF from, PointF to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (length < 1)
                return;
            var end = new PointF(to.X + dx / length * DimensionOvershootPixels,
                to.Y + dy / length * DimensionOvershootPixels);
            g.DrawLine(pen, from, end);
        }

        private static void DrawArrow(Graphics g, Brush brush, PointF tip, PointF direction)
        {
            var baseX = tip.X + direction.X * ArrowLengthPixels;
            var baseY = tip.Y + direction.Y * ArrowLengthPixels;
            g.FillPolygon(brush, new[]
            {
                tip,
                new PointF(baseX - direction.Y * ArrowHalfWidthPixels, baseY + direction.X * ArrowHalfWidthPixels),
                new PointF(baseX + direction.Y * ArrowHalfWidthPixels, baseY - direction.X * ArrowHalfWidthPixels),
            });
        }
    }
}

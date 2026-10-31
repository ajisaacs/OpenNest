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
        public DimensionDragEventArgs(int dimension, double offset, double? angle = null)
        {
            Dimension = dimension;
            Offset = offset;
            Angle = angle;
        }

        public int Dimension { get; }

        /// <summary>
        /// The new distance from the part's extents, or from the curve for a radius or diameter,
        /// already kept clear of the part.
        /// </summary>
        public double Offset { get; }

        /// <summary>Radius and diameter: the new leader angle; null for overall dimensions.</summary>
        public double? Angle { get; }
    }

    public partial class EntityView
    {
        private const int DimensionHitPixels = 6;
        private const int DimensionMinimumPixels = 12;
        private const int DimensionGapPixels = 6;
        private const int DimensionOvershootPixels = 5;
        private const int ArrowLengthPixels = 9;
        private const int ArrowHalfWidthPixels = 3;
        private const int CenterMarkPixels = 4;

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
            var point = PointControlToWorld(location);
            var minimum = LengthGuiToWorld(DimensionMinimumPixels);
            if (layout.IsRadial)
            {
                var (radialOffset, angle) = layout.RadialPlacementAt(point, minimum);
                DimensionDragged?.Invoke(this, new DimensionDragEventArgs(index, radialOffset, angle));
                return;
            }
            var offset = layout.OffsetAt(point, minimum);
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

        private Cursor DimensionCursor(int index) => DimensionLayouts[index].Kind switch
        {
            DimensionKind.OverallHorizontal => Cursors.SizeNS,
            DimensionKind.OverallVertical => Cursors.SizeWE,
            _ => Cursors.SizeAll,
        };

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
                if (layout.IsRadial)
                {
                    points.Add(layout.ArrowPoint);
                    points.Add(layout.FarArrowPoint);
                }
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
            if (layout.IsRadial)
            {
                DrawRadialDimension(g, layout, pen, brush);
                return;
            }

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

        // A leader from the center (radius) or the far side of the curve (diameter) out to the
        // label, with arrows on the curve pointing at it from the label's side.
        private void DrawRadialDimension(Graphics g, DimensionLayout layout, Pen pen, Brush brush)
        {
            var start = PointWorldToGraph(layout.LineStart);
            var label = PointWorldToGraph(layout.LineEnd);
            var arrow = PointWorldToGraph(layout.ArrowPoint);
            g.DrawLine(pen, start, label);

            var center = PointWorldToGraph(layout.Center);
            if (layout.Kind == DimensionKind.Diameter)
            {
                // Both tips on the curve, tails across the circle, the label beyond one end.
                DrawArrowAt(g, brush, arrow, center);
                DrawArrowAt(g, brush, PointWorldToGraph(layout.FarArrowPoint), center);
            }
            else
            {
                // The tail runs along the leader toward the label, outside or inside the curve.
                var outside = layout.LineEnd.DistanceTo(layout.Center) >= layout.Radius;
                DrawArrowAt(g, brush, arrow, outside ? label : center);
                g.DrawLine(pen, center.X - CenterMarkPixels, center.Y, center.X + CenterMarkPixels, center.Y);
                g.DrawLine(pen, center.X, center.Y - CenterMarkPixels, center.X, center.Y + CenterMarkPixels);
            }

            // The label sits past the leader's end, on the side away from the curve's center.
            var text = layout.DisplayText;
            var size = g.MeasureString(text, dimensionFont);
            var dx = System.Math.Cos(layout.Angle);
            var dy = -System.Math.Sin(layout.Angle); // Screen Y runs down.
            var reach = (float)(System.Math.Abs(dx) * size.Width / 2 + System.Math.Abs(dy) * size.Height / 2) + 2;
            var cx = label.X + (float)dx * reach;
            var cy = label.Y + (float)dy * reach;
            g.FillRectangle(labelBackBrush, cx - size.Width / 2f, cy - size.Height / 2f, size.Width, size.Height);
            g.DrawString(text, dimensionFont, brush, cx - size.Width / 2f, cy - size.Height / 2f);
        }

        // An arrow with its tip at tip and its tail toward toward.
        private static void DrawArrowAt(Graphics g, Brush brush, PointF tip, PointF toward)
        {
            var dx = toward.X - tip.X;
            var dy = toward.Y - tip.Y;
            var length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (length < 1)
                return;
            DrawArrow(g, brush, tip, new PointF(dx / length, dy / length));
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

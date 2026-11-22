using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public partial class EntityView
    {
        private const int LabelBoxHandlePixels = 4;
        private const int LabelBoxMinimumPixels = 6;
        private static readonly Color LabelBoxColor = Color.FromArgb(230, 90, 200);

        // The grabbed corner (0-3, as Labels.LabelBox.Corners orders them), or -1 for the body;
        // null when nothing is being dragged.
        private int? draggedLabelBoxPart;
        private Vector labelBoxGrab;
        private Labels.LabelBox labelBoxAtPress;

        /// <summary>
        /// Where the part number label would be etched, in world coordinates, drawn as a dashed
        /// rectangle the user can move and resize; null draws nothing.
        /// </summary>
        public Labels.LabelBox LabelBox { get; set; }

        /// <summary>The label box was moved or resized; the argument is its new placement.</summary>
        public event EventHandler<Labels.LabelBox> LabelBoxChanged;

        private bool BeginLabelBoxDrag(Point location)
        {
            draggedLabelBoxPart = HitTestLabelBox(location);
            if (draggedLabelBoxPart is not { } part || LabelBox == null)
                return false;

            labelBoxAtPress = LabelBox.Clone();
            labelBoxGrab = labelBoxAtPress.Center - PointControlToWorld(location);
            return true;
        }

        private void EndLabelBoxDrag() => draggedLabelBoxPart = null;

        // A corner handle first, then anywhere inside the box.
        private int? HitTestLabelBox(Point location)
        {
            if (LabelBox is not { IsValid: true } box)
                return null;

            var corners = box.Corners();
            for (var i = 0; i < corners.Length; i++)
            {
                var at = PointWorldToControl(corners[i]);
                var reach = LabelBoxHandlePixels + 3;
                if (System.Math.Abs(at.X - location.X) <= reach && System.Math.Abs(at.Y - location.Y) <= reach)
                    return i;
            }
            return box.Contains(PointControlToWorld(location)) ? -1 : null;
        }

        private void RaiseLabelBoxDragged(Point location)
        {
            if (draggedLabelBoxPart is not { } part || labelBoxAtPress == null)
                return;

            var point = PointControlToWorld(location);
            if (part < 0)
            {
                LabelBoxChanged?.Invoke(this, new Labels.LabelBox(point + labelBoxGrab, labelBoxAtPress.Width,
                    labelBoxAtPress.Height, labelBoxAtPress.Rotation));
                return;
            }

            // The corner follows the pointer and the opposite corner stays put, in the box's own frame.
            var corners = labelBoxAtPress.Corners();
            var anchor = labelBoxAtPress.ToLocal(corners[(part + 2) % 4]);
            var moved = labelBoxAtPress.ToLocal(point);
            var minimum = LengthGuiToWorld(LabelBoxMinimumPixels);
            var width = System.Math.Max(System.Math.Abs(moved.X - anchor.X), minimum);
            var height = System.Math.Max(System.Math.Abs(moved.Y - anchor.Y), minimum);
            var centerX = anchor.X + System.Math.Sign(moved.X - anchor.X == 0 ? 1 : moved.X - anchor.X) * width / 2;
            var centerY = anchor.Y + System.Math.Sign(moved.Y - anchor.Y == 0 ? 1 : moved.Y - anchor.Y) * height / 2;
            LabelBoxChanged?.Invoke(this, new Labels.LabelBox(labelBoxAtPress.ToWorld(new Vector(centerX, centerY)),
                width, height, labelBoxAtPress.Rotation));
        }

        private Cursor LabelBoxCursor(int part) => part < 0 ? Cursors.SizeAll : Cursors.SizeNWSE;

        private void DrawLabelBox(Graphics g)
        {
            if (LabelBox is not { IsValid: true } box)
                return;

            var points = box.Corners().Select(PointWorldToGraph).ToArray();
            using var pen = new Pen(LabelBoxColor) { DashStyle = DashStyle.Dash };
            using var brush = new SolidBrush(Color.FromArgb(30, LabelBoxColor));
            g.FillPolygon(brush, points);
            g.DrawPolygon(pen, points);

            using var handleBrush = new SolidBrush(LabelBoxColor);
            foreach (var point in points)
            {
                g.FillRectangle(handleBrush, point.X - LabelBoxHandlePixels, point.Y - LabelBoxHandlePixels,
                    LabelBoxHandlePixels * 2, LabelBoxHandlePixels * 2);
            }
        }
    }
}

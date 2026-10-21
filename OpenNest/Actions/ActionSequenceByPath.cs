using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    /// <summary>
    /// Sequences the plate along lines dragged over it with the left button, cut in the order
    /// they are drawn; lifting the button between lines is a rapid that claims no parts. The
    /// plate renumbers after each line. Backspace or Ctrl+Z removes the last line, right-click
    /// clears them all, and Escape or the window's Done ends the tool.
    /// </summary>
    [DisplayName("Sequence by Path")]
    public class ActionSequenceByPath : Action
    {
        // Screen distance between recorded points, so a line stays smooth but small.
        private const float PointSpacingPixels = 4f;
        private const float LabelRadius = 8f;

        private readonly Forms.SequenceByPathForm form;
        private readonly List<List<Vector>> lines = new();
        private readonly List<Part> originalOrder;
        private readonly Pen linePen;
        private readonly Pen jumpPen;
        private readonly Brush labelBrush = new SolidBrush(Color.Red);
        private readonly Font labelFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private readonly StringFormat centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        private List<Vector> drawing;
        private bool disconnecting;

        public override bool ResumeOnEscape => false;

        public ActionSequenceByPath(PlateView plateView)
            : base(plateView)
        {
            originalOrder = plateView.Plate.Parts.ToList();
            linePen = new Pen(Color.Red, 2.0f)
            {
                CustomEndCap = new AdjustableArrowCap(4f, 5f),
                LineJoin = LineJoin.Round,
            };
            jumpPen = new Pen(Color.Gray, 1.0f) { DashStyle = DashStyle.Dash };

            form = new Forms.SequenceByPathForm();
            form.UndoClicked += (_, _) => RemoveLastLine();
            form.ClearClicked += (_, _) => ClearLines();
            form.DoneClicked += (_, _) => plateView.EndAction();
            form.FormClosed += (_, _) =>
            {
                if (!disconnecting)
                    plateView.EndAction();
            };
            form.Show(plateView.FindForm());

            plateView.MouseDown += plateView_MouseDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseUp += plateView_MouseUp;
            plateView.KeyDown += plateView_KeyDown;
            plateView.Paint += plateView_Paint;
        }

        private void plateView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ClearLines();
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;

            drawing = new List<Vector> { plateView.PointControlToWorld(e.Location) };
            plateView.Invalidate();
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            if (drawing == null || e.Button != MouseButtons.Left)
                return;

            var point = plateView.PointControlToWorld(e.Location);
            if (plateView.LengthWorldToGui(point.DistanceTo(drawing[^1])) < PointSpacingPixels)
                return;
            drawing.Add(point);
            plateView.Invalidate();
        }

        private void plateView_MouseUp(object sender, MouseEventArgs e)
        {
            if (drawing == null || e.Button != MouseButtons.Left)
                return;

            var line = drawing;
            drawing = null;
            var point = plateView.PointControlToWorld(e.Location);
            if (point.DistanceTo(line[^1]) > 0)
                line.Add(point);

            // A click without a drag is not a line.
            if (DrawnPathSequencing.HasLength(line))
            {
                lines.Add(line);
                Resequence();
            }
            plateView.Invalidate();
        }

        private void plateView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData is Keys.Back or (Keys.Control | Keys.Z))
            {
                RemoveLastLine();
                e.Handled = true;
            }
        }

        private void RemoveLastLine()
        {
            if (lines.Count == 0)
                return;
            lines.RemoveAt(lines.Count - 1);
            Resequence();
        }

        private void ClearLines()
        {
            if (lines.Count == 0)
                return;
            lines.Clear();
            Resequence();
        }

        /// <summary>Sequences by the current lines, or restores the order the tool started with when none are left.</summary>
        private void Resequence()
        {
            var plate = plateView.Plate;
            if (lines.Count > 0)
                DrawnPathSequencing.Apply(plate, lines);
            else if (plate.Parts.Count == originalOrder.Count && originalOrder.All(plate.Parts.Contains))
                plate.Parts.Reorder(originalOrder);

            form.SetLineCount(lines.Count);
            plateView.Invalidate();
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (var i = 1; i < lines.Count; i++)
                g.DrawLine(jumpPen, ToGraph(lines[i - 1][^1]), ToGraph(lines[i][0]));
            if (drawing != null && lines.Count > 0)
                g.DrawLine(jumpPen, ToGraph(lines[^1][^1]), ToGraph(drawing[0]));

            for (var i = 0; i < lines.Count; i++)
                DrawLine(g, lines[i], i + 1);
            if (drawing != null)
                DrawLine(g, drawing, lines.Count + 1);

            g.SmoothingMode = smoothing;
        }

        private void DrawLine(Graphics g, List<Vector> line, int number)
        {
            if (line.Count > 1)
                g.DrawLines(linePen, line.Select(ToGraph).ToArray());

            var start = ToGraph(line[0]);
            g.FillEllipse(labelBrush, start.X - LabelRadius, start.Y - LabelRadius, LabelRadius * 2, LabelRadius * 2);
            g.DrawString(number.ToString(), labelFont, Brushes.White, start, centered);
        }

        private PointF ToGraph(Vector point) => plateView.PointWorldToGraph(point);

        public override void DisconnectEvents()
        {
            disconnecting = true;
            plateView.MouseDown -= plateView_MouseDown;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseUp -= plateView_MouseUp;
            plateView.KeyDown -= plateView_KeyDown;
            plateView.Paint -= plateView_Paint;
            form.Close();
            form.Dispose();
            linePen.Dispose();
            jumpPen.Dispose();
            labelBrush.Dispose();
            labelFont.Dispose();
            centered.Dispose();
            plateView.Invalidate();
        }

        // Escape mid-drag drops only the line being drawn.
        public override void CancelAction()
        {
            drawing = null;
            plateView.Invalidate();
        }

        public override bool IsBusy() => drawing != null;
    }
}

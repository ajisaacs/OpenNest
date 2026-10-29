using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.Help;

namespace OpenNest.Actions
{
    /// <summary>
    /// Trims or extends a cut-off against the cut-offs that cross it (see <see cref="CutOffTrim"/>).
    /// Trim removes the clicked piece; Extend grows the end nearer the click. Tab switches mode.
    /// </summary>
    [DisplayName("Trim/Extend Cut-Off")]
    public class ActionTrimExtendCutOff : Action
    {
        private bool extend;
        private CutOff hovered;
        private CutOffEdit preview;
        private string message;

        public ActionTrimExtendCutOff(PlateView plateView)
            : base(plateView)
        {
            ConnectEvents();
        }

        internal bool Extending => extend;

        public override void ConnectEvents()
        {
            plateView.MouseMove += OnMouseMove;
            plateView.MouseDown += OnMouseDown;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
        }

        public override void DisconnectEvents()
        {
            plateView.MouseMove -= OnMouseMove;
            plateView.MouseDown -= OnMouseDown;
            plateView.KeyDown -= OnKeyDown;
            plateView.Paint -= OnPaint;

            hovered = null;
            preview = null;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => false;

        public override string Hint => message ?? (extend
            ? "click near the end of a cut-off to extend it, Tab switches to Trim, Esc finishes, F1 for help"
            : "click the piece of a cut-off to remove, Tab switches to Extend, Esc finishes, F1 for help");

        public override string HelpTopic => HelpTopics.TrimExtendCutOff;

        private CutOffEdit EditAt(CutOff cutOff, Vector point) => extend
            ? CutOffTrim.Extend(plateView.Plate, cutOff, point)
            : CutOffTrim.Trim(plateView.Plate, cutOff, point, plateView.CutOffSettings);

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var point = plateView.CurrentPoint;
            hovered = plateView.GetCutOffAtPoint(point, 5.0 / plateView.ViewScale);
            preview = hovered == null ? null : EditAt(hovered, point);
            SetMessage(preview?.Refusal);
            plateView.Invalidate();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var point = plateView.CurrentPoint;
            var cutOff = plateView.GetCutOffAtPoint(point, 5.0 / plateView.ViewScale);
            if (cutOff == null)
                return;

            var edit = EditAt(cutOff, point);
            if (plateView.CutOffs.Apply(edit, extend ? "Extend Cut-Off" : "Trim Cut-Off"))
            {
                hovered = null;
                preview = null;
                SetMessage(edit.Notice);
            }
            else
            {
                SetMessage(edit.Refusal);
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
            {
                extend = !extend;
                preview = hovered == null ? null : EditAt(hovered, plateView.CurrentPoint);
                message = preview?.Refusal;
                plateView.RefreshActionStatus();
                plateView.Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                plateView.SetAction(typeof(ActionSelect));
            }
        }

        private void SetMessage(string value)
        {
            if (message == value)
                return;
            message = value;
            plateView.RefreshActionStatus();
        }

        // Dashed red over the piece a trim removes, dashed green over the piece an extend adds.
        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (preview == null || !preview.Succeeded)
                return;

            var width = 2f / plateView.ViewScale;
            using var removed = new Pen(Color.FromArgb(200, 220, 20, 60), width) { DashStyle = DashStyle.Dash };
            using var added = new Pen(Color.FromArgb(200, 34, 139, 34), width) { DashStyle = DashStyle.Dash };
            var sheet = plateView.Plate.BoundingBox(includeParts: false);
            foreach (var change in preview.Changes)
            {
                var cutOff = change.CutOff;
                var vertical = cutOff.Axis == CutOffAxis.Vertical;
                var (oldStart, oldEnd) = CutOffTrim.NominalSpan(plateView.Plate, cutOff);
                var newStart = change.StartLimit ?? (vertical ? sheet.Bottom : sheet.Left);
                var newEnd = change.EndLimit ?? (vertical ? sheet.Top : sheet.Right);
                DrawPiece(e.Graphics, cutOff, oldStart, newStart, newStart > oldStart ? removed : added);
                DrawPiece(e.Graphics, cutOff, oldEnd, newEnd, newEnd < oldEnd ? removed : added);
            }
        }

        private void DrawPiece(Graphics g, CutOff cutOff, double from, double to, Pen pen)
        {
            if (System.Math.Abs(to - from) <= Math.Tolerance.Epsilon)
                return;
            var vertical = cutOff.Axis == CutOffAxis.Vertical;
            var line = vertical ? cutOff.Position.X : cutOff.Position.Y;
            var a = vertical ? new Vector(line, from) : new Vector(from, line);
            var b = vertical ? new Vector(line, to) : new Vector(to, line);
            g.DrawLine(pen, plateView.PointWorldToGraph(a), plateView.PointWorldToGraph(b));
        }
    }
}

using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Sheet Cut-Off")]
    public class ActionCutOff : Action
    {
        private CutOff previewCutOff;
        private CutOffSettings settings;

        public ActionCutOff(PlateView plateView)
            : base(plateView)
        {
            settings = plateView.CutOffSettings;

            plateView.MouseMove += OnMouseMove;
            plateView.MouseDown += OnMouseDown;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
        }

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

            previewCutOff = null;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => false;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pt = plateView.CurrentPoint;
            var axis = DetectAxis(pt);

            previewCutOff = new CutOff(pt, axis);
            previewCutOff.Regenerate(plateView.Plate, settings);
            plateView.Invalidate();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var pt = plateView.CurrentPoint;
            var axis = DetectAxis(pt);
            var cutoff = new CutOff(pt, axis);

            plateView.Plate.CutOffs.Add(cutoff);
            plateView.Plate.RegenerateCutOffs(settings);
            plateView.Invalidate();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                plateView.SetAction(typeof(ActionSelect));
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (previewCutOff?.Drawing?.Program == null)
                return;

            var program = previewCutOff.Drawing.Program;
            if (program.Codes.Count == 0)
                return;

            using var pen = new Pen(Color.FromArgb(128, 64, 64, 64), 1.5f / plateView.ViewScale)
            {
                DashStyle = DashStyle.Dash
            };

            for (var i = 0; i < program.Codes.Count - 1; i += 2)
            {
                if (program.Codes[i] is RapidMove rapid &&
                    program.Codes[i + 1] is LinearMove linear)
                {
                    var pt1 = plateView.PointWorldToGraph(rapid.EndPoint);
                    var pt2 = plateView.PointWorldToGraph(linear.EndPoint);
                    e.Graphics.DrawLine(pen, pt1, pt2);
                }
            }
        }

        private CutOffAxis DetectAxis(Vector pt)
        {
            var bounds = plateView.Plate.BoundingBox(includeParts: false);
            var distToLeftRight = System.Math.Min(
                System.Math.Abs(pt.X - bounds.X),
                System.Math.Abs(pt.X - (bounds.X + bounds.Width)));
            var distToTopBottom = System.Math.Min(
                System.Math.Abs(pt.Y - bounds.Y),
                System.Math.Abs(pt.Y - (bounds.Y + bounds.Length)));

            return distToTopBottom < distToLeftRight
                ? CutOffAxis.Horizontal
                : CutOffAxis.Vertical;
        }
    }
}

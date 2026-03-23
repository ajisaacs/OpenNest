using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using System.Collections.Generic;
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
        private CutOffAxis lockedAxis = CutOffAxis.Vertical;
        private Dictionary<Part, Entity> perimeterCache;
        private readonly Timer debounceTimer;
        private bool regeneratePending;

        public ActionCutOff(PlateView plateView)
            : base(plateView)
        {
            settings = plateView.CutOffSettings;
            perimeterCache = Plate.BuildPerimeterCache(plateView.Plate);
            debounceTimer = new Timer { Interval = 16 };
            debounceTimer.Tick += OnDebounce;

            plateView.MouseMove += OnMouseMove;
            plateView.MouseDown += OnMouseDown;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
        }

        public override void ConnectEvents()
        {
            perimeterCache = Plate.BuildPerimeterCache(plateView.Plate);

            plateView.MouseMove += OnMouseMove;
            plateView.MouseDown += OnMouseDown;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
        }

        public override void DisconnectEvents()
        {
            debounceTimer.Stop();
            plateView.MouseMove -= OnMouseMove;
            plateView.MouseDown -= OnMouseDown;
            plateView.KeyDown -= OnKeyDown;
            plateView.Paint -= OnPaint;

            previewCutOff = null;
            perimeterCache = null;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => false;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            regeneratePending = true;
            debounceTimer.Start();
        }

        private void OnDebounce(object sender, System.EventArgs e)
        {
            debounceTimer.Stop();

            if (!regeneratePending)
                return;

            regeneratePending = false;
            var pt = plateView.CurrentPoint;
            previewCutOff = new CutOff(pt, lockedAxis);
            previewCutOff.Regenerate(plateView.Plate, settings, perimeterCache);
            plateView.Invalidate();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var pt = plateView.CurrentPoint;
            var cutoff = new CutOff(pt, lockedAxis);

            plateView.Plate.CutOffs.Add(cutoff);
            plateView.Plate.RegenerateCutOffs(settings);
            perimeterCache = Plate.BuildPerimeterCache(plateView.Plate);
            plateView.Invalidate();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                lockedAxis = lockedAxis == CutOffAxis.Vertical
                    ? CutOffAxis.Horizontal
                    : CutOffAxis.Vertical;

                if (previewCutOff != null)
                {
                    previewCutOff = new CutOff(plateView.CurrentPoint, lockedAxis);
                    previewCutOff.Regenerate(plateView.Plate, settings, perimeterCache);
                    plateView.Invalidate();
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                plateView.SetAction(typeof(ActionSelect));
            }
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
    }
}

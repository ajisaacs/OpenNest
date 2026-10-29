using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.Help;

namespace OpenNest.Actions
{
    [DisplayName("Sheet Cut-Off")]
    public class ActionCutOff : Action
    {
        private CutOff previewCutOff;
        private CutOffSettings settings;
        private CutOffAxis lockedAxis = CutOffAxis.Vertical;
        private Dictionary<Part, Entity> perimeterCache;

        public ActionCutOff(PlateView plateView)
            : base(plateView)
        {
            settings = plateView.CutOffSettings;
            ConnectEvents();
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

        public override string Hint =>
            $"click to add a {AxisName(lockedAxis)} line, Space turns it {AxisName(OtherAxis(lockedAxis))}, Esc finishes, F1 for help";

        public override string HelpTopic => HelpTopics.SheetCutOff;

        private static CutOffAxis OtherAxis(CutOffAxis axis) =>
            axis == CutOffAxis.Vertical ? CutOffAxis.Horizontal : CutOffAxis.Vertical;

        private static string AxisName(CutOffAxis axis) =>
            axis == CutOffAxis.Vertical ? "vertical" : "horizontal";

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pt = plateView.CurrentPoint;
            previewCutOff = new CutOff(pt, lockedAxis) { HeadLockOvercut = settings.NewCutOffHeadLockOvercut };
            previewCutOff.Regenerate(plateView.Plate, settings, perimeterCache);
            plateView.Invalidate();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var pt = plateView.CurrentPoint;
            var cutoff = new CutOff(pt, lockedAxis) { HeadLockOvercut = settings.NewCutOffHeadLockOvercut };

            using var change = plateView.BeginChange("Add Cut-Off");
            change.MarkChanged();
            plateView.Plate.CutOffs.Add(cutoff);
            plateView.Plate.RegenerateCutOffs(settings);
            plateView.Invalidate();
            change.Complete();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                lockedAxis = OtherAxis(lockedAxis);
                plateView.RefreshActionStatus();

                if (previewCutOff != null)
                {
                    previewCutOff = new CutOff(plateView.CurrentPoint, lockedAxis)
                    {
                        HeadLockOvercut = settings.NewCutOffHeadLockOvercut,
                    };
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

            // Match the applied cut-off line's look (orange-red dash) so the
            // preview is exactly what the click produces; the alpha keeps it
            // reading as a preview.
            using var pen = new Pen(Color.FromArgb(128, 255, 69, 0), 1.5f / plateView.ViewScale)
            {
                DashStyle = DashStyle.Dash,
            };

            for (var i = 0; i < program.Codes.Count - 1; i += 2)
            {
                if (
                    program.Codes[i] is RapidMove rapid
                    && program.Codes[i + 1] is LinearMove linear
                )
                {
                    var pt1 = plateView.PointWorldToGraph(rapid.EndPoint);
                    var pt2 = plateView.PointWorldToGraph(linear.EndPoint);
                    e.Graphics.DrawLine(pen, pt1, pt2);
                }
            }

            DrawRotateTag(e.Graphics);
        }

        // The status bar is easy to miss, so the cursor says how to turn the line.
        private void DrawRotateTag(Graphics g)
        {
            var text = $"Space: turn {AxisName(OtherAxis(lockedAxis))}";
            var cursor = plateView.PointWorldToGraph(plateView.CurrentPoint);
            var size = g.MeasureString(text, plateView.Font);
            var rect = new RectangleF(cursor.X + 16f, cursor.Y + 16f, size.Width + 6, size.Height + 4);

            using (var background = new SolidBrush(Color.FromArgb(230, Color.White)))
                g.FillRectangle(background, rect);
            g.DrawRectangle(Pens.DimGray, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawString(text, plateView.Font, Brushes.Black, rect.X + 3, rect.Y + 2);
        }
    }
}

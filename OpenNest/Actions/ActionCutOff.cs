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
        private string notice;

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
            notice = null;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => false;

        public override string Hint
        {
            get
            {
                var hint = FullLength
                    ? $"click to add a full-length {AxisName(lockedAxis)} line, Space turns it {AxisName(OtherAxis(lockedAxis))}, Tab stops it at the first crossing, Esc finishes, F1 for help"
                    : $"click to add a {AxisName(lockedAxis)} line stopped at the first crossing, Space turns it {AxisName(OtherAxis(lockedAxis))}, Tab makes it full length, Esc finishes, F1 for help";
                return notice == null ? hint : $"{notice} {hint}";
            }
        }

        public override string HelpTopic => HelpTopics.SheetCutOff;

        private bool FullLength => plateView.CutOffs.FullLengthNewLines;

        private static CutOffAxis OtherAxis(CutOffAxis axis) =>
            axis == CutOffAxis.Vertical ? CutOffAxis.Horizontal : CutOffAxis.Vertical;

        private static string AxisName(CutOffAxis axis) =>
            axis == CutOffAxis.Vertical ? "vertical" : "horizontal";

        private CutOff NewCutOff(Vector point)
        {
            var cutOff = new CutOff(point, lockedAxis) { HeadLockOvercut = settings.NewCutOffHeadLockOvercut };
            var edit = FullLength ? null : CutOffTrim.StopAtFirstCrossing(plateView.Plate, cutOff, settings);
            edit?.Apply();

            if (notice != edit?.Notice)
            {
                notice = edit?.Notice;
                plateView.RefreshActionStatus();
            }
            return cutOff;
        }

        private void UpdatePreview()
        {
            previewCutOff = NewCutOff(plateView.CurrentPoint);
            previewCutOff.Regenerate(plateView.Plate, settings, perimeterCache);
            plateView.Invalidate();
        }

        private void OnMouseMove(object sender, MouseEventArgs e) => UpdatePreview();

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var cutoff = NewCutOff(plateView.CurrentPoint);

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
                    UpdatePreview();
            }
            else if (e.KeyCode == Keys.Tab)
            {
                plateView.CutOffs.FullLengthNewLines = !FullLength;
                plateView.RefreshActionStatus();

                if (previewCutOff != null)
                    UpdatePreview();
                e.Handled = true;
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
            var text = $"Space: turn {AxisName(OtherAxis(lockedAxis))}\nTab: {(FullLength ? "stop at first crossing" : "full length")}";
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

using OpenNest.Controls;
using OpenNest.Geometry;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Zoom Window")]
    public class ActionZoomWindow : Action
    {
        public Vector Point1;
        public Vector Point2;

        private readonly Pen borderPen;
        private readonly Brush fillBrush;

        private Status status;

        public ActionZoomWindow(PlateView plateView)
            : base(plateView)
        {
            borderPen = new Pen(Color.FromArgb(99, 162, 228));
            fillBrush = new SolidBrush(Color.FromArgb(90, 150, 200, 255));

            status = Status.SetFirstPoint;

            plateView.Cursor = Cursors.Arrow;
            plateView.MouseDown += plateView_MouseDown;
            plateView.MouseUp += plateView_MouseUp;
            plateView.MouseMove += plateView_MouseMove;
            plateView.Paint += plateView_Paint;
        }

        public override void DisconnectEvents()
        {
            plateView.Cursor = Cursors.Cross;
            plateView.MouseDown -= plateView_MouseDown;
            plateView.MouseUp -= plateView_MouseUp;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.Paint -= plateView_Paint;
        }

        public override void CancelAction()
        {
            Point1 = Vector.Invalid;
            Point2 = Vector.Invalid;

            plateView.Invalidate();
            status = Status.SetFirstPoint;
        }

        public override bool IsBusy()
        {
            return status != Status.SetFirstPoint;
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            if (status == Status.SetSecondPoint || e.Button == MouseButtons.Left)
            {
                Point2 = plateView.PointControlToWorld(e.Location);
                plateView.Invalidate();
            }
        }

        private void plateView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                CancelAction();
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            if (e.Button == MouseButtons.Left && status == Status.SetFirstPoint)
            {
                Point1 = plateView.PointControlToWorld(e.Location);
                Point2 = Point1;
                plateView.Invalidate();
            }
        }

        private void plateView_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Point2 = plateView.PointControlToWorld(e.Location);

            if (status == Status.SetFirstPoint)
            {
                var distance1 = Point1.DistanceTo(Point2);
                var distance2 = plateView.LengthWorldToGui(distance1);

                if (distance2 > 40)
                {
                    ZoomWindow();
                    CancelAction();
                }
                else
                {
                    status = Status.SetSecondPoint;
                }
            }
            else if (status == Status.SetSecondPoint)
            {
                ZoomWindow();
                CancelAction();
            }
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            if (!Point1.IsValid() || !Point2.IsValid())
                return;

            var rect = GetRectangle();

            e.Graphics.FillRectangle(fillBrush, rect);

            e.Graphics.DrawRectangle(borderPen,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height);

            var centerX = rect.X + rect.Width * 0.5f;
            var centerY = rect.Y + rect.Height * 0.5f;

            const float halfWidth = 10;

            e.Graphics.DrawLine(borderPen, centerX, centerY - halfWidth, centerX, centerY + halfWidth);
            e.Graphics.DrawLine(borderPen, centerX - halfWidth, centerY, centerX + halfWidth, centerY);
        }

        private void ZoomWindow()
        {
            double x, y, w, h;

            if (Point1.X < Point2.X)
            {
                x = Point1.X;
                w = Point2.X - Point1.X;
            }
            else
            {
                x = Point2.X;
                w = Point1.X - Point2.X;
            }

            if (Point1.Y < Point2.Y)
            {
                y = Point1.Y;
                h = Point2.Y - Point1.Y;
            }
            else
            {
                y = Point2.Y;
                h = Point1.Y - Point2.Y;
            }

            plateView.ZoomToArea(x, y, w, h);
            plateView.Refresh();
        }

        private RectangleF GetRectangle() => GetRectangle(Point1, Point2);

        public enum Status
        {
            SetFirstPoint,
            SetSecondPoint
        }
    }
}

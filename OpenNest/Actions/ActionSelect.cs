using OpenNest.Controls;
using OpenNest.Geometry;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Select")]
    public class ActionSelect : Action
    {
        private readonly Pen borderPenContain;
        private readonly Pen borderPenIntersect;
        private readonly Brush fillBrushContain;
        private readonly Brush fillBrushIntersect;

        private Pen borderPen;
        private Brush fillBrush;

        public Vector Point1;
        public Vector Point2;

        private Status status;

        public ActionSelect(PlateView plateView)
            : base(plateView)
        {
            borderPenIntersect = new Pen(Color.FromArgb(50, 255, 50));
            fillBrushIntersect = new SolidBrush(Color.FromArgb(50, 50, 255, 50));

            borderPenContain = new Pen(Color.FromArgb(99, 162, 228));
            fillBrushContain = new SolidBrush(Color.FromArgb(90, 150, 200, 255));

            UpdateBrushAndPen();

            status = Status.SetFirstPoint;

            plateView.MouseDown += plateView_MouseDown;
            plateView.MouseUp += plateView_MouseUp;
            plateView.MouseMove += plateView_MouseMove;
            plateView.Paint += plateView_Paint;
        }

        public override void DisconnectEvents()
        {
            plateView.MouseDown -= plateView_MouseDown;
            plateView.MouseUp -= plateView_MouseUp;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.Paint -= plateView_Paint;
        }

        public override void CancelAction()
        {
            status = Status.SetFirstPoint;

            plateView.DeselectAll();
            plateView.Invalidate();
        }

        public override bool IsBusy()
        {
            return status != Status.SetFirstPoint || plateView.SelectedParts.Count > 0;
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (status == Status.SetSecondPoint)
            {
                Point2 = plateView.PointControlToWorld(e.Location);
                UpdateBrushAndPen();
            }
            else
            {
                if (plateView.SelectedParts.Count == 0)
                    return;

                var offset = plateView.CurrentPoint - plateView.LastPoint;

                foreach (var part in plateView.SelectedParts)
                    part.Offset(offset.X, offset.Y);
            }

            plateView.Invalidate();
        }

        private void plateView_MouseDown(object sender, MouseEventArgs e)
        {
            if (!plateView.AllowSelect)
                return;

            if (e.Button == MouseButtons.Left)
            {
                if (plateView.SelectedParts.Count > 0)
                {
                    var part = plateView.GetPartAtControlPoint(e.Location);

                    if (part == null)
                        CancelAction();
                }

                if (SelectPartAtCurrentPoint())
                    return;

                if (status == Status.SetFirstPoint)
                {
                    Point1 = plateView.PointControlToWorld(e.Location);
                    Point2 = Point1;
                    status = Status.SetSecondPoint;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                CancelAction();
            }
        }

        private void plateView_MouseUp(object sender, MouseEventArgs e)
        {
            if (!plateView.AllowSelect)
                return;

            if (e.Button != MouseButtons.Left)
                return;

            if (status == Status.SetSecondPoint)
            {
                Point2 = plateView.PointControlToWorld(e.Location);
                SelectPartsInWindow();

                plateView.Invalidate();
                status = Status.SetFirstPoint;
            }
            else if (plateView.SelectedParts.Count > 0)
            {
                // Part drag completed — regenerate cut-off programs
                if (plateView.Plate.CutOffs.Count > 0)
                    plateView.Plate.RegenerateCutOffs(plateView.CutOffSettings);
            }
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            if (status != Status.SetSecondPoint)
                return;

            var rect = GetRectangle();

            e.Graphics.FillRectangle(fillBrush, rect);

            e.Graphics.DrawRectangle(borderPen,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height);
        }

        private bool SelectPartAtCurrentPoint()
        {
            var part = plateView.GetPartAtPoint(plateView.CurrentPoint);

            if (part == null)
            {
                plateView.DeselectAll();
                status = Status.SetFirstPoint;
                return false;
            }

            if (Control.ModifierKeys != Keys.Control && plateView.SelectedParts.Contains(part) == false)
                plateView.DeselectAll();

            if (plateView.SelectedParts.Contains(part) == false)
            {
                plateView.SelectedParts.Add(part);
                part.IsSelected = true;
            }

            plateView.NotifySelectionChanged();
            plateView.Invalidate();
            return true;
        }

        private void SelectPartsInWindow()
        {
            var parts = plateView.GetPartsFromWindow(GetRectangle(), SelectionType);

            foreach (var part in parts)
            {
                if (plateView.SelectedParts.Contains(part))
                    continue;

                plateView.SelectedParts.Add(part);
                part.IsSelected = true;
            }

            plateView.NotifySelectionChanged();
        }

        private void UpdateBrushAndPen()
        {
            if (SelectionType == SelectionType.Intersect)
            {
                borderPen = borderPenIntersect;
                fillBrush = fillBrushIntersect;
            }
            else
            {
                borderPen = borderPenContain;
                fillBrush = fillBrushContain;
            }
        }

        private RectangleF GetRectangle() => GetRectangle(Point1, Point2);

        public SelectionType SelectionType
        {
            get
            {
                return Point1.X < Point2.X
                    ? SelectionType.Contains
                    : SelectionType.Intersect;
            }
        }

        public enum Status
        {
            SetFirstPoint,
            SetSecondPoint
        }
    }
}

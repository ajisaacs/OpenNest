using OpenNest.Controls;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Select Area")]
    public class ActionSelectArea : Action
    {
        public Box SelectedArea { get; private set; }
        private Box Bounds { get; set; }
        private List<Box> boxes { get; set; }

        private bool dynamicSelect;
        private bool altSelect;

        private readonly Pen pen;
        private readonly Brush brush;

        private readonly Font font;
        private readonly StringFormat stringFormat;

        public ActionSelectArea(PlateView plateView)
            : base(plateView)
        {
            boxes = new List<Box>();
            pen = new Pen(Color.FromArgb(50, 255, 50));
            brush = new SolidBrush(Color.FromArgb(50, 50, 255, 50));
            font = new Font(SystemFonts.DefaultFont.FontFamily, 14);

            stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            SelectedArea = Box.Empty;
            dynamicSelect = true;

            Update();

            if (dynamicSelect)
                plateView.MouseMove += plateView_MouseMove;
            else
                plateView.MouseClick += plateView_MouseClick;

            plateView.KeyUp += plateView_KeyUp;
            plateView.Paint += plateView_Paint;
        }

        private void plateView_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space:
                    altSelect = !altSelect;
                    UpdateSelectedArea();
                    break;
            }
        }

        public bool DynamicSelect
        {
            get { return dynamicSelect; }
            set
            {
                if (value == dynamicSelect)
                    return;

                dynamicSelect = value;

                if (dynamicSelect)
                {
                    plateView.MouseMove += plateView_MouseMove;
                    plateView.MouseClick -= plateView_MouseClick;
                }
                else
                {
                    plateView.MouseMove -= plateView_MouseMove;
                    plateView.MouseClick += plateView_MouseClick;
                }
            }
        }

        private void plateView_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            if (SelectedArea == Box.Empty)
                return;

            var location = plateView.PointWorldToGraph(SelectedArea.Location);
            var size = new SizeF(
                plateView.LengthWorldToGui(SelectedArea.Width),
                plateView.LengthWorldToGui(SelectedArea.Length));

            var rect = new System.Drawing.RectangleF(location.X, location.Y - size.Height, size.Width, size.Height);

            e.Graphics.DrawRectangle(pen,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height);

            e.Graphics.FillRectangle(brush, rect);

            e.Graphics.DrawString(
                SelectedArea.Size.ToString(2),
                font,
                Brushes.Green,
                rect,
                stringFormat);
        }

        private void plateView_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            UpdateSelectedArea();
        }

        private void plateView_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return;

            UpdateSelectedArea();
            plateView.Invalidate();
        }

        public override void DisconnectEvents()
        {
            plateView.KeyUp -= plateView_KeyUp;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseClick -= plateView_MouseClick;
            plateView.Paint -= plateView_Paint;
        }

        public override void CancelAction()
        {
            plateView.Invalidate();
        }

        public override bool IsBusy()
        {
            return false;
        }

        private void UpdateSelectedArea()
        {
            SelectedArea = altSelect
                ? SpatialQuery.GetLargestBoxHorizontally(plateView.CurrentPoint, Bounds, boxes)
                : SpatialQuery.GetLargestBoxVertically(plateView.CurrentPoint, Bounds, boxes);

            plateView.Invalidate();
        }

        public void Update()
        {
            foreach (var part in plateView.Plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                boxes.Add(part.BoundingBox.Offset(plateView.Plate.PartSpacing));
            }

            // Add thin obstacle boxes from cutoff definitions so that
            // the area selection correctly treats cutoffs as boundaries.
            // Cutoff Parts have inflated bounding boxes (their programs use
            // absolute coordinates, causing BoundingBox to span from origin)
            // so we derive the position directly from the CutOff definition.
            var plateBounds = plateView.Plate.BoundingBox(includeParts: false);

            foreach (var cutoff in plateView.Plate.CutOffs)
            {
                Box cutoffBox;

                if (cutoff.Axis == CutOffAxis.Vertical)
                    cutoffBox = new Box(cutoff.Position.X, plateBounds.Y, 0, plateBounds.Length);
                else
                    cutoffBox = new Box(plateBounds.X, cutoff.Position.Y, plateBounds.Width, 0);

                boxes.Add(cutoffBox.Offset(plateView.Plate.PartSpacing));
            }

            Bounds = plateView.Plate.WorkArea();
        }
    }
}

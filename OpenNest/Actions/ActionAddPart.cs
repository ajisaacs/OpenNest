using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    [DisplayName("Add Parts")]
    public class ActionAddPart : Action
    {
        private LayoutPart part;
        private double lastScale;

        public ActionAddPart(PlateView plateView)
            : this(plateView, null)
        {
        }

        public ActionAddPart(PlateView plateView, Drawing drawing)
            : base(plateView)
        {
            plateView.KeyDown += plateView_KeyDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseDown += plateView_MouseDown;
            plateView.Paint += plateView_Paint;

            part = LayoutPart.Create(new Part(drawing), plateView);
            part.IsSelected = true;

            lastScale = double.NaN;

            plateView.SelectedParts.Clear();
            plateView.SelectedParts.Add(part);
        }

        private void plateView_MouseDown(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    Apply();
                    break;
            }
        }

        private void plateView_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.F1:
                case Keys.Enter:
                    Apply();
                    break;

                case Keys.F:
                    if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                        Fill();
                    break;
            }
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            if (plateView.ViewScale != lastScale)
            {
                part.Update(plateView);
                part.Draw(e.Graphics);
            }
            else
            {
                if (part.IsDirty)
                    part.Update(plateView);

                part.Draw(e.Graphics);
            }

            lastScale = plateView.ViewScale;
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            var offset = plateView.CurrentPoint - part.BoundingBox.Location;
            part.Offset(offset);
            plateView.Invalidate();
        }

        public override void DisconnectEvents()
        {
            plateView.KeyDown -= plateView_KeyDown;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseDown -= plateView_MouseDown;
            plateView.Paint -= plateView_Paint;

            plateView.SelectedParts.Clear();
            plateView.Invalidate();
        }

        public override void CancelAction()
        {
        }

        public override bool IsBusy()
        {
            return false;
        }

        private void Fill()
        {
            var boxes = new List<Box>();

            foreach (var part in plateView.Plate.Parts)
                boxes.Add(part.BoundingBox.Offset(plateView.Plate.PartSpacing));

            var bounds = plateView.Plate.WorkArea();

            var vbox = Helper.GetLargestBoxVertically(plateView.CurrentPoint, bounds, boxes);
            var hbox = Helper.GetLargestBoxHorizontally(plateView.CurrentPoint, bounds, boxes);

            var box = vbox.Area() > hbox.Area() ? vbox : hbox;

            var engine = new NestEngine(plateView.Plate);
            engine.FillArea(box, new NestItem { Drawing = this.part.BasePart.BaseDrawing });
        }

        private void Apply()
        {
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                switch (plateView.Plate.Quadrant)
                {
                    case 1:
                        plateView.PushSelected(PushDirection.Left);
                        plateView.PushSelected(PushDirection.Down);
                        break;

                    case 2:
                        plateView.PushSelected(PushDirection.Right);
                        plateView.PushSelected(PushDirection.Down);
                        break;

                    case 3:
                        plateView.PushSelected(PushDirection.Right);
                        plateView.PushSelected(PushDirection.Up);
                        break;
                    case 4:
                        plateView.PushSelected(PushDirection.Left);
                        plateView.PushSelected(PushDirection.Up);
                        break;
                }
                
            }

            plateView.Plate.Parts.Add(part.BasePart.Clone() as Part);
        }
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    [DisplayName("Clone Parts")]
    public class ActionClone : Action
    {
        private readonly List<LayoutPart> parts;

        private double lastScale;

        public ActionClone(PlateView plateView, Drawing drawing)
            : this(plateView, new List<Part> { new Part(drawing) })
        {
        }

        public ActionClone(PlateView plateView, List<Part> partsToClone)
            : base(plateView)
        {
            plateView.KeyDown += plateView_KeyDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseDown += plateView_MouseDown;
            plateView.Paint += plateView_Paint;

            parts = new List<LayoutPart>();
            lastScale = double.NaN;

            for (int i = 0; i < partsToClone.Count; i++)
            {
                var part = LayoutPart.Create(partsToClone[i].Clone() as Part, plateView);
                part.IsSelected = true;
                parts.Add(part);
            }

            plateView.SelectedParts.Clear();
            plateView.SelectedParts.AddRange(parts);
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
                parts.ForEach(p => 
                {
                    p.Update(plateView);
                    p.Draw(e.Graphics);
                });
            }
            else
            {
                parts.ForEach(p =>
                {
                    if (p.IsDirty)
                        p.Update(plateView);

                    p.Draw(e.Graphics);
                });
            }

            lastScale = plateView.ViewScale;
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            var offset = plateView.CurrentPoint - parts.GetBoundingBox().Location;
            parts.ForEach(p => p.Offset(offset));
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

        public void Apply()
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

            parts.ForEach(p => plateView.Plate.Parts.Add(p.BasePart.Clone() as Part));
        }

        private void Fill()
        {
            var engine = new NestEngine(plateView.Plate);
            var groupParts = parts.Select(p => p.BasePart).ToList();
            engine.Fill(groupParts);
        }
    }
}

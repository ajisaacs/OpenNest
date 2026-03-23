using OpenNest.Controls;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

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

        public override bool SurvivesPlateChange => true;

        public override void OnPlateChanged()
        {
            plateView.SelectedParts.Clear();
            plateView.SelectedParts.AddRange(parts);
        }

        public override void ConnectEvents()
        {
            plateView.KeyDown += plateView_KeyDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseDown += plateView_MouseDown;
            plateView.Paint += plateView_Paint;

            plateView.SelectedParts.Clear();
            plateView.SelectedParts.AddRange(parts);
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
                var movingParts = parts.Select(p => p.BasePart).ToList();

                PushDirection hDir, vDir;
                switch (plateView.Plate.Quadrant)
                {
                    case 1: hDir = PushDirection.Left; vDir = PushDirection.Down; break;
                    case 2: hDir = PushDirection.Right; vDir = PushDirection.Down; break;
                    case 3: hDir = PushDirection.Right; vDir = PushDirection.Up; break;
                    case 4: hDir = PushDirection.Left; vDir = PushDirection.Up; break;
                    default: hDir = PushDirection.Left; vDir = PushDirection.Down; break;
                }

                // Phase 1: BB-only push to get past irregular geometry quickly.
                Compactor.PushBoundingBox(movingParts, plateView.Plate, hDir);
                Compactor.PushBoundingBox(movingParts, plateView.Plate, vDir);

                // Phase 2: Geometry push to settle against actual contours.
                Compactor.Push(movingParts, plateView.Plate, hDir);
                Compactor.Push(movingParts, plateView.Plate, vDir);

                parts.ForEach(p => p.IsDirty = true);
                plateView.Invalidate();
            }

            parts.ForEach(p => plateView.Plate.Parts.Add(p.BasePart.Clone() as Part));

            if (plateView.Plate.CutOffs.Count > 0)
                plateView.Plate.RegenerateCutOffs(plateView.CutOffSettings);
        }

        private void Fill()
        {
            var plate = plateView.Plate;
            var groupParts = parts.Select(p => p.BasePart).ToList();

            var bounds = plate.WorkArea();

            if (plate.Parts.Count == 0)
            {
                plateView.FillWithProgress(groupParts, bounds);
                return;
            }

            var boxes = new List<Box>();
            foreach (var part in plate.Parts)
                boxes.Add(part.BoundingBox.Offset(plate.PartSpacing));

            var pt = plateView.CurrentPoint;
            var vertical = SpatialQuery.GetLargestBoxVertically(pt, bounds, boxes);
            var horizontal = SpatialQuery.GetLargestBoxHorizontally(pt, bounds, boxes);

            var bestArea = vertical;
            if (horizontal.Area() > vertical.Area())
                bestArea = horizontal;

            if (bestArea == Box.Empty)
                return;

            plateView.FillWithProgress(groupParts, bestArea);
        }
    }
}

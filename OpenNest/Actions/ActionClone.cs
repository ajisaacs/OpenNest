using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.CutOffs;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Undo;

namespace OpenNest.Actions
{
    [DisplayName("Clone Parts")]
    public class ActionClone : Action
    {
        private readonly List<LayoutPart> parts;
        private readonly List<Part> sourceParts;
        private readonly List<(int[] Members, double Kerf)> commonLines;

        private double lastScale;
        private System.Drawing.Point? restingCursor;

        public ActionClone(PlateView plateView, Drawing drawing)
            : this(plateView, new List<Part> { new Part(drawing) }) { }

        public ActionClone(PlateView plateView, List<Part> partsToClone)
            : base(plateView)
        {
            plateView.KeyDown += plateView_KeyDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseDown += plateView_MouseDown;
            plateView.Paint += plateView_Paint;
            plateView.GotFocus += plateView_FocusChanged;
            plateView.LostFocus += plateView_FocusChanged;

            sourceParts = partsToClone;
            parts = new List<LayoutPart>();
            lastScale = double.NaN;

            for (int i = 0; i < partsToClone.Count; i++)
                parts.Add(LayoutPart.Create(partsToClone[i].Clone() as Part, plateView));

            commonLines = CopiedCommonLines(plateView.Plate, partsToClone);

            SelectPreviews();
        }

        // The common-line groups among the copied parts, by index into the copy, so each paste
        // joins its clones the same way. Read now: Cut removes the sources, and their groups, next.
        private static List<(int[] Members, double Kerf)> CopiedCommonLines(Plate plate, List<Part> copied)
        {
            var groups = new List<(int[] Members, double Kerf)>();
            if (plate == null)
                return groups;

            foreach (var group in plate.CommonLines)
            {
                var members = Enumerable.Range(0, copied.Count).Where(i => group.Contains(copied[i])).ToArray();
                if (members.Length >= 2)
                    groups.Add((members, group.Kerf));
            }

            return groups;
        }

        // Re-flag on every reselect: a deselect elsewhere would otherwise leave the
        // previews drawn with the solid fill of placed parts.
        private void SelectPreviews()
        {
            parts.ForEach(p => p.IsSelected = true);
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

                case Keys.Delete:
                    DeleteSources();
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
                    p.UpdateView(plateView);
                    p.Draw(e.Graphics);
                });
            }
            else
            {
                parts.ForEach(p =>
                {
                    if (p.IsDirty)
                        p.UpdateView(plateView);

                    p.Draw(e.Graphics);
                });
            }

            lastScale = plateView.ViewScale;
        }

        // Windows posts a mouse move when a dialog opens or closes, though the mouse stayed
        // put. Following it would throw away arrow-key nudges, so only a real move counts.
        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Location == restingCursor)
                return;

            restingCursor = e.Location;
            var offset = plateView.CurrentPoint - parts.GetBoundingBox().Location;
            parts.ForEach(p => p.Offset(offset));
            plateView.Invalidate();
        }

        // The cursor may have been moved to the dialog's OK button, so measure the rest
        // position afresh whenever focus leaves or returns.
        private void plateView_FocusChanged(object sender, System.EventArgs e) =>
            restingCursor = plateView.PointToClient(Cursor.Position);

        public override bool SurvivesPlateChange => true;

        public override void OnPlateChanged() => SelectPreviews();

        public override void ConnectEvents()
        {
            plateView.KeyDown += plateView_KeyDown;
            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseDown += plateView_MouseDown;
            plateView.Paint += plateView_Paint;
            plateView.GotFocus += plateView_FocusChanged;
            plateView.LostFocus += plateView_FocusChanged;

            SelectPreviews();
        }

        public override void DisconnectEvents()
        {
            plateView.KeyDown -= plateView_KeyDown;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseDown -= plateView_MouseDown;
            plateView.Paint -= plateView_Paint;
            plateView.GotFocus -= plateView_FocusChanged;
            plateView.LostFocus -= plateView_FocusChanged;

            plateView.SelectedParts.Clear();
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy()
        {
            return false;
        }

        private void DeleteSources()
        {
            // Select All adds placed parts beside the previews; those go with the sources.
            var selected = plateView.SelectedParts.Except(parts).Select(p => p.BasePart);
            var doomed = sourceParts.Concat(selected).ToList();

            using var change = plateView.BeginChange("Delete");
            if (doomed.Any(plateView.Plate.Parts.Contains))
                change.MarkChanged();
            foreach (var part in doomed)
                plateView.Plate.Parts.Remove(part);

            if (plateView.Plate.CutOffs.Count > 0)
                plateView.Plate.RegenerateCutOffs(plateView.CutOffSettings);

            SelectPreviews();
            plateView.Selection.NotifySelectionChanged();
            plateView.Invalidate();
            change.Complete();
        }

        /// <summary>
        /// Which way Shift+click and Ctrl+click push the copy; null reads the user's Options
        /// setting at each click, so a change there applies without restarting the action.
        /// </summary>
        public SettleOrder? PlacementOrder { get; set; }

        // An unknown stored name falls back to the closest-to-origin search.
        internal static SettleOrder ReadPlacementOrder() =>
            System.Enum.TryParse<SettleOrder>(Properties.Settings.Default.ClonePlacementOrder, out var order)
                && System.Enum.IsDefined(order)
                ? order
                : SettleOrder.ClosestToCorner;

        public void Apply() => Apply(Control.ModifierKeys);

        // Shift settles on the parts' geometry (a box pass first when clear); Ctrl settles on
        // bounding boxes alone, so the copy stops square to its neighbors' boxes.
        internal void Apply(Keys modifiers)
        {
            using var change = plateView.BeginChange(parts.Count == 1 ? "Add Part" : "Add Parts");
            change.MarkChanged();
            var shift = (modifiers & Keys.Shift) == Keys.Shift;
            var boxesOnly = !shift && (modifiers & Keys.Control) == Keys.Control;
            if (shift || boxesOnly)
            {
                var movingParts = parts.Select(p => p.BasePart).ToList();

                PushDirection hDir,
                    vDir;
                switch (plateView.Plate.Quadrant)
                {
                    case 1:
                        hDir = PushDirection.Left;
                        vDir = PushDirection.Down;
                        break;
                    case 2:
                        hDir = PushDirection.Right;
                        vDir = PushDirection.Down;
                        break;
                    case 3:
                        hDir = PushDirection.Right;
                        vDir = PushDirection.Up;
                        break;
                    case 4:
                        hDir = PushDirection.Left;
                        vDir = PushDirection.Up;
                        break;
                    default:
                        hDir = PushDirection.Left;
                        vDir = PushDirection.Down;
                        break;
                }

                Compactor.SettlePlacement(movingParts, plateView.Plate, hDir, vDir,
                    order: PlacementOrder ?? ReadPlacementOrder(), boundingBoxesOnly: boxesOnly);

                parts.ForEach(p => p.IsDirty = true);
                plateView.Invalidate();
            }

            var placed = parts.Select(p => p.BasePart.Clone() as Part).ToList();
            placed.ForEach(plateView.Plate.Parts.Add);
            foreach (var (members, kerf) in commonLines)
                plateView.Plate.JoinCommonLine(members.Select(i => placed[i]), kerf);

            if (plateView.Plate.CutOffs.Count > 0)
                plateView.Plate.RegenerateCutOffs(plateView.CutOffSettings);
            change.Complete();
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
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                boxes.Add(part.BoundingBox.Offset(plate.PartSpacing));
            }

            var plateBounds = plate.BoundingBox(includeParts: false);
            foreach (var cutoff in plate.CutOffs)
            {
                Box cutoffBox;

                if (cutoff.Axis == CutOffAxis.Vertical)
                    cutoffBox = new Box(cutoff.Position.X, plateBounds.Y, 0, plateBounds.Width);
                else
                    cutoffBox = new Box(plateBounds.X, cutoff.Position.Y, plateBounds.Length, 0);

                boxes.Add(cutoffBox.Offset(plate.PartSpacing));
            }

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Collections;
using Action = OpenNest.Actions.Action;
using Timer = System.Timers.Timer;

namespace OpenNest.Controls
{
    public class PlateView : DrawControl
    {
        private readonly Font programIdFont;
        private readonly Timer redrawTimer;

        private string status;
        private Plate plate;
        private Action currentAction;
        private List<LayoutPart> parts;

        public List<LayoutPart> SelectedParts;
        public ReadOnlyCollection<LayoutPart> Parts;
        
        public event EventHandler<PartAddedEventArgs> PartAdded;
        public event EventHandler<PartRemovedEventArgs> PartRemoved;
        public event EventHandler StatusChanged;

        public PlateView()
            : this(ColorScheme.Default)
        {
        }

        public PlateView(ColorScheme colorScheme)
        {
            Plate = new Plate(60, 120);
            programIdFont = new Font(DefaultFont, FontStyle.Bold | FontStyle.Underline);
            origin = new PointF();
            parts = new List<LayoutPart>();
            Parts = new ReadOnlyCollection<LayoutPart>(parts);
            SelectedParts = new List<LayoutPart>();

            redrawTimer = new Timer()
            {
                AutoReset = false,
                Enabled = true,
                Interval = 50
            };
            redrawTimer.Elapsed += redrawTimer_Elapsed;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);

            ViewScale = 1.0f;
            RotateIncrementAngle = 10;
            OffsetIncrementDistance = 10;
            ColorScheme = colorScheme;
            BackColor = colorScheme.BackgroundColor;
            Cursor = Cursors.Cross;
            AllowPan = true;
            AllowSelect = true;
            AllowZoom = true;
            AllowDrop = true;
            DrawOrigin = true;
            DrawRapid = false;
            DrawBounds = true;
            FillParts = true;
            SetAction(typeof(ActionSelect));

            UpdateMatrix();
        }

        public ColorScheme ColorScheme { get; set; }

        public bool AllowZoom { get; set; }

        public bool AllowSelect { get; set; }

        public bool AllowPan { get; set; }

        public bool DrawOrigin { get; set; }

        public bool DrawRapid { get; set; }

        public bool DrawBounds { get; set; }

        public bool FillParts { get; set; }

        public double RotateIncrementAngle { get; set; }

        public double OffsetIncrementDistance { get; set; }

        public Plate Plate
        {
            get { return plate; }
            set { SetPlate(value); }
        }

        private void SetPlate(Plate p)
        {
            if (plate != null)
            {
                plate.PartAdded -= plate_PartAdded;
                plate.PartRemoved -= plate_PartRemoved;
                parts.Clear();
                SelectedParts.Clear();
            }

            plate = p;
            plate.PartAdded += plate_PartAdded;
            plate.PartRemoved += plate_PartRemoved;

            foreach (var part in plate.Parts)
                parts.Add(LayoutPart.Create(part, this));

            SetAction(typeof(ActionSelect));
        }

        public string Status
        {
            get { return status; }
            protected set
            {
                status = value;

                if (StatusChanged != null)
                    StatusChanged.Invoke(this, new EventArgs());
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Focused) Focus();
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            if (drgevent.Data.GetData(typeof(Drawing)) != null)
                drgevent.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            var dwg = drgevent.Data.GetData(typeof(Drawing)) as Drawing;

            if (dwg == null)
                return;

            var pt1 = PointToClient(new Point(drgevent.X, drgevent.Y));
            var pt2 = PointControlToWorld(pt1);

            AddPartFromDrawing(dwg, pt2);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var multiplier = Math.Abs(e.Delta / 120);

            if (SelectedParts.Count > 0 && ((ModifierKeys & Keys.Shift) == Keys.Shift))
            {
                var increment = (ModifierKeys & Keys.Control) == Keys.Control
                    ? RotateIncrementAngle * 0.1
                    : RotateIncrementAngle;

                var angle = Angle.ToRadians((e.Delta > 0 ? -increment : increment) * multiplier);

                RotateSelectedParts(angle);
            }
            else
            {
                if (AllowZoom)
                {
                    if (e.Delta > 0)
                        ZoomToControlPoint(e.Location, (float)Math.Pow(ZoomInFactor, multiplier));
                    else
                        ZoomToControlPoint(e.Location, (float)Math.Pow(ZoomOutFactor, multiplier));
                }
            }

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                if (AllowPan)
                {
                    var diffx = e.X - lastPoint.X;
                    var diffy = e.Y - lastPoint.Y;

                    origin.X += diffx;
                    origin.Y += diffy;

                    Invalidate();
                }
            }
            else
            {
                LastPoint = CurrentPoint;
                CurrentPoint = PointControlToWorld(e.Location);
            }

            lastPoint = e.Location;

            base.OnMouseMove(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button == MouseButtons.Middle)
                ZoomToFit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    RemoveSelectedParts();
                    break;

                default:
                    base.OnKeyDown(e);
                    break;
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Only handle TAB, RETURN, ESC, and ARROW KEYS here.
            // All other keys can be handled in OnKeyDown method.

            switch (keyData)
            {
                case Keys.Escape:
                    if (currentAction.IsBusy())
                        currentAction.CancelAction();
                    else
                        SetAction(typeof(ActionSelect));
                    break;

                case Keys.Left:
                    SelectedParts.ForEach(part => part.Offset(-OffsetIncrementDistance, 0));
                    Invalidate();
                    break;

                case Keys.X:
                case Keys.Shift | Keys.Left:
                    PushSelected(PushDirection.Left);
                    break;

                case Keys.Shift | Keys.X:
                case Keys.Shift | Keys.Right:
                    PushSelected(PushDirection.Right);
                    break;

                case Keys.Shift | Keys.Y:
                case Keys.Shift | Keys.Up:
                    PushSelected(PushDirection.Up);
                    break;

                case Keys.Y:
                case Keys.Shift | Keys.Down:
                    PushSelected(PushDirection.Down);
                    break;

                case Keys.Right:
                    SelectedParts.ForEach(part => part.Offset(OffsetIncrementDistance, 0));
                    Invalidate();
                    break;

                case Keys.Up:
                    SelectedParts.ForEach(part => part.Offset(0, OffsetIncrementDistance));
                    Invalidate();
                    break;

                case Keys.Down:
                    SelectedParts.ForEach(part => part.Offset(0, -OffsetIncrementDistance));
                    Invalidate();
                    break;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighSpeed;

            if (DrawOrigin)
            {
                e.Graphics.DrawLine(ColorScheme.OriginPen, origin.X, 0, origin.X, Height);
                e.Graphics.DrawLine(ColorScheme.OriginPen, 0, origin.Y, Width, origin.Y);
            }
            
            e.Graphics.TranslateTransform(origin.X, origin.Y);

            DrawPlate(e.Graphics);
            DrawParts(e.Graphics);

            base.OnPaint(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);

            if (currentAction != null)
            {
                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }
        }

        public override void Refresh()
        {
            parts.ForEach(p => p.Update(this));
            Invalidate();
        }

        private void DrawPlate(Graphics g)
        {
            var plateRect = new RectangleF
            {
                Width = LengthWorldToGui(Plate.Size.Width),
                Height = LengthWorldToGui(Plate.Size.Height)
            };

            var edgeSpacingRect = new RectangleF
            {
                Width = LengthWorldToGui(Plate.Size.Width - Plate.EdgeSpacing.Left - Plate.EdgeSpacing.Right),
                Height = LengthWorldToGui(Plate.Size.Height - Plate.EdgeSpacing.Top - Plate.EdgeSpacing.Bottom)
            };

            switch (Plate.Quadrant)
            {
                case 1:
                    plateRect.Location = PointWorldToGraph(0, 0);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left,
                        Plate.EdgeSpacing.Bottom);
                    break;

                case 2:
                    plateRect.Location = PointWorldToGraph(-Plate.Size.Width, 0);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left - Plate.Size.Width,
                        Plate.EdgeSpacing.Bottom);
                    break;

                case 3:
                    plateRect.Location = PointWorldToGraph(-Plate.Size.Width, -Plate.Size.Height);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left - Plate.Size.Width,
                        Plate.EdgeSpacing.Bottom - Plate.Size.Height);
                    break;

                case 4:
                    plateRect.Location = PointWorldToGraph(0, -Plate.Size.Height);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left,
                        Plate.EdgeSpacing.Bottom - Plate.Size.Height);
                    break;

                default:
                    return;
            }

            plateRect.Y -= plateRect.Height;
            edgeSpacingRect.Y -= edgeSpacingRect.Height;

            g.FillRectangle(ColorScheme.LayoutFillBrush, plateRect);

            var viewBounds = new RectangleF(-origin.X, -origin.Y, Width, Height);

            if (!edgeSpacingRect.Contains(viewBounds))
            {
                g.DrawRectangle(ColorScheme.EdgeSpacingPen,
                   edgeSpacingRect.X,
                   edgeSpacingRect.Y,
                   edgeSpacingRect.Width,
                   edgeSpacingRect.Height);
            }

            g.DrawRectangle(ColorScheme.LayoutOutlinePen,
                plateRect.X,
                plateRect.Y,
                plateRect.Width,
                plateRect.Height);
        }

        private void DrawParts(Graphics g)
        {
            var viewBounds = new RectangleF(-origin.X, -origin.Y, Width, Height);

            for (int i = 0; i < parts.Count; ++i)
            {
                var part = parts[i];

                if (part.IsDirty)
                    part.Update(this);

                var path = part.Path;
                var pathBounds = path.GetBounds();

                if (!pathBounds.IntersectsWith(viewBounds))
                    continue;

                part.Draw(g, (i + 1).ToString());
            }

            if (DrawBounds)
            {
                var bounds = SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox();
                DrawBox(g, bounds);
            }

            if (DrawRapid)
                DrawRapids(g);
        }

        private void DrawRapids(Graphics g)
        {
            var pos = new Vector(0, 0);

            for (int i = 0; i < Plate.Parts.Count; ++i)
            {
                var part = Plate.Parts[i];
                var pgm = part.Program;

                DrawLine(g, pos, part.Location, ColorScheme.RapidPen);
                pos = part.Location;
                DrawRapids(g, pgm, ref pos);
            }
        }

        private void DrawRapids(Graphics g, Program pgm, ref Vector pos)
        {
            for (int i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;
                    var program = subpgm.Program;

                    if (program != null)
                        DrawRapids(g, program, ref pos);
                }
                else
                {
                    var motion = code as Motion;

                    if (motion != null)
                    {
                        if (pgm.Mode == Mode.Incremental)
                        {
                            var endpt = motion.EndPoint + pos;

                            if (code.Type == CodeType.RapidMove)
                                DrawLine(g, pos, endpt, ColorScheme.RapidPen);
                            pos = endpt;
                        }
                        else
                        {
                            if (code.Type == CodeType.RapidMove)
                                DrawLine(g, pos, motion.EndPoint, ColorScheme.RapidPen);
                            pos = motion.EndPoint;
                        }
                    }
                }
            }
        }

        private void DrawLine(Graphics g, Vector pt1, Vector pt2, Pen pen)
        {
            var point1 = PointWorldToGraph(pt1);
            var point2 = PointWorldToGraph(pt2);

            g.DrawLine(pen, point1, point2);
        }

        private void DrawBox(Graphics g, Box box)
        {
            var rect = new RectangleF
            {
                Location = PointWorldToGraph(box.Location),
                Width = LengthWorldToGui(box.Width),
                Height = LengthWorldToGui(box.Height)
            };

            g.DrawRectangle(ColorScheme.BoundingBoxPen, rect.X, rect.Y - rect.Height, rect.Width, rect.Height);
        }

        public LayoutPart GetPartAtControlPoint(Point pt)
        {
            var pt2 = PointControlToGraph(pt);
            return GetPartAtGraphPoint(pt2);
        }

        public LayoutPart GetPartAtGraphPoint(PointF pt)
        {
            for (int i = parts.Count - 1; i >= 0; --i)
            {
                if (parts[i].Path.IsVisible(pt))
                    return parts[i];
            }

            return null;
        }

        public LayoutPart GetPartAtPoint(Vector pt)
        {
            var pt2 = PointWorldToGraph(pt);
            return GetPartAtGraphPoint(pt2);
        }

        public IList<LayoutPart> GetPartsFromWindow(RectangleF rect, SelectionType selectionType)
        {
            var list = new List<LayoutPart>();

            if (selectionType == SelectionType.Intersect)
            {
                for (int i = 0; i < parts.Count; ++i)
                {
                    var part = parts[i];
                    var path = part.Path;
                    var region = new Region(path);

                    if (region.IsVisible(rect))
                        list.Add(part);

                    region.Dispose();
                }
            }
            else
            {
                for (int i = 0; i < parts.Count; ++i)
                {
                    var part = parts[i];
                    var path = part.Path;
                    var bounds = path.GetBounds();

                    if (rect.Contains(bounds))
                        list.Add(part);
                }
            }

            return list;
        }

        public void SetAction(Type type)
        {
            var action = Activator.CreateInstance(type, this) as Action;

            if (action == null)
                return;

            if (currentAction != null)
            {
                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }

            currentAction = action;

            Status = GetDisplayName(type);
        }

        public void SetAction(Type type, params object[] args)
        {
            if (currentAction != null)
            {
                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }

            Array.Resize(ref args, args.Length + 1);

            // shift all elements to the right
            for (int i = args.Length - 2; i >= 0; i--)
                args[i + 1] = args[i];

            // set the first argument to this.
            args[0] = this;

            var action = Activator.CreateInstance(type, args) as Action;

            if (action == null)
                return;

            currentAction = action;

            Status = GetDisplayName(type);
        }

        public void AlignSelected(AlignType alignType)
        {
            if (SelectedParts.Count == 0)
                return;

            AlignSelected(alignType, SelectedParts[0]);
        }

        public void AlignSelected(AlignType alignType, LayoutPart fixedPart)
        {
            switch (alignType)
            {
                case AlignType.Bottom:
                    Align.Bottom(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.Horizontally:
                    Align.Horizontally(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.Left:
                    Align.Left(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.Right:
                    Align.Right(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.Top:
                    Align.Top(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.Vertically:
                    Align.Vertically(fixedPart.BasePart, SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.EvenlySpaceHorizontally:
                    Align.EvenlyDistributeHorizontally(SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                case AlignType.EvenlySpaceVertically:
                    Align.EvenlyDistributeVertically(SelectedParts.Select(p => p.BasePart).ToList());
                    break;

                default:
                    return;
            }

            SelectedParts.ForEach(p => p.IsDirty = true);
            Invalidate();
        }

        public void AddPartFromDrawing(Drawing dwg, Vector location)
        {
            var part = new Part(dwg, location);

            part.Offset(
                part.Location.X - part.BoundingBox.Center.X,
                part.Location.Y - part.BoundingBox.Center.Y);

            Plate.Parts.Add(part);
        }

        public void RemoveSelectedParts()
        {
            foreach (var part in SelectedParts)
                Plate.Parts.Remove(part.BasePart);

            DeselectAll();
            Invalidate();
        }

        private void redrawTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Invalidate();
        }

        private void plate_PartAdded(object sender, PartAddedEventArgs e)
        {
            if (PartAdded != null)
                PartAdded.Invoke(this, e);

            parts.Insert(e.Index, LayoutPart.Create(e.Part, this));
            redrawTimer.Start();
        }

        private void plate_PartRemoved(object sender, PartRemovedEventArgs e)
        {
            if (PartRemoved != null)
                PartRemoved.Invoke(this, e);

            parts.RemoveAll(p => p.BasePart == e.Part);
        }

        public void DeselectAll()
        {
            SelectedParts.ForEach(p => p.IsSelected = false);
            SelectedParts.Clear();
        }

        public void SelectAll()
        {
            parts.ForEach(p => p.IsSelected = true);
            SelectedParts.AddRange(parts);
        }

        public override void ZoomToPoint(Vector pt, float zoomFactor, bool redraw = true)
        {
            base.ZoomToPoint(pt, zoomFactor, false);

            if (redraw) 
                Invalidate();
        }

        public override void ZoomToArea(double x, double y, double width, double height, bool redraw = true)
        {
            base.ZoomToArea(x, y, width, height, false);

            if (redraw)
                Invalidate();
        }

        public virtual void ZoomToFit(bool redraw = true)
        {
            ZoomToArea(plate.BoundingBox(true), redraw);
        }

        public virtual void ZoomToSelected(bool redraw = true)
        {
            ZoomToArea(SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox(), redraw);
        }

        public virtual void ZoomToPlate(bool redraw = true)
        {
            ZoomToArea(plate.BoundingBox(false), redraw);
        }

        public void PushSelected(PushDirection direction)
        {
            var boxes = new List<Box>();

            foreach (var part in parts.Where(p => !p.IsSelected).ToList())
            {
                if (!SelectedParts.Contains(part))
                    boxes.Add(part.BoundingBox);
            }

            var workArea = Plate.WorkArea();
            var distance = double.MaxValue;
            var offset = new Vector();

            switch (direction)
            {
                case PushDirection.Down:
                    SelectedParts.ForEach(part =>
                    {
                        var d1 = Helper.ClosestDistanceDown(part.BoundingBox.Offset(Plate.PartSpacing), boxes);

                        if (d1 < distance)
                            distance = d1;

                        var d2 = part.Bottom - workArea.Bottom;

                        if (d2 > 0 && d2 < distance)
                            distance = d2;
                    });
                    offset.Y = -distance;
                    break;

                case PushDirection.Left:
                    SelectedParts.ForEach(part =>
                    {
                        var d1 = Helper.ClosestDistanceLeft(part.BoundingBox.Offset(Plate.PartSpacing), boxes);

                        if (d1 < distance)
                            distance = d1;

                        var d2 = part.Left - workArea.Left;

                        if (d2 > 0 && d2 < distance)
                            distance = d2;
                    });
                    offset.X = -distance;
                    break;

                case PushDirection.Right:
                    SelectedParts.ForEach(part =>
                    {
                        var d1 = Helper.ClosestDistanceRight(part.BoundingBox.Offset(Plate.PartSpacing), boxes);

                        if (d1 < distance)
                            distance = d1;

                        var d2 = workArea.Right - part.Right;

                        if (d2 > 0 && d2 < distance)
                            distance = d2;
                    });
                    offset.X = distance;
                    break;

                case PushDirection.Up:
                    SelectedParts.ForEach(part =>
                    {
                        var d1 = Helper.ClosestDistanceUp(part.BoundingBox.Offset(Plate.PartSpacing), boxes);

                        if (d1 < distance)
                            distance = d1;

                        var d2 = workArea.Top - part.Top;

                        if (d2 > 0 && d2 < distance)
                            distance = d2;
                    });
                    offset.Y = distance;
                    break;
            }

            if (distance < double.MaxValue)
            {
                SelectedParts.ForEach(p => p.Offset(offset));
                Invalidate();
            }
        }

        private string GetDisplayName(Type type)
        {
            var attributes = type.GetCustomAttributes(true);

            foreach (var attr in attributes)
            {
                var displayNameAttr = attr as DisplayNameAttribute;

                if (displayNameAttr != null)
                    return displayNameAttr.DisplayName;
            }

            return type.Name;
        }

        public void RotateSelectedParts(double angle)
        {
            var pt1 = SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox().Location;

            for (int i = 0; i < SelectedParts.Count; ++i)
            {
                var part = SelectedParts[i];
                part.Rotate(angle);
            }

            var pt2 = SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox().Location;
            var diff = pt1 - pt2;

            for (int i = 0; i < SelectedParts.Count; ++i)
            {
                var part = SelectedParts[i];
                part.Offset(diff);
            }
        }

        protected override void UpdateMatrix()
        {
            base.UpdateMatrix();
            parts.ForEach(p => p.Update(this));
        }
    }
}

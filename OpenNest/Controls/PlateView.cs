using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Collections;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Math;
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
        private Action previousAction;
        private List<LayoutPart> parts;
        private List<LayoutPart> temporaryParts = new List<LayoutPart>();
        private Point middleMouseDownPoint;
        private Box activeWorkArea;
        private List<Box> debugRemnants;

        public Box ActiveWorkArea
        {
            get => activeWorkArea;
            set
            {
                activeWorkArea = value;
                Invalidate();
            }
        }

        public List<Box> DebugRemnants
        {
            get => debugRemnants;
            set
            {
                debugRemnants = value;
                Invalidate();
            }
        }

        public List<int> DebugRemnantPriorities { get; set; }

        public List<LayoutPart> SelectedParts;
        public ReadOnlyCollection<LayoutPart> Parts;
        
        public event EventHandler<ItemAddedEventArgs<Part>> PartAdded;
        public event EventHandler<ItemRemovedEventArgs<Part>> PartRemoved;
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
            DrawOffset = false;
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

        public bool DrawOffset { get; set; }

        public double OffsetTolerance { get; set; } = 0.001;

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
                temporaryParts.Clear();
                SelectedParts.Clear();
            }

            plate = p;
            plate.PartAdded += plate_PartAdded;
            plate.PartRemoved += plate_PartRemoved;

            foreach (var part in plate.Parts)
                parts.Add(LayoutPart.Create(part, this));

            if (currentAction == null || !currentAction.SurvivesPlateChange)
                SetAction(typeof(ActionSelect));
            else
                currentAction.OnPlateChanged();
        }

        public string Status
        {
            get { return status; }
            set
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
                middleMouseDownPoint = e.Location;

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle && SelectedParts.Count > 0)
            {
                var dx = e.X - middleMouseDownPoint.X;
                var dy = e.Y - middleMouseDownPoint.Y;

                if (dx * dx + dy * dy < 25)
                {
                    RotateSelectedParts(Angle.ToRadians(90));
                    Invalidate();
                }
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var multiplier = System.Math.Abs(e.Delta / 120);

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
                        ZoomToControlPoint(e.Location, (float)System.Math.Pow(ZoomInFactor, multiplier));
                    else
                        ZoomToControlPoint(e.Location, (float)System.Math.Pow(ZoomOutFactor, multiplier));
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

            if (e.Button == MouseButtons.Middle && SelectedParts.Count == 0)
                ZoomToFit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    RemoveSelectedParts();
                    break;

                case Keys.F:
                    if ((ModifierKeys & Keys.Control) == 0)
                        ZoomToFit();
                    else
                        base.OnKeyDown(e);
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
                    else if (currentAction is ActionSelect && previousAction != null)
                        RestorePreviousAction();
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
            DrawActiveWorkArea(e.Graphics);
            DrawDebugRemnants(e.Graphics);

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
            temporaryParts.ForEach(p => p.Update(this));
            Invalidate();
        }

        protected void DrawPlate(Graphics g)
        {
            var plateRect = new RectangleF
            {
                Width = LengthWorldToGui(Plate.Size.Length),
                Height = LengthWorldToGui(Plate.Size.Width)
            };

            var edgeSpacingRect = new RectangleF
            {
                Width = LengthWorldToGui(Plate.Size.Length - Plate.EdgeSpacing.Left - Plate.EdgeSpacing.Right),
                Height = LengthWorldToGui(Plate.Size.Width - Plate.EdgeSpacing.Top - Plate.EdgeSpacing.Bottom)
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
                    plateRect.Location = PointWorldToGraph(-Plate.Size.Length, 0);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left - Plate.Size.Length,
                        Plate.EdgeSpacing.Bottom);
                    break;

                case 3:
                    plateRect.Location = PointWorldToGraph(-Plate.Size.Length, -Plate.Size.Width);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left - Plate.Size.Length,
                        Plate.EdgeSpacing.Bottom - Plate.Size.Width);
                    break;

                case 4:
                    plateRect.Location = PointWorldToGraph(0, -Plate.Size.Width);
                    edgeSpacingRect.Location = PointWorldToGraph(
                        Plate.EdgeSpacing.Left,
                        Plate.EdgeSpacing.Bottom - Plate.Size.Width);
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

        protected void DrawParts(Graphics g)
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

            // Draw temporary (preview) parts
            for (var i = 0; i < temporaryParts.Count; i++)
            {
                var temp = temporaryParts[i];

                if (temp.IsDirty)
                    temp.Update(this);

                var path = temp.Path;
                var pathBounds = path.GetBounds();

                if (!pathBounds.IntersectsWith(viewBounds))
                    continue;

                g.FillPath(ColorScheme.PreviewPartBrush, path);
                g.DrawPath(ColorScheme.PreviewPartPen, path);
            }

            if (DrawOffset && Plate.PartSpacing > 0)
                DrawOffsetGeometry(g);

            if (DrawBounds)
            {
                var bounds = SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox();
                DrawBox(g, bounds);
            }

            if (DrawRapid)
                DrawRapids(g);
        }

        private void DrawOffsetGeometry(Graphics g)
        {
            using (var offsetPen = new Pen(Color.FromArgb(120, 255, 100, 100)))
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    var layoutPart = parts[i];

                    if (layoutPart.IsDirty)
                        layoutPart.Update(this);

                    layoutPart.UpdateOffset(Plate.PartSpacing, OffsetTolerance, Matrix);

                    if (layoutPart.OffsetPath != null)
                        g.DrawPath(offsetPen, layoutPart.OffsetPath);
                }
            }
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
                Height = LengthWorldToGui(box.Length)
            };

            g.DrawRectangle(ColorScheme.BoundingBoxPen, rect.X, rect.Y - rect.Height, rect.Width, rect.Height);
        }

        private void DrawActiveWorkArea(Graphics g)
        {
            if (activeWorkArea == null)
                return;

            var rect = new RectangleF
            {
                Location = PointWorldToGraph(activeWorkArea.Location),
                Width = LengthWorldToGui(activeWorkArea.Width),
                Height = LengthWorldToGui(activeWorkArea.Length)
            };
            rect.Y -= rect.Height;

            using var pen = new Pen(Color.Red, 1.5f)
            {
                DashStyle = DashStyle.Dash
            };
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        // Priority 0 = green (preferred), 1 = yellow (extend), 2 = red (last resort)
        private static readonly Color[] PriorityFills =
        {
            Color.FromArgb(60, Color.LimeGreen),
            Color.FromArgb(60, Color.Gold),
            Color.FromArgb(60, Color.Salmon),
        };

        private static readonly Color[] PriorityBorders =
        {
            Color.FromArgb(180, Color.Green),
            Color.FromArgb(180, Color.DarkGoldenrod),
            Color.FromArgb(180, Color.DarkRed),
        };

        private void DrawDebugRemnants(Graphics g)
        {
            if (debugRemnants == null || debugRemnants.Count == 0)
                return;

            for (var i = 0; i < debugRemnants.Count; i++)
            {
                var box = debugRemnants[i];
                var loc = PointWorldToGraph(box.Location);
                var w = LengthWorldToGui(box.Width);
                var h = LengthWorldToGui(box.Length);
                var rect = new RectangleF(loc.X, loc.Y - h, w, h);

                var priority = DebugRemnantPriorities != null && i < DebugRemnantPriorities.Count
                    ? System.Math.Min(DebugRemnantPriorities[i], 2)
                    : 0;

                using var brush = new SolidBrush(PriorityFills[priority]);
                g.FillRectangle(brush, rect);

                using var pen = new Pen(PriorityBorders[priority], 1.5f);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                var label = $"P{priority} {box.Width:F1}x{box.Length:F1}";
                using var font = new Font("Segoe UI", 8f);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, Brushes.Black, rect, sf);
            }
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
                if (type == typeof(ActionSelect) && !(currentAction is ActionSelect))
                    previousAction = currentAction;
                else
                    previousAction = null;

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
                previousAction = null;
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

        private void RestorePreviousAction()
        {
            var action = previousAction;
            previousAction = null;

            currentAction.CancelAction();
            currentAction.DisconnectEvents();

            action.ConnectEvents();
            currentAction = action;

            Status = GetDisplayName(action.GetType());
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

        public void SetTemporaryParts(List<Part> parts)
        {
            temporaryParts.Clear();

            if (parts != null)
            {
                foreach (var part in parts)
                    temporaryParts.Add(LayoutPart.Create(part, this));
            }

            Invalidate();
        }

        public void ClearTemporaryParts()
        {
            temporaryParts.Clear();
            Invalidate();
        }

        public int AcceptTemporaryParts()
        {
            var count = temporaryParts.Count;

            foreach (var layoutPart in temporaryParts)
                Plate.Parts.Add(layoutPart.BasePart);

            temporaryParts.Clear();
            return count;
        }

        public async void FillWithProgress(List<Part> groupParts, Box workArea)
        {
            var sw = Stopwatch.StartNew();
            var cts = new CancellationTokenSource();
            var progressForm = new NestProgressForm(cts, showPlateRow: false);

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);
                SetTemporaryParts(p.BestParts);
                ActiveWorkArea = p.ActiveWorkArea;
            });

            progressForm.Show(FindForm());

            try
            {
                var engine = NestEngineRegistry.Create(Plate);
                var parts = await Task.Run(() =>
                    engine.Fill(groupParts, workArea, progress, cts.Token));

                if (parts.Count > 0 && !cts.IsCancellationRequested)
                {
                    AcceptTemporaryParts();
                    sw.Stop();
                    Status = $"Fill: {parts.Count} parts in {sw.ElapsedMilliseconds} ms";
                }
                else
                {
                    ClearTemporaryParts();
                }

                progressForm.ShowCompleted();
            }
            catch (Exception)
            {
                ClearTemporaryParts();
            }
            finally
            {
                ActiveWorkArea = null;
                progressForm.Close();
                cts.Dispose();
            }
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

        private void plate_PartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            if (PartAdded != null)
                PartAdded.Invoke(this, e);

            parts.Insert(e.Index, LayoutPart.Create(e.Item, this));
            redrawTimer.Start();
        }

        private void plate_PartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            if (PartRemoved != null)
                PartRemoved.Invoke(this, e);

            parts.RemoveAll(p => p.BasePart == e.Item);
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
            var movingParts = SelectedParts.Select(p => p.BasePart).ToList();
            Compactor.Push(movingParts, Plate, direction);
            SelectedParts.ForEach(p => p.IsDirty = true);
            Invalidate();
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
            var parts = SelectedParts.Select(p => p.BasePart).ToList();
            var bounds = parts.GetBoundingBox();
            var center = bounds.Center;
            var anchor = bounds.Location;
            var rotatedPrograms = new HashSet<Program>();

            for (int i = 0; i < SelectedParts.Count; ++i)
            {
                var part = SelectedParts[i];
                var basePart = part.BasePart;

                if (rotatedPrograms.Add(basePart.Program))
                    basePart.Program.Rotate(angle);

                part.Location = part.Location.Rotate(angle, center);
                basePart.UpdateBounds();
            }

            var diff = anchor - parts.GetBoundingBox().Location;

            for (int i = 0; i < SelectedParts.Count; ++i)
                SelectedParts[i].Offset(diff);
        }

        protected override void UpdateMatrix()
        {
            base.UpdateMatrix();
            parts.ForEach(p => p.Update(this));
            temporaryParts.ForEach(p => p.Update(this));
        }
    }
}

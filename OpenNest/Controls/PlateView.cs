using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Collections;
using OpenNest.Engine.Fill;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Math;
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
        private CutOffSettings cutOffSettings = new CutOffSettings();
        private CutOff selectedCutOff;
        private bool draggingCutOff;
        private Dictionary<Part, Geometry.Entity> dragPerimeterCache;
        protected List<LayoutPart> parts;
        private List<LayoutPart> stationaryParts = new List<LayoutPart>();
        private List<LayoutPart> activeParts = new List<LayoutPart>();
        private Point middleMouseDownPoint;
        private Box activeWorkArea;
        private List<Box> debugRemnants;
        private PlateRenderer renderer;

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
        public event EventHandler SelectionChanged;

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
            renderer = new PlateRenderer(this);
            SetAction(typeof(ActionSelect));

            UpdateMatrix();
        }

        public ColorScheme ColorScheme { get; set; }

        public bool AllowZoom { get; set; }

        public bool AllowSelect { get; set; }

        public bool AllowPan { get; set; }

        public bool DrawOrigin { get; set; }

        public bool DrawRapid { get; set; }

        public bool DrawPiercePoints { get; set; }

        public bool DrawBounds { get; set; }

        public bool DrawOffset { get; set; }

        public bool DrawCutDirection { get; set; }

        public bool ShowBendLines { get; set; }

        public double OffsetTolerance { get; set; } = 0.001;

        public bool FillParts { get; set; }

        internal List<LayoutPart> LayoutParts => parts;

        internal IReadOnlyList<LayoutPart> PreviewParts =>
            activeParts.Count > 0 ? activeParts : stationaryParts;

        internal Brush PreviewBrush =>
            activeParts.Count > 0 ? ColorScheme.ActivePreviewPartBrush : ColorScheme.PreviewPartBrush;

        internal Pen PreviewPen =>
            activeParts.Count > 0 ? ColorScheme.ActivePreviewPartPen : ColorScheme.PreviewPartPen;

        internal RectangleF GetViewBounds() =>
            new RectangleF(-origin.X, -origin.Y, Width, Height);

        internal PlateRenderer Renderer => renderer;

        public CutOffSettings CutOffSettings
        {
            get => cutOffSettings;
            set
            {
                cutOffSettings = value;
                Plate?.RegenerateCutOffs(value);
                Invalidate();
            }
        }

        public CutOff SelectedCutOff
        {
            get => selectedCutOff;
            set
            {
                selectedCutOff = value;
                Invalidate();
            }
        }

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
                stationaryParts.Clear();
                activeParts.Clear();
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

            if (e.Button == MouseButtons.Left && currentAction is ActionSelect)
            {
                var hitCutOff = GetCutOffAtPoint(CurrentPoint, 5.0 / ViewScale);
                if (hitCutOff != null)
                {
                    SelectedCutOff = hitCutOff;
                    draggingCutOff = true;
                    dragPerimeterCache = Plate.BuildPerimeterCache(Plate);
                    return;
                }
                else
                {
                    SelectedCutOff = null;
                }
            }

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

            if (draggingCutOff && selectedCutOff != null)
            {
                draggingCutOff = false;
                dragPerimeterCache = null;
                Plate.RegenerateCutOffs(cutOffSettings);
                Invalidate();
                return;
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

            if (draggingCutOff && selectedCutOff != null)
            {
                if (selectedCutOff.Axis == CutOffAxis.Vertical)
                    selectedCutOff.Position = new Vector(CurrentPoint.X, selectedCutOff.Position.Y);
                else
                    selectedCutOff.Position = new Vector(selectedCutOff.Position.X, CurrentPoint.Y);

                selectedCutOff.Regenerate(Plate, cutOffSettings, dragPerimeterCache);
                Invalidate();
                return;
            }

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
                    if (selectedCutOff != null)
                    {
                        Plate.CutOffs.Remove(selectedCutOff);
                        selectedCutOff = null;
                        Plate.RegenerateCutOffs(cutOffSettings);
                        Invalidate();
                    }
                    else
                    {
                        RemoveSelectedParts();
                    }
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

        public void ProcessEscapeKey()
        {
            if (currentAction.IsBusy())
                currentAction.CancelAction();
            else if (currentAction is ActionSelect && previousAction != null)
                RestorePreviousAction();
            else
                SetAction(typeof(ActionSelect));
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Only handle TAB, RETURN, ESC, and ARROW KEYS here.
            // All other keys can be handled in OnKeyDown method.

            switch (keyData)
            {
                case Keys.Escape:
                    ProcessEscapeKey();
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

            renderer.DrawPlate(e.Graphics);
            renderer.DrawParts(e.Graphics);
            renderer.DrawCutOffs(e.Graphics);
            renderer.DrawActiveWorkArea(e.Graphics);
            renderer.DrawDebugRemnants(e.Graphics);

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
            stationaryParts.ForEach(p => p.Update(this));
            activeParts.ForEach(p => p.Update(this));
            Invalidate();
        }

        public CutOff GetCutOffAtPoint(Vector point, double tolerance)
        {
            if (Plate?.CutOffs == null)
                return null;

            foreach (var cutoff in Plate.CutOffs)
            {
                var program = cutoff.Drawing?.Program;
                if (program == null)
                    continue;

                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (program.Codes[i] is RapidMove rapid &&
                        program.Codes[i + 1] is LinearMove linear)
                    {
                        var line = new Geometry.Line(rapid.EndPoint, linear.EndPoint);
                        if (line.ClosestPointTo(point).DistanceTo(point) <= tolerance)
                            return cutoff;
                    }
                }
            }

            return null;
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

        public void SetStationaryParts(List<Part> parts)
        {
            stationaryParts.Clear();
            activeParts.Clear();

            if (parts != null)
            {
                foreach (var part in parts)
                    stationaryParts.Add(LayoutPart.Create(part, this));
            }

            Invalidate();
        }

        public void SetActiveParts(List<Part> parts)
        {
            activeParts.Clear();

            if (parts != null)
            {
                foreach (var part in parts)
                    activeParts.Add(LayoutPart.Create(part, this));
            }

            Invalidate();
        }

        public void ClearPreviewParts()
        {
            stationaryParts.Clear();
            activeParts.Clear();
            Invalidate();
        }

        public void AcceptPreviewParts(List<Part> parts)
        {
            if (parts != null)
            {
                foreach (var part in parts)
                    Plate.Parts.Add(part);
            }

            stationaryParts.Clear();
            activeParts.Clear();
        }

        public async void FillWithProgress(List<Part> groupParts, Box workArea)
        {
            var sw = Stopwatch.StartNew();
            var cts = new CancellationTokenSource();
            var progressForm = new NestProgressForm(cts, showPlateRow: false);

            var previewPlate = new Plate(Plate.Size)
            {
                Quadrant = Plate.Quadrant,
                PartSpacing = Plate.PartSpacing,
            };
            previewPlate.EdgeSpacing = Plate.EdgeSpacing;
            progressForm.PreviewPlate = previewPlate;

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                {
                    progressForm.UpdatePreview(p.BestParts);
                    SetActiveParts(p.BestParts);
                }

                ActiveWorkArea = p.ActiveWorkArea;
            });

            progressForm.Show(FindForm());

            try
            {
                var engine = NestEngineRegistry.Create(Plate);
                var spacing = Plate.PartSpacing;
                var parts = await Task.Run(() =>
                {
                    var result = engine.Fill(groupParts, workArea, progress, cts.Token);
                    Compactor.Settle(result, workArea, spacing);
                    return result;
                });

                if (parts.Count > 0 && (!cts.IsCancellationRequested || progressForm.Accepted))
                {
                    SetActiveParts(parts);
                    AcceptPreviewParts(parts);

                    if (Plate.CutOffs.Count > 0)
                        Plate.RegenerateCutOffs(cutOffSettings);

                    sw.Stop();
                    Status = $"Fill: {parts.Count} parts in {sw.ElapsedMilliseconds} ms";
                }
                else
                {
                    ClearPreviewParts();
                }

                progressForm.ShowCompleted();
            }
            catch (Exception)
            {
                ClearPreviewParts();
            }
            finally
            {
                ActiveWorkArea = null;
                progressForm.Close();
                cts.Dispose();
                Focus();
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
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectAll()
        {
            parts.ForEach(p => p.IsSelected = true);
            SelectedParts.AddRange(parts);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifySelectionChanged()
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
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

            for (var i = 0; i < SelectedParts.Count; ++i)
            {
                var part = SelectedParts[i];
                part.BasePart.Rotate(angle, center);
            }

            var diff = anchor - parts.GetBoundingBox().Location;

            for (var i = 0; i < SelectedParts.Count; ++i)
                SelectedParts[i].Offset(diff);

            if (Plate.CutOffs.Count > 0)
                Plate.RegenerateCutOffs(cutOffSettings);
        }

        protected override void UpdateMatrix()
        {
            base.UpdateMatrix();
            parts.ForEach(p => p.Update(this));
            stationaryParts.ForEach(p => p.Update(this));
            activeParts.ForEach(p => p.Update(this));
        }
    }
}

using OpenNest.Actions;
using OpenNest.Collections;
using OpenNest.Engine.Fill;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace OpenNest.Controls
{
    public class PlateView : DrawControl
    {
        private readonly Timer redrawTimer;

        private string status;
        private Plate plate;
        private ActionManager actionManager;
        private CutOffSettings cutOffSettings = new CutOffSettings();
        private SelectionManager selection;
        private CutOffHandler cutOffHandler;
        private PreviewManager previewManager;
        protected List<LayoutPart> parts;
        private Point middleMouseDownPoint;
        private Box activeWorkArea;
        private List<Box> debugRemnants;
        private PlateRenderer renderer;
        private LayoutPart hoveredPart;
        private Point hoverPoint;

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

        public List<LayoutPart> SelectedParts => selection.SelectedParts;
        public ReadOnlyCollection<LayoutPart> Parts => new ReadOnlyCollection<LayoutPart>(parts);

        internal SelectionManager Selection => selection;
        internal CutOffHandler CutOffs => cutOffHandler;
        internal ActionManager Actions => actionManager;
        internal PreviewManager Previews => previewManager;

        public event EventHandler<ItemAddedEventArgs<Part>> PartAdded;
        public event EventHandler<ItemRemovedEventArgs<Part>> PartRemoved;
        public event EventHandler StatusChanged;

        public event EventHandler SelectionChanged
        {
            add => selection.SelectionChanged += value;
            remove => selection.SelectionChanged -= value;
        }

        public PlateView()
            : this(ColorScheme.Default)
        {
        }

        public PlateView(ColorScheme colorScheme)
        {
            Plate = new Plate(60, 120);
            origin = new PointF();
            parts = new List<LayoutPart>();
            selection = new SelectionManager(this);
            cutOffHandler = new CutOffHandler(this);
            previewManager = new PreviewManager(this);

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
            actionManager = new ActionManager(this);
            actionManager.SetAction(typeof(ActionSelect));

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

        internal IReadOnlyList<LayoutPart> PreviewParts => previewManager.PreviewParts;
        internal Brush PreviewBrush => previewManager.PreviewBrush;
        internal Pen PreviewPen => previewManager.PreviewPen;

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
                previewManager.Clear();
                selection.Clear();
            }

            plate = p;
            plate.PartAdded += plate_PartAdded;
            plate.PartRemoved += plate_PartRemoved;

            foreach (var part in plate.Parts)
                parts.Add(LayoutPart.Create(part, this));

            actionManager.OnPlateChanged();
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

            if (e.Button == MouseButtons.Left && actionManager.CurrentAction is ActionSelect)
            {
                var hitCutOff = cutOffHandler.TryStartDrag(CurrentPoint, 5.0 / ViewScale);
                if (hitCutOff != null)
                {
                    selection.DeselectParts();
                    selection.SelectedCutOffs.Clear();
                    selection.SelectedCutOffs.Add(hitCutOff);
                    Invalidate();
                    return;
                }
                else
                {
                    selection.DeselectCutOffs();
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
                    selection.RotateSelectedParts(Angle.ToRadians(90));
                    Invalidate();
                }
            }

            if (cutOffHandler.IsDragging && selection.SelectedCutOffs.Count > 0)
            {
                cutOffHandler.EndDrag();
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

                selection.RotateSelectedParts(angle);
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

            if (cutOffHandler.IsDragging && selection.SelectedCutOffs.Count > 0)
            {
                cutOffHandler.UpdateDrag(CurrentPoint, selection.SelectedCutOffs[0]);
                return;
            }

            if (e.Button == MouseButtons.None && actionManager.CurrentAction is ActionSelect)
            {
                var graphPt = PointControlToGraph(e.Location);
                LayoutPart hitPart = null;
                for (var i = parts.Count - 1; i >= 0; --i)
                {
                    if (parts[i].Path.IsVisible(graphPt))
                    {
                        hitPart = parts[i];
                        break;
                    }
                }

                if (hitPart != hoveredPart)
                {
                    hoveredPart = hitPart;
                    hoverPoint = e.Location;
                    Invalidate();
                }
            }
            else if (hoveredPart != null)
            {
                hoveredPart = null;
                Invalidate();
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
                    selection.DeleteSelected();
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

        public void ProcessEscapeKey() => actionManager.ProcessEscapeKey();

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
                    selection.PushSelected(PushDirection.Left);
                    break;

                case Keys.Shift | Keys.X:
                case Keys.Shift | Keys.Right:
                    selection.PushSelected(PushDirection.Right);
                    break;

                case Keys.Shift | Keys.Y:
                case Keys.Shift | Keys.Up:
                    selection.PushSelected(PushDirection.Up);
                    break;

                case Keys.Y:
                case Keys.Shift | Keys.Down:
                    selection.PushSelected(PushDirection.Down);
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

            if (hoveredPart != null)
            {
                e.Graphics.ResetTransform();
                var text = hoveredPart.BasePart.BaseDrawing.Name;
                var size = e.Graphics.MeasureString(text, Font);
                var x = hoverPoint.X + 16f;
                var y = hoverPoint.Y - size.Height - 6f;

                if (x + size.Width + 8 > ClientSize.Width)
                    x = hoverPoint.X - size.Width - 8;
                if (y < 0)
                    y = hoverPoint.Y + 20;

                var rect = new RectangleF(x, y, size.Width + 6, size.Height + 4);
                using (var bgBrush = new SolidBrush(Color.FromArgb(230, Color.White)))
                    e.Graphics.FillRectangle(bgBrush, rect);
                e.Graphics.DrawRectangle(Pens.DimGray, rect.X, rect.Y, rect.Width, rect.Height);
                e.Graphics.DrawString(text, Font, Brushes.Black, x + 3, y + 2);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            actionManager.Cleanup();
        }

        public override void Refresh()
        {
            parts.ForEach(p => p.Update(this));
            previewManager.Update();
            Invalidate();
        }

        public CutOff GetCutOffAtPoint(Vector point, double tolerance) => cutOffHandler.GetCutOffAtPoint(point, tolerance);

        public LayoutPart GetPartAtControlPoint(Point pt) => selection.GetPartAtControlPoint(pt);
        public LayoutPart GetPartAtGraphPoint(PointF pt) => selection.GetPartAtGraphPoint(pt);
        public LayoutPart GetPartAtPoint(Vector pt) => selection.GetPartAtPoint(pt);
        public IList<LayoutPart> GetPartsFromWindow(RectangleF rect, SelectionType selectionType) => selection.GetPartsFromWindow(rect, selectionType);

        public void SetAction(Type type) => actionManager.SetAction(type);
        public void SetAction(Type type, params object[] args) => actionManager.SetAction(type, args);

        public void AlignSelected(AlignType alignType) => selection.AlignSelected(alignType);
        public void AlignSelected(AlignType alignType, LayoutPart fixedPart) => selection.AlignSelected(alignType, fixedPart);

        public void AddPartFromDrawing(Drawing dwg, Vector location)
        {
            var part = new Part(dwg, location);

            part.Offset(
                part.Location.X - part.BoundingBox.Center.X,
                part.Location.Y - part.BoundingBox.Center.Y);

            Plate.Parts.Add(part);
        }

        public void SetStationaryParts(List<Part> parts) => previewManager.SetStationaryParts(parts);
        public void SetActiveParts(List<Part> parts) => previewManager.SetActiveParts(parts);
        public void ClearPreviewParts() => previewManager.ClearPreviewParts();
        public void AcceptPreviewParts(List<Part> parts) => previewManager.AcceptPreviewParts(parts);

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

        public void RemoveSelectedParts() => selection.RemoveSelectedParts();


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

        public void DeselectAll() => selection.DeselectAll();
        public void SelectAll() => selection.SelectAll();
        public void NotifySelectionChanged() => selection.NotifySelectionChanged();

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

        public void PushSelected(PushDirection direction) => selection.PushSelected(direction);

        public void RotateSelectedParts(double angle) => selection.RotateSelectedParts(angle);

        protected override void UpdateMatrix()
        {
            base.UpdateMatrix();
            parts.ForEach(p => p.Update(this));
            previewManager.Update();
        }
    }
}

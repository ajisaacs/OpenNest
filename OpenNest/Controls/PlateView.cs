using OpenNest.Actions;
using OpenNest.Bending;
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

        public bool ShowBendLines { get; set; }

        public double OffsetTolerance { get; set; } = 0.001;

        public bool FillParts { get; set; }

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
            DrawCutOffs(e.Graphics);
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
            stationaryParts.ForEach(p => p.Update(this));
            activeParts.ForEach(p => p.Update(this));
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
                DrawBendLines(g, part.BasePart);
                DrawEtchMarks(g, part.BasePart);
                DrawGrainWarning(g, part.BasePart);
            }

            // Draw preview parts — active (current strategy) takes precedence
            // over stationary (overall best) to avoid overlapping fills.
            var previewParts = activeParts.Count > 0 ? activeParts : stationaryParts;
            var previewBrush = activeParts.Count > 0 ? ColorScheme.ActivePreviewPartBrush : ColorScheme.PreviewPartBrush;
            var previewPen = activeParts.Count > 0 ? ColorScheme.ActivePreviewPartPen : ColorScheme.PreviewPartPen;

            for (var i = 0; i < previewParts.Count; i++)
            {
                var part = previewParts[i];

                if (part.IsDirty)
                    part.Update(this);

                var path = part.Path;
                if (!path.GetBounds().IntersectsWith(viewBounds))
                    continue;

                g.FillPath(previewBrush, path);
                g.DrawPath(previewPen, path);
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

            if (DrawPiercePoints)
                DrawAllPiercePoints(g);
        }

        private void DrawBendLines(Graphics g, Part part)
        {
            if (!ShowBendLines || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            using var bendPen = new Pen(Color.Yellow, 1.5f)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
            };

            foreach (var bend in part.BaseDrawing.Bends)
            {
                var start = bend.StartPoint;
                var end = bend.EndPoint;

                // Apply part rotation
                if (part.Rotation != 0)
                {
                    start = start.Rotate(part.Rotation);
                    end = end.Rotate(part.Rotation);
                }

                // Apply part offset
                start = start + part.Location;
                end = end + part.Location;

                var pt1 = PointWorldToGraph(start);
                var pt2 = PointWorldToGraph(end);

                g.DrawLine(bendPen, pt1, pt2);
            }
        }

        private void DrawEtchMarks(Graphics g, Part part)
        {
            if (!ShowBendLines || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            using var etchPen = new Pen(Color.Green, 1.5f);
            var etchLength = 1.0;

            foreach (var bend in part.BaseDrawing.Bends)
            {
                if (bend.Direction != BendDirection.Up)
                    continue;

                var start = bend.StartPoint;
                var end = bend.EndPoint;

                // Apply part rotation
                if (part.Rotation != 0)
                {
                    start = start.Rotate(part.Rotation);
                    end = end.Rotate(part.Rotation);
                }

                // Apply part offset
                start = start + part.Location;
                end = end + part.Location;

                var length = bend.Length;
                var angle = bend.StartPoint.AngleTo(bend.EndPoint) + part.Rotation;

                if (length < etchLength * 3.0)
                {
                    var pt1 = PointWorldToGraph(start);
                    var pt2 = PointWorldToGraph(end);
                    g.DrawLine(etchPen, pt1, pt2);
                }
                else
                {
                    var dx = System.Math.Cos(angle) * etchLength;
                    var dy = System.Math.Sin(angle) * etchLength;

                    var s1 = PointWorldToGraph(start);
                    var e1 = PointWorldToGraph(new Vector(start.X + dx, start.Y + dy));
                    g.DrawLine(etchPen, s1, e1);

                    var s2 = PointWorldToGraph(end);
                    var e2 = PointWorldToGraph(new Vector(end.X - dx, end.Y - dy));
                    g.DrawLine(etchPen, s2, e2);
                }
            }
        }

        private void DrawGrainWarning(Graphics g, Part part)
        {
            if (!ShowBendLines || Plate == null || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            var grainAngle = Plate.GrainAngle;
            var tolerance = Angle.ToRadians(5);

            foreach (var bend in part.BaseDrawing.Bends)
            {
                var bendAngle = bend.LineAngle + part.Rotation;
                bendAngle = bendAngle % System.Math.PI;
                if (bendAngle < 0) bendAngle += System.Math.PI;

                var grainNormalized = grainAngle % System.Math.PI;
                if (grainNormalized < 0) grainNormalized += System.Math.PI;

                var diff = System.Math.Abs(bendAngle - grainNormalized);
                diff = System.Math.Min(diff, System.Math.PI - diff);

                if (diff > tolerance)
                {
                    var box = part.BaseDrawing.Program.BoundingBox();
                    var location = part.Location;
                    var pt1 = PointWorldToGraph(location);
                    var pt2 = PointWorldToGraph(new Vector(
                        location.X + box.Width, location.Y + box.Length));
                    using var warnPen = new Pen(Color.FromArgb(180, 255, 140, 0), 2f);
                    g.DrawRectangle(warnPen, pt1.X, pt2.Y,
                        System.Math.Abs(pt2.X - pt1.X), System.Math.Abs(pt2.Y - pt1.Y));
                    return;
                }
            }
        }

        private void DrawCutOffs(Graphics g)
        {
            if (Plate?.CutOffs == null || Plate.CutOffs.Count == 0)
                return;

            using var pen = new Pen(Color.FromArgb(64, 64, 64), 1.5f);
            using var selectedPen = new Pen(Color.FromArgb(0, 120, 255), 3.5f);

            foreach (var cutoff in Plate.CutOffs)
            {
                var program = cutoff.Drawing?.Program;
                if (program == null || program.Codes.Count == 0)
                    continue;

                var activePen = cutoff == selectedCutOff ? selectedPen : pen;

                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (program.Codes[i] is RapidMove rapid &&
                        program.Codes[i + 1] is LinearMove linear)
                    {
                        DrawLine(g, rapid.EndPoint, linear.EndPoint, activePen);
                    }
                }
            }
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

        private void DrawAllPiercePoints(Graphics g)
        {
            using var brush = new SolidBrush(Color.Red);
            using var pen = new Pen(Color.DarkRed, 1f);

            for (var i = 0; i < Plate.Parts.Count; ++i)
            {
                var part = Plate.Parts[i];
                var pgm = part.Program;
                var pos = part.Location;
                DrawProgramPiercePoints(g, pgm, ref pos, brush, pen);
            }
        }

        private void DrawProgramPiercePoints(Graphics g, Program pgm, ref Vector pos, Brush brush, Pen pen)
        {
            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;
                    if (subpgm.Program != null)
                        DrawProgramPiercePoints(g, subpgm.Program, ref pos, brush, pen);
                }
                else
                {
                    var motion = code as Motion;
                    if (motion == null) continue;

                    var endpt = pgm.Mode == Mode.Incremental
                        ? motion.EndPoint + pos
                        : motion.EndPoint;

                    if (code.Type == CodeType.RapidMove)
                    {
                        var pt = PointWorldToGraph(endpt);
                        var radius = 2f;
                        g.FillEllipse(brush, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
                        g.DrawEllipse(pen, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
                    }

                    pos = endpt;
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

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                    SetStationaryParts(p.BestParts);
                else
                    SetActiveParts(p.BestParts);

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

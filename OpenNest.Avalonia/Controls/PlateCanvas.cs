using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OpenNest.Avalonia.Rendering;
using OpenNest.CNC;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;
using OpenNest.Support;
using SkiaSharp;
using Control = Avalonia.Controls.Control;
using Vector = OpenNest.Geometry.Vector;

namespace OpenNest.Avalonia.Controls
{
    /// <summary>
    /// Draws one plate with Skia as the WinForms plate view does, in the colours of
    /// <see cref="Scheme"/>: the sheet (or irregular outline), edge spacing, filled parts with their
    /// numbers, cut-offs, clamps, common lines and the View toggles in <see cref="Options"/>. Pans
    /// with a left or middle drag, zooms at the cursor with the wheel, fits on a double click, and
    /// highlights the part under the cursor.
    /// </summary>
    public sealed class PlateCanvas : Control
    {
        // Paths are built once per part pose and only read afterwards, so the render thread may draw
        // them while the UI thread builds others.
        private readonly Dictionary<Part, PartPaths> paths = new();
        private readonly Dictionary<Drawing, (CNC.Program Program, double Rotation, Vector Point)> labelPoints = new();
        private readonly ConditionalWeakTable<Part, OffsetEntry> offsets = new();
        private readonly SupportAnalysisCache supports = new();
        private readonly Dictionary<CommonLineGroup, (string State, CommonLineCheck Check)> commonLineChecks = new();
        private readonly ViewTransform view = new();

        private Plate plate;
        private Point? dragStart;
        private bool fitPending = true;

        private sealed record OffsetEntry(CNC.Program Program, double Rotation, double Spacing, double Tolerance,
            SKPath Path);

        public Plate Plate
        {
            get => plate;
            set
            {
                plate = value;
                HoveredPart = null;
                SelectedPart = null;
                fitPending = true;
                TrimCache();
                InvalidateVisual();
            }
        }

        /// <summary>The colours drawn with: the application's active scheme unless set.</summary>
        public ColorScheme Scheme { get; set; } = ColorScheme.Default;

        /// <summary>The View menu toggles this view draws with.</summary>
        public PlateViewOptions Options { get; set; } = new();

        public Part HoveredPart { get; private set; }

        public Part SelectedPart { get; private set; }

        /// <summary>The cursor in plate coordinates, or null when it is outside the view.</summary>
        public Vector? CursorWorld { get; private set; }

        /// <summary>Pixels per drawing unit.</summary>
        public double Zoom => view.Scale;

        /// <summary>The view's world/screen mapping.</summary>
        public ViewTransform View => view;

        public event EventHandler ViewChanged;

        public PlateCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public void ClearCache()
        {
            paths.Clear();
            labelPoints.Clear();
            commonLineChecks.Clear();
        }

        // Keeps only the shown plate's parts, so closed plates do not hold their paths.
        private void TrimCache()
        {
            if (plate == null)
            {
                paths.Clear();
                labelPoints.Clear();
                return;
            }
            var shown = new HashSet<Part>(plate.Parts);
            foreach (var stale in paths.Keys.Where(part => !shown.Contains(part)).ToList())
                paths.Remove(stale);
            var drawings = new HashSet<Drawing>(plate.Parts.Select(part => part.BaseDrawing));
            foreach (var stale in labelPoints.Keys.Where(drawing => !drawings.Contains(drawing)).ToList())
                labelPoints.Remove(stale);
        }

        public void ZoomToFit()
        {
            if (!Fit())
                return;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Fits the view to the plate and its parts without requesting a redraw.</summary>
        private bool Fit()
        {
            if (plate == null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return false;
            view.Resize(Bounds.Width, Bounds.Height);
            if (!view.ZoomToArea(plate.BoundingBox(true)))
                return false;
            fitPending = false;
            return true;
        }

        private Vector ToWorld(Point p) => view.ScreenToWorld(p.X, p.Y);

        private SKMatrix WorldMatrix =>
            new((float)view.Scale, 0, (float)view.OriginX, 0, (float)-view.Scale, (float)view.OriginY, 0, 0, 1);

        private PartPaths PathsFor(Part part)
        {
            if (!paths.TryGetValue(part, out var result) || !result.Geometry.IsCurrentFor(part))
                paths[part] = result = PartPaths.Build(part);
            return result;
        }

        private Vector LabelPoint(Part part)
        {
            if (PartLabels.CutOffSegmentPoint(part) is { } cutOff)
                return cutOff;
            var drawing = part.BaseDrawing;
            if (!labelPoints.TryGetValue(drawing, out var cached)
                || !ReferenceEquals(cached.Program, drawing.Program) || cached.Rotation != drawing.Program.Rotation)
            {
                cached = (drawing.Program, drawing.Program.Rotation, PartLabels.LabelPoint(drawing));
                labelPoints[drawing] = cached;
            }
            return PartLabels.Place(part, cached.Point);
        }

        private SKPath OffsetFor(Part part, double spacing, double tolerance)
        {
            if (offsets.TryGetValue(part, out var entry) && ReferenceEquals(entry.Program, part.Program)
                && entry.Rotation == part.Rotation && entry.Spacing == spacing && entry.Tolerance == tolerance)
                return entry.Path;
            var polygons = PlateOverlays.OffsetOutline(part, spacing, tolerance);
            var path = SkiaFigures.Polygons(polygons.Select(p => (IReadOnlyList<Vector>)p.Vertices));
            offsets.AddOrUpdate(part, new OffsetEntry(part.Program, part.Rotation, spacing, tolerance, path));
            return path;
        }

        public override void Render(DrawingContext context)
        {
            // A redraw may not be requested during a render, so tell listeners afterwards.
            if (fitPending && Fit())
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ViewChanged?.Invoke(this, EventArgs.Empty));
            if (Bounds.Width > 0 && Bounds.Height > 0)
                view.Resize(Bounds.Width, Bounds.Height);

            var scene = new Scene
            {
                Bounds = new Rect(Bounds.Size),
                Palette = SkiaSchemePalette.For(Scheme ?? ColorScheme.Default),
                Matrix = WorldMatrix,
                Scale = view.Scale,
                DrawOrigin = Options.DrawOrigin,
            };
            if (plate != null)
                BuildScene(scene);
            context.Custom(new PlateDrawOperation(scene));
        }

        private void BuildScene(Scene scene)
        {
            var options = Options;
            var visible = view.VisibleWorld();
            scene.Sheet = PlateOverlays.Sheet(plate);
            if (plate.Outline is { } outline)
            {
                scene.SheetOutline = SkiaFigures.Polygons(new[] { PlateOverlays.PlaceOutline(plate, outline.Points) }
                    .Concat(outline.Obstacles.Select(o => PlateOverlays.PlaceOutline(plate, o))));
                scene.Obstacles = outline.Obstacles.Count == 0 ? null
                    : SkiaFigures.Polygons(outline.Obstacles.Select(o => PlateOverlays.PlaceOutline(plate, o)));
                var usable = PlateOverlays.UsableOutline(plate);
                scene.Usable = usable.Count == 0 ? null : SkiaFigures.Polygons(usable);
            }
            else
            {
                var edge = PlateOverlays.EdgeSpacing(plate);
                // As WinForms: the edge-spacing line is left out while it surrounds the whole view.
                if (!Contains(edge, visible))
                    scene.EdgeSpacing = edge;
            }

            var palette = scene.Palette;
            for (var i = 0; i < plate.Parts.Count; i++)
            {
                var part = plate.Parts[i];
                var partPaths = PathsFor(part);
                var bounds = partPaths.StrokeBounds;
                var placed = bounds == null ? null : bounds.Translate(part.Location.X, part.Location.Y);
                if (placed == null || !Intersects(placed, visible))
                    continue;
                var color = ToSkColor(part.BaseDrawing.Color);
                scene.Parts.Add(new PartItem(partPaths, part.Location, placed, color, palette.PartOutline(color),
                    (i + 1).ToString(), LabelPoint(part), ReferenceEquals(part, SelectedPart),
                    ReferenceEquals(part, HoveredPart), part.BaseDrawing.IsCutOff));
            }

            if (options.DrawOffset && plate.PartSpacing > 0)
            {
                foreach (var part in plate.Parts)
                    scene.Offsets.Add((OffsetFor(part, plate.PartSpacing, options.OffsetTolerance), part.Location));
            }
            if (options.DrawBounds && SelectedPart != null)
                scene.SelectionBounds = PlateOverlays.SelectionBounds(new[] { SelectedPart });
            if (options.DrawRapid)
                scene.Rapids = PlateOverlays.Rapids(plate);
            if (options.DrawPiercePoints)
                scene.PiercePoints = PlateOverlays.PiercePoints(plate);
            if (options.DrawCutDirection)
                scene.Arrows = PlateOverlays.CutDirection(plate, view.LengthToWorld(60));

            scene.Clamps = PlateOverlays.Clamps(plate);
            scene.RemovedClamps = PlateOverlays.RemovedClamps(plate);
            scene.CutOffStrokes = PlateOverlays.CutOffStrokes(plate);
            BuildCommonLines(scene);
            if (options.DrawSupport)
                BuildSupport(scene);
        }

        private void BuildCommonLines(Scene scene)
        {
            if (plate.CommonLines is not { Count: > 0 } groups)
            {
                commonLineChecks.Clear();
                return;
            }
            foreach (var stale in commonLineChecks.Keys.Where(group => !groups.Contains(group)).ToList())
                commonLineChecks.Remove(stale);
            foreach (var group in groups)
            {
                var state = string.Join(";", group.Members.Select(m =>
                    $"{m.Location.X:R},{m.Location.Y:R},{m.Rotation:R},{RuntimeHelpers.GetHashCode(m.BaseDrawing.Program)}"));
                if (!commonLineChecks.TryGetValue(group, out var cached) || cached.State != state)
                {
                    cached = (state, CommonLineGeometry.Check(group));
                    commonLineChecks[group] = cached;
                }
                foreach (var path in CommonLineGeometry.Paths(cached.Check.Spans, group.Kerf))
                    scene.CommonLines.Add(path.Points);
                foreach (var problem in cached.Check.Problems)
                {
                    // A copy: moving the part changes its box in place while the render thread draws.
                    var box = problem.Part.BoundingBox;
                    scene.CommonLineProblems.Add(new Box(box.X, box.Y, box.Length, box.Width));
                }
            }
        }

        private void BuildSupport(Scene scene)
        {
            if (plate.SupportBed is not { } bed)
                return;
            // Grid points and slats only while they are at least 6 pixels apart on screen.
            const double minimumSpacing = 6;
            if (bed is SupportGrid grid
                && view.LengthToScreen(System.Math.Min(grid.PitchX, grid.PitchY)) >= minimumSpacing)
                scene.GridPoints = grid.PointsIn(scene.Sheet, plate.Quadrant);
            else if (bed is SupportSlats slats && view.LengthToScreen(slats.Pitch) >= minimumSpacing)
                scene.Slats = slats.SlatsIn(scene.Sheet, plate.Quadrant);

            var clearance = PlateOverlays.SupportClearance(plate);
            foreach (var part in plate.Parts)
            {
                var support = supports.Analyze(part, bed, plate.Quadrant, clearance);
                if (support is not { IsComplete: true })
                    continue;
                scene.Supports.Add(new SupportItem(support.Contacts, support.CenterOfGravity, support.IsSupported,
                    support.IsSupported ? null : support.BestTabPoint));
            }
        }

        private static bool Contains(Box outer, Box inner) =>
            inner.Left >= outer.Left && inner.Right <= outer.Right && inner.Bottom >= outer.Bottom && inner.Top <= outer.Top;

        private static bool Intersects(Box a, Box b) =>
            a.Left <= b.Right && a.Right >= b.Left && a.Bottom <= b.Top && a.Top >= b.Bottom;

        private static SKColor ToSkColor(System.Drawing.Color color) =>
            color.IsEmpty ? new SKColor(130, 204, 130) : new SKColor(color.R, color.G, color.B, color.A);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var at = e.GetPosition(this);
            view.ZoomAt(new ScreenPoint(at.X, at.Y), System.Math.Pow(1.2, e.Delta.Y));
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            var point = e.GetCurrentPoint(this);
            if (e.ClickCount == 2)
            {
                ZoomToFit();
                return;
            }
            if (point.Properties.IsLeftButtonPressed)
            {
                SelectedPart = HitTest(ToWorld(point.Position));
                InvalidateVisual();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
            {
                dragStart = point.Position;
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var at = e.GetPosition(this);
            if (dragStart is { } start)
            {
                view.Pan(at.X - start.X, at.Y - start.Y);
                dragStart = at;
                InvalidateVisual();
            }

            CursorWorld = ToWorld(at);
            var hovered = dragStart == null ? HitTest(CursorWorld.Value) : HoveredPart;
            if (!ReferenceEquals(hovered, HoveredPart))
            {
                HoveredPart = hovered;
                InvalidateVisual();
            }
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            dragStart = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            CursorWorld = null;
            if (HoveredPart != null)
            {
                HoveredPart = null;
                InvalidateVisual();
            }
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F)
            {
                ZoomToFit();
                e.Handled = true;
            }
        }

        protected override void OnSizeChanged(global::Avalonia.Controls.SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            view.Resize(e.NewSize.Width, e.NewSize.Height);
            InvalidateVisual();
        }

        /// <summary>The topmost part whose material (or, for an etch-only part, closed mark) holds <paramref name="world"/>.</summary>
        private Part HitTest(Vector world)
        {
            if (plate == null)
                return null;
            for (var i = plate.Parts.Count - 1; i >= 0; i--)
            {
                var part = plate.Parts[i];
                var box = part.BoundingBox;
                if (world.X < box.Left || world.X > box.Right || world.Y < box.Bottom || world.Y > box.Top)
                    continue;
                var local = world - part.Location;
                if (PathsFor(part).Selection.Contains((float)local.X, (float)local.Y))
                    return part;
            }
            return null;
        }

        private sealed record PartItem(PartPaths Paths, Vector Location, Box Bounds, SKColor Color, SKColor Outline,
            string Label, Vector LabelPoint, bool Selected, bool Hovered, bool IsCutOff);

        private sealed record SupportItem(IReadOnlyList<Vector> Contacts, Vector CenterOfGravity, bool Supported,
            Vector? Tab);

        /// <summary>Everything one frame draws, built on the UI thread and only read on the render thread.</summary>
        private sealed class Scene
        {
            public Rect Bounds;
            public SkiaSchemePalette Palette;
            public SKMatrix Matrix;
            public double Scale;
            public bool DrawOrigin;
            public Box Sheet;
            public SKPath SheetOutline;
            public SKPath Obstacles;
            public SKPath Usable;
            public Box EdgeSpacing;
            public readonly List<PartItem> Parts = new();
            public readonly List<(SKPath Path, Vector Location)> Offsets = new();
            public Box SelectionBounds;
            public IReadOnlyList<RapidEnumerator.Segment> Rapids;
            public IReadOnlyList<Vector> PiercePoints;
            public IReadOnlyList<ContourSample> Arrows;
            public IReadOnlyList<Box> Clamps;
            public IReadOnlyList<Box> RemovedClamps;
            public IReadOnlyList<Line> CutOffStrokes;
            public readonly List<IReadOnlyList<Vector>> CommonLines = new();
            public readonly List<Box> CommonLineProblems = new();
            public IReadOnlyList<Vector> GridPoints;
            public IReadOnlyList<Box> Slats;
            public readonly List<SupportItem> Supports = new();
        }

        private sealed class PlateDrawOperation : ICustomDrawOperation
        {
            private readonly Scene scene;

            public PlateDrawOperation(Scene scene)
            {
                this.scene = scene;
            }

            public Rect Bounds => scene.Bounds;

            public bool HitTest(Point p) => Bounds.Contains(p);

            public bool Equals(ICustomDrawOperation other) => false;

            public void Dispose()
            {
            }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease == null)
                    return;
                using var api = lease.Lease();
                var canvas = api.SkCanvas;
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
                canvas.Clear(scene.Palette.Background);
                new PlatePainter(canvas, scene).Paint();
                canvas.Restore();
            }
        }

        /// <summary>
        /// Paints a scene in the WinForms view's order and pen sizes: widths and dashes are screen
        /// pixels, so parts drawn in world units scale them by the zoom.
        /// </summary>
        private sealed class PlatePainter
        {
            private static readonly SKColor LeadColor = new(255, 69, 0); // OrangeRed
            private static readonly SKColor DisplayColor = new(70, 110, 140);
            private static readonly SKColor OffsetColor = new(255, 100, 100, 120);
            private static readonly SKColor CutDirectionColor = new(60, 60, 60);
            private static readonly SKColor PierceFill = new(255, 0, 0);
            private static readonly SKColor PierceOutline = new(139, 0, 0); // DarkRed
            private static readonly SKColor ClampHatch = new(70, 80, 90, 150);
            private static readonly SKColor ClampBack = new(230, 170, 40, 70);
            private static readonly SKColor ClampOutline = new(150, 100, 0, 220);
            private static readonly SKColor RemovedClampOutline = new(110, 110, 110, 200);
            private static readonly SKColor ObstacleHatch = new(120, 60, 60, 170);
            private static readonly SKColor ObstacleBack = new(200, 80, 80, 60);
            private static readonly SKColor CutOffColor = new(255, 69, 0); // OrangeRed
            private static readonly SKColor CommonLineColor = new(0, 150, 136);
            private static readonly SKColor BrokenCommonLineColor = new(255, 0, 0);
            private static readonly SKColor GridColor = new(120, 120, 120, 110);
            private static readonly SKColor SlatColor = new(120, 120, 120, 90);
            private static readonly SKColor ContactColor = new(30, 150, 60, 220);
            private static readonly SKColor SupportedColor = new(20, 140, 50, 230);
            private static readonly SKColor TippingColor = new(210, 30, 30, 240);
            private static readonly SKColor TabColor = new(240, 140, 0, 240);

            // The WinForms default font, bold and underlined, for part numbers.
            private static readonly SKTypeface LabelTypeface =
                SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;

            private readonly SKCanvas canvas;
            private readonly Scene scene;
            // Not readonly: SKCanvas.Concat takes it by reference.
            private SKMatrix matrix;
            private readonly float scale;

            public PlatePainter(SKCanvas canvas, Scene scene)
            {
                this.canvas = canvas;
                this.scene = scene;
                matrix = scene.Matrix;
                scale = (float)scene.Scale;
            }

            private SKPoint Screen(Vector world) => matrix.MapPoint((float)world.X, (float)world.Y);

            private SKRect Screen(Box box)
            {
                var a = Screen(new Vector(box.Left, box.Top));
                var b = Screen(new Vector(box.Right, box.Bottom));
                return new SKRect(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y),
                    System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y));
            }

            private static SKPaint Stroke(SKColor color, float width = 1, float[] dash = null) => new()
            {
                Style = SKPaintStyle.Stroke,
                Color = color,
                StrokeWidth = width,
                IsAntialias = true,
                PathEffect = dash == null ? null : SKPathEffect.CreateDash(dash, 0),
            };

            private static SKPaint Fill(SKColor color) => new() { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };

            // A pen of screen width in the world frame: width 1 stays a one-pixel hairline.
            private SKPaint WorldStroke(SKColor color, float width = 1, float[] dash = null) =>
                Stroke(color, width <= 1 && dash == null ? 0 : width / scale,
                    dash?.Select(d => d / scale).ToArray());

            public void Paint()
            {
                if (scene.DrawOrigin)
                {
                    using var origin = Stroke(scene.Palette.Origin);
                    origin.IsAntialias = false;
                    var o = Screen(Vector.Zero);
                    canvas.DrawLine(o.X, 0, o.X, (float)scene.Bounds.Height, origin);
                    canvas.DrawLine(0, o.Y, (float)scene.Bounds.Width, o.Y, origin);
                }

                if (scene.Sheet == null)
                    return;

                DrawSheet();
                DrawParts();
                DrawOffsets();
                if (scene.SelectionBounds != null)
                {
                    using var pen = Stroke(scene.Palette.BoundingBox);
                    canvas.DrawRect(Screen(scene.SelectionBounds), pen);
                }
                DrawRapids();
                DrawPiercePoints();
                DrawCutDirection();
                DrawClamps();
                DrawCutOffs();
                DrawCommonLines();
                DrawSupport();
            }

            private void DrawSheet()
            {
                var palette = scene.Palette;
                canvas.Save();
                canvas.Concat(ref matrix);
                using var sheetFill = Fill(palette.PlateFill);
                if (scene.SheetOutline != null)
                {
                    canvas.DrawPath(scene.SheetOutline, sheetFill);
                    canvas.Restore();
                    if (scene.Obstacles != null)
                    {
                        HatchPath(scene.Obstacles, ObstacleHatch, ObstacleBack);
                        using var obstaclePen = WorldStroke(ObstacleHatch, 1.5f);
                        DrawWorld(scene.Obstacles, obstaclePen);
                    }
                    if (scene.Usable != null)
                    {
                        using var usablePen = WorldStroke(palette.EdgeSpacing, 1, new float[] { 3, 3 });
                        DrawWorld(scene.Usable, usablePen);
                    }
                    using var outlinePen = WorldStroke(palette.PlateOutline);
                    DrawWorld(scene.SheetOutline, outlinePen);
                    return;
                }

                var sheet = scene.Sheet;
                canvas.DrawRect(new SKRect((float)sheet.Left, (float)sheet.Bottom, (float)sheet.Right, (float)sheet.Top),
                    sheetFill);
                canvas.Restore();
                if (scene.EdgeSpacing != null)
                {
                    using var edgePen = Stroke(palette.EdgeSpacing, 1, new float[] { 3, 3 });
                    edgePen.IsAntialias = false;
                    canvas.DrawRect(Screen(scene.EdgeSpacing), edgePen);
                }
                using var outline = Stroke(palette.PlateOutline);
                outline.IsAntialias = false;
                canvas.DrawRect(Screen(sheet), outline);
            }

            private void DrawWorld(SKPath path, SKPaint paint)
            {
                canvas.Save();
                canvas.Concat(ref matrix);
                canvas.DrawPath(path, paint);
                canvas.Restore();
            }

            // GDI+ WideUpwardDiagonal: three-pixel "/" stripes every eight pixels, in screen space.
            private void HatchPath(SKPath worldPath, SKColor hatch, SKColor back)
            {
                using var screenPath = new SKPath(worldPath);
                screenPath.Transform(matrix);
                using var shader = HatchShader(hatch, back);
                using var paint = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader };
                canvas.DrawPath(screenPath, paint);
            }

            private void HatchRect(SKRect rect, SKColor hatch, SKColor back)
            {
                using var shader = HatchShader(hatch, back);
                using var paint = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader };
                canvas.DrawRect(rect, paint);
            }

            private static SKShader HatchShader(SKColor hatch, SKColor back)
            {
                using var bitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (var y = 0; y < 8; y++)
                    for (var x = 0; x < 8; x++)
                        bitmap.SetPixel(x, y, (x + y) % 8 < 3 ? hatch : back);
                return SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            }

            private void DrawParts()
            {
                var palette = scene.Palette;
                using var fill = Fill(SKColors.Black);
                using var cut = WorldStroke(SKColors.Black);
                using var etch = WorldStroke(palette.Etch, 1.5f);
                using var lead = WorldStroke(LeadColor, 1.5f);
                using var display = WorldStroke(DisplayColor, 1, new float[] { 6, 3 });
                using var text = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = 11,
                    Typeface = LabelTypeface,
                    TextAlign = SKTextAlign.Center,
                };
                using var underline = Stroke(SKColors.Black);
                var metrics = text.FontMetrics;
                var textHeight = metrics.Descent - metrics.Ascent;

                foreach (var item in scene.Parts)
                {
                    canvas.Save();
                    canvas.Concat(ref matrix);
                    canvas.Translate((float)item.Location.X, (float)item.Location.Y);

                    fill.Color = item.Selected ? palette.SelectedFill
                        : item.Hovered ? Lighten(item.Color) : item.Color;
                    canvas.DrawPath(item.Paths.Material, fill);
                    if (item.Paths.Display != null)
                        canvas.DrawPath(item.Paths.Display, display);
                    canvas.DrawPath(item.Paths.Etch, etch);
                    // Keep real cuts visible even where an etch overlaps them.
                    cut.Color = item.Selected ? palette.SelectedOutline : item.Outline;
                    canvas.DrawPath(item.Paths.Cut, cut);
                    // As on the desktop, only manually placed lead-ins are shown.
                    canvas.DrawPath(item.Paths.Lead, lead);
                    canvas.Restore();

                    // A number shows only once the part is big enough on screen to hold it.
                    if (item.IsCutOff)
                        continue;
                    var bounds = Screen(item.Bounds);
                    var width = text.MeasureText(item.Label);
                    if (bounds.Width < width || bounds.Height < textHeight)
                        continue;
                    var at = Screen(item.LabelPoint);
                    var baseline = at.Y - (metrics.Ascent + metrics.Descent) / 2;
                    canvas.DrawText(item.Label, at.X, baseline, text);
                    var lineY = baseline + System.Math.Max(1, metrics.UnderlinePosition ?? 1.5f);
                    canvas.DrawLine(at.X - width / 2, lineY, at.X + width / 2, lineY, underline);
                }
            }

            private void DrawOffsets()
            {
                if (scene.Offsets.Count == 0)
                    return;
                using var pen = WorldStroke(OffsetColor);
                foreach (var (path, location) in scene.Offsets)
                {
                    canvas.Save();
                    canvas.Concat(ref matrix);
                    canvas.Translate((float)location.X, (float)location.Y);
                    canvas.DrawPath(path, pen);
                    canvas.Restore();
                }
            }

            private void DrawRapids()
            {
                if (scene.Rapids == null)
                    return;
                using var pen = Stroke(scene.Palette.Rapid, 1, new float[] { 10, 10 });
                foreach (var segment in scene.Rapids)
                    canvas.DrawLine(Screen(segment.From), Screen(segment.To), pen);
            }

            private void DrawPiercePoints()
            {
                if (scene.PiercePoints == null)
                    return;
                using var fill = Fill(PierceFill);
                using var pen = Stroke(PierceOutline);
                foreach (var point in scene.PiercePoints)
                {
                    var p = Screen(point);
                    canvas.DrawCircle(p, 2, fill);
                    canvas.DrawCircle(p, 2, pen);
                }
            }

            private void DrawCutDirection()
            {
                if (scene.Arrows == null)
                    return;
                const float size = 6;
                using var pen = Stroke(CutDirectionColor, 1.5f);
                foreach (var sample in scene.Arrows)
                {
                    var tip = Screen(sample.Position);
                    // Screen space flips Y, so the screen angle mirrors the world tangent.
                    var angle = System.Math.Atan2(-sample.Direction.Y, sample.Direction.X);
                    foreach (var side in new[] { 0.5, -0.5 })
                    {
                        var a = angle + System.Math.PI + side;
                        canvas.DrawLine(tip.X + size * (float)System.Math.Cos(a), tip.Y + size * (float)System.Math.Sin(a),
                            tip.X, tip.Y, pen);
                    }
                }
            }

            // Machine clamps are fixtures, not material: a translucent hatched amber keep-out.
            private void DrawClamps()
            {
                using var outline = Stroke(ClampOutline, 1.5f);
                foreach (var clamp in scene.Clamps)
                {
                    var rect = Screen(clamp);
                    HatchRect(rect, ClampHatch, ClampBack);
                    canvas.DrawRect(rect, outline);
                }

                using var removed = Stroke(RemovedClampOutline, 1.5f, new float[] { 4.5f, 1.5f });
                using var text = new SKPaint { Color = RemovedClampOutline, IsAntialias = true, TextSize = 9 };
                foreach (var clamp in scene.RemovedClamps)
                {
                    var rect = Screen(clamp);
                    canvas.DrawRect(rect, removed);
                    canvas.DrawText("remove", rect.Left, rect.Top - text.FontMetrics.Descent, text);
                }
            }

            // Scrap cut-off lines stand out from the dark part outlines: a saturated orange-red dash.
            private void DrawCutOffs()
            {
                using var pen = Stroke(CutOffColor, 2, new float[] { 6, 2 });
                foreach (var stroke in scene.CutOffStrokes)
                    canvas.DrawLine(Screen(stroke.StartPoint), Screen(stroke.EndPoint), pen);

                // Each segment's number over the line, on a disc of the line's colour.
                using var disc = Fill(CutOffColor);
                using var text = new SKPaint
                {
                    Color = SKColors.White,
                    IsAntialias = true,
                    TextSize = 11,
                    Typeface = LabelTypeface,
                    TextAlign = SKTextAlign.Center,
                };
                var metrics = text.FontMetrics;
                var textHeight = metrics.Descent - metrics.Ascent;
                foreach (var item in scene.Parts)
                {
                    if (!item.IsCutOff)
                        continue;
                    var bounds = Screen(item.Bounds);
                    var diameter = System.Math.Max(text.MeasureText(item.Label), textHeight) + 2;
                    if (System.Math.Max(bounds.Width, bounds.Height) < diameter)
                        continue;
                    var at = Screen(item.LabelPoint);
                    canvas.DrawCircle(at, diameter / 2, disc);
                    canvas.DrawText(item.Label, at.X, at.Y - (metrics.Ascent + metrics.Descent) / 2, text);
                }
            }

            private void DrawCommonLines()
            {
                using var pen = Stroke(CommonLineColor, 2, new float[] { 6, 2 });
                foreach (var points in scene.CommonLines)
                    for (var i = 1; i < points.Count; i++)
                        canvas.DrawLine(Screen(points[i - 1]), Screen(points[i]), pen);
                using var broken = Stroke(BrokenCommonLineColor, 2);
                foreach (var box in scene.CommonLineProblems)
                    canvas.DrawRect(Screen(box), broken);
            }

            private void DrawSupport()
            {
                if (scene.GridPoints != null)
                {
                    // Whole-pixel squares, not anti-aliased dots, so every point is the same size.
                    using var grid = new SKPaint { Style = SKPaintStyle.Fill, Color = GridColor, IsAntialias = false };
                    foreach (var point in scene.GridPoints)
                    {
                        var p = Screen(point);
                        var x = (float)System.Math.Round(p.X) - 1;
                        var y = (float)System.Math.Round(p.Y) - 1;
                        canvas.DrawRect(x, y, 3, 3, grid);
                    }
                }
                if (scene.Slats != null)
                {
                    using var slat = new SKPaint { Style = SKPaintStyle.Fill, Color = SlatColor, IsAntialias = false };
                    foreach (var box in scene.Slats)
                    {
                        var rect = Screen(box);
                        // At least a pixel wide however far out the view is.
                        if (rect.Width < 1)
                            rect = new SKRect(rect.MidX - 0.5f, rect.Top, rect.MidX + 0.5f, rect.Bottom);
                        if (rect.Height < 1)
                            rect = new SKRect(rect.Left, rect.MidY - 0.5f, rect.Right, rect.MidY + 0.5f);
                        canvas.DrawRect(rect, slat);
                    }
                }

                using var contact = Fill(ContactColor);
                using var supported = Stroke(SupportedColor, 2);
                using var tipping = Stroke(TippingColor, 2);
                using var tab = Fill(TabColor);
                foreach (var item in scene.Supports)
                {
                    foreach (var point in item.Contacts)
                        canvas.DrawCircle(Screen(point), 2.5f, contact);
                    const float size = 6;
                    var c = Screen(item.CenterOfGravity);
                    var pen = item.Supported ? supported : tipping;
                    canvas.DrawCircle(c, size, pen);
                    canvas.DrawLine(c.X - size, c.Y, c.X + size, c.Y, pen);
                    canvas.DrawLine(c.X, c.Y - size, c.X, c.Y + size, pen);
                    if (item.Tab is { } point2)
                    {
                        const float d = 5;
                        var t = Screen(point2);
                        using var diamond = new SKPath();
                        diamond.MoveTo(t.X, t.Y - d);
                        diamond.LineTo(t.X + d, t.Y);
                        diamond.LineTo(t.X, t.Y + d);
                        diamond.LineTo(t.X - d, t.Y);
                        diamond.Close();
                        canvas.DrawPath(diamond, tab);
                    }
                }
            }

            private static SKColor Lighten(SKColor c) =>
                new((byte)(c.Red + (255 - c.Red) / 2), (byte)(c.Green + (255 - c.Green) / 2),
                    (byte)(c.Blue + (255 - c.Blue) / 2), c.Alpha);
        }
    }
}

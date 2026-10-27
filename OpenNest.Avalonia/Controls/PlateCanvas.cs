using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OpenNest.Avalonia.Rendering;
using OpenNest.CNC;
using OpenNest.Geometry;
using SkiaSharp;
using Control = Avalonia.Controls.Control;
using Vector = OpenNest.Geometry.Vector;

namespace OpenNest.Avalonia.Controls
{
    /// <summary>
    /// Draws one plate with Skia: pans with a left or middle drag, zooms at the cursor with the
    /// wheel, fits on a double click, and highlights the part under the cursor.
    /// </summary>
    public sealed class PlateCanvas : Control
    {
        private static readonly SKColor BackgroundColor = new(169, 169, 169);
        private static readonly SKColor PlateFillColor = new(245, 245, 245);
        private static readonly SKColor PlateOutlineColor = new(128, 128, 128);
        private static readonly SKColor EdgeSpacingColor = new(180, 180, 180);
        private static readonly SKColor LeadColor = new(255, 0, 0);
        private static readonly SKColor EtchColor = new(0, 150, 60);
        private static readonly SKColor SelectedColor = new(150, 200, 255, 90 * 255 / 100);

        // Paths are built once per program and only read afterwards, so the render thread may draw
        // them while the UI thread builds others. Keyed by reference: a rotation replaces the program.
        private readonly Dictionary<CNC.Program, ProgramPaths> paths = new();

        private Plate plate;
        private double scale = 1;
        private Point origin;
        private Point? dragStart;
        private bool fitPending = true;

        public Plate Plate
        {
            get => plate;
            set
            {
                plate = value;
                HoveredPart = null;
                SelectedPart = null;
                fitPending = true;
                InvalidateVisual();
            }
        }

        public Part HoveredPart { get; private set; }

        public Part SelectedPart { get; private set; }

        /// <summary>The cursor in plate coordinates, or null when it is outside the view.</summary>
        public Vector? CursorWorld { get; private set; }

        public event EventHandler ViewChanged;

        public PlateCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public void ClearCache() => paths.Clear();

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
            var extents = PlateRect(plate);
            foreach (var part in plate.Parts)
                extents = Union(extents, part.BoundingBox);
            const double margin = 20;
            var sx = (Bounds.Width - 2 * margin) / System.Math.Max(extents.Length, 1e-6);
            var sy = (Bounds.Height - 2 * margin) / System.Math.Max(extents.Width, 1e-6);
            scale = System.Math.Min(sx, sy);
            origin = new Point(
                Bounds.Width / 2 - (extents.Left + extents.Length / 2) * scale,
                Bounds.Height / 2 + (extents.Bottom + extents.Width / 2) * scale);
            fitPending = false;
            return true;
        }

        public double Zoom => scale;

        private Vector ToWorld(Point p) => new((p.X - origin.X) / scale, (origin.Y - p.Y) / scale);

        private SKMatrix WorldMatrix =>
            new((float)scale, 0, (float)origin.X, 0, (float)-scale, (float)origin.Y, 0, 0, 1);

        private static Box PlateRect(Plate plate)
        {
            var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
            var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;
            return new Box(left, bottom, plate.Size.Length, plate.Size.Width);
        }

        private static Box Union(Box a, Box b)
        {
            var left = System.Math.Min(a.Left, b.Left);
            var bottom = System.Math.Min(a.Bottom, b.Bottom);
            return new Box(left, bottom,
                System.Math.Max(a.Right, b.Right) - left, System.Math.Max(a.Top, b.Top) - bottom);
        }

        private ProgramPaths PathsFor(Part part)
        {
            if (!paths.TryGetValue(part.Program, out var result))
                paths[part.Program] = result = ProgramPaths.Build(part.Program);
            return result;
        }

        public override void Render(DrawingContext context)
        {
            // A redraw may not be requested during a render, so tell listeners afterwards.
            if (fitPending && Fit())
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ViewChanged?.Invoke(this, EventArgs.Empty));

            var items = new List<PartItem>();
            if (plate != null)
            {
                for (var i = 0; i < plate.Parts.Count; i++)
                {
                    var part = plate.Parts[i];
                    items.Add(new PartItem(PathsFor(part), part.Location, part.BoundingBox,
                        ToSkColor(part.BaseDrawing.Color), (i + 1).ToString(),
                        ReferenceEquals(part, SelectedPart), ReferenceEquals(part, HoveredPart),
                        part.BaseDrawing.IsCutOff, part.HasManualLeadIns));
                }
            }

            context.Custom(new PlateDrawOperation(new Rect(Bounds.Size), plate == null ? null : PlateRect(plate),
                plate?.EdgeSpacing, WorldMatrix, items));
        }

        private static SKColor ToSkColor(System.Drawing.Color color) =>
            color.IsEmpty ? new SKColor(130, 204, 130) : new SKColor(color.R, color.G, color.B, color.A);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var at = e.GetPosition(this);
            var factor = System.Math.Pow(1.2, e.Delta.Y);
            // Keep the world point under the cursor fixed.
            origin = new Point(at.X - (at.X - origin.X) * factor, at.Y - (at.Y - origin.Y) * factor);
            scale *= factor;
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
                origin += at - start;
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
            if (fitPending)
                InvalidateVisual();
        }

        /// <summary>The topmost part whose material contains <paramref name="world"/>.</summary>
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
                if (PathsFor(part).Fill.Contains((float)local.X, (float)local.Y))
                    return part;
            }
            return null;
        }

        private sealed record PartItem(ProgramPaths Paths, Vector Location, Box Bounds, SKColor Color,
            string Label, bool Selected, bool Hovered, bool IsCutOff, bool ShowLeads);

        private sealed class PlateDrawOperation : ICustomDrawOperation
        {
            private readonly Box plateRect;
            private readonly Spacing? edgeSpacing;
            private readonly SKMatrix matrix;
            private readonly List<PartItem> items;

            public PlateDrawOperation(Rect bounds, Box plateRect, Spacing? edgeSpacing, SKMatrix matrix,
                List<PartItem> items)
            {
                Bounds = bounds;
                this.plateRect = plateRect;
                this.edgeSpacing = edgeSpacing;
                this.matrix = matrix;
                this.items = items;
            }

            public Rect Bounds { get; }

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
                canvas.Clear(BackgroundColor);

                if (plateRect != null)
                    Draw(canvas);

                canvas.Restore();
            }

            private void Draw(SKCanvas canvas)
            {
                // Hairline strokes (width 0) stay one pixel wide at any zoom.
                using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
                using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0, IsAntialias = true };

                canvas.Save();
                var world = matrix;
                canvas.Concat(ref world);

                var plate = new SKRect((float)plateRect.Left, (float)plateRect.Bottom,
                    (float)plateRect.Right, (float)plateRect.Top);
                fill.Color = PlateFillColor;
                canvas.DrawRect(plate, fill);
                if (edgeSpacing is { } spacing)
                {
                    stroke.Color = EdgeSpacingColor;
                    canvas.DrawRect(new SKRect(plate.Left + (float)spacing.Left,
                        plate.Top + (float)spacing.Bottom, plate.Right - (float)spacing.Right,
                        plate.Bottom - (float)spacing.Top), stroke);
                }
                stroke.Color = PlateOutlineColor;
                canvas.DrawRect(plate, stroke);

                var view = canvas.LocalClipBounds;
                foreach (var item in items)
                {
                    var b = item.Bounds;
                    if (b.Right < view.Left || b.Left > view.Right || b.Top < view.Top || b.Bottom > view.Bottom)
                        continue;

                    canvas.Save();
                    canvas.Translate((float)item.Location.X, (float)item.Location.Y);

                    fill.Color = item.Selected ? SelectedColor
                        : item.Hovered ? Lighten(item.Color) : item.Color;
                    canvas.DrawPath(item.Paths.Fill, fill);

                    stroke.Color = EtchColor;
                    canvas.DrawPath(item.Paths.Etch, stroke);
                    stroke.Color = item.Selected ? new SKColor(30, 90, 200) : Darken(item.Color);
                    canvas.DrawPath(item.Paths.Cut, stroke);
                    // As on the desktop, only manually placed lead-ins are shown.
                    if (item.ShowLeads)
                    {
                        stroke.Color = LeadColor;
                        canvas.DrawPath(item.Paths.Lead, stroke);
                    }

                    canvas.Restore();
                }

                canvas.Restore();
                DrawLabels(canvas);
            }

            // Ids are drawn in screen space and only where the part is big enough on screen to hold one.
            private void DrawLabels(SKCanvas canvas)
            {
                using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = 12 };
                foreach (var item in items)
                {
                    if (item.IsCutOff)
                        continue;
                    var center = matrix.MapPoint((float)item.Bounds.Center.X, (float)item.Bounds.Center.Y);
                    var width = item.Bounds.Length * matrix.ScaleX;
                    var height = item.Bounds.Width * -matrix.ScaleY;
                    var measured = text.MeasureText(item.Label);
                    if (measured + 4 > width || text.TextSize + 4 > height)
                        continue;
                    canvas.DrawText(item.Label, center.X - measured / 2, center.Y + text.TextSize / 3, text);
                }
            }

            private static SKColor Darken(SKColor c) =>
                new((byte)(c.Red * 0.55), (byte)(c.Green * 0.55), (byte)(c.Blue * 0.55), 255);

            private static SKColor Lighten(SKColor c) =>
                new((byte)(c.Red + (255 - c.Red) / 2), (byte)(c.Green + (255 - c.Green) / 2),
                    (byte)(c.Blue + (255 - c.Blue) / 2), c.Alpha);
        }
    }
}

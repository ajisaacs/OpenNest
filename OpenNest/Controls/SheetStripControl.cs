using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// A whole-job layout as a row of sheets, all drawn at one scale so sheet sizes compare
    /// truly. Read-only: it never attaches parts to a plate or changes drawing quantities.
    /// Repeated identical sheets show once with their count.
    /// </summary>
    internal sealed class SheetStripControl : ScrollableControl
    {
        private const int Gap = 16;
        private const int Edge = 8;

        private readonly List<Tile> tiles = new();
        private ColorScheme colorScheme = ColorScheme.Default;
        private bool showRemnant;

        public SheetStripControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            AutoScroll = true;
            BackColor = ColorScheme.Default.BackgroundColor;
        }

        public ColorScheme ColorScheme
        {
            get => colorScheme;
            set
            {
                colorScheme = value ?? ColorScheme.Default;
                BackColor = colorScheme.BackgroundColor;
                Invalidate();
            }
        }

        /// <summary>The text shown when there are no sheets.</summary>
        public string EmptyText { get; set; } = "No sheets yet";

        /// <summary>
        /// Shows each sheet's free strip: a dimension across it and its distance in the caption
        /// (to the right edge for vertical strips, to the top for horizontal ones).
        /// </summary>
        public bool ShowRemnant
        {
            get => showRemnant;
            set
            {
                showRemnant = value;
                UpdateScrollSize();
                Invalidate();
            }
        }

        public int SheetCount => tiles.Sum(t => t.Quantity);

        /// <summary>Material area over sheet area for every physical sheet; 0 with no sheets.</summary>
        public double Utilization
        {
            get
            {
                var sheetArea = tiles.Sum(t => t.Quantity * t.SheetArea);
                return sheetArea > 0 ? tiles.Sum(t => t.Quantity * t.PartArea) / sheetArea : 0;
            }
        }

        public int PartCount => tiles.Sum(t => t.Quantity * t.PartCount);

        public void SetSheets(IReadOnlyList<ProposedPlate> sheets)
        {
            ClearTiles();
            var first = 1;
            foreach (var sheet in sheets ?? Array.Empty<ProposedPlate>())
            {
                tiles.Add(new Tile(sheet, first));
                first += sheet.Quantity;
            }
            UpdateScrollSize();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ClearTiles();
            base.Dispose(disposing);
        }

        private void ClearTiles()
        {
            foreach (var tile in tiles)
                tile.Dispose();
            tiles.Clear();
        }

        private int CaptionHeight => Font.Height * (showRemnant ? 3 : 2) + 4;

        private string Caption(Tile tile) => showRemnant && tile.Remnant is { } remnant
            ? $"{tile.Title}\n{tile.Detail}\n{remnant}"
            : $"{tile.Title}\n{tile.Detail}";

        /// <summary>
        /// Pixels per world unit, shared by every sheet. All sheets fit the width when they can;
        /// otherwise they shrink to a third of full height and the strip scrolls. When the
        /// captions alone are wider than the strip, shrinking cannot avoid the scroll, so the
        /// sheets keep full height and the strip scrolls.
        /// </summary>
        internal float PixelsPerUnit
        {
            get
            {
                if (tiles.Count == 0)
                    return 0;
                var height = ClientSize.Height - CaptionHeight - Edge * 2 - SystemInformation.HorizontalScrollBarHeight;
                var tallest = tiles.Max(t => t.Sheet.Width);
                if (tallest <= 0 || height <= 0)
                    return 0;
                var full = (float)(height / tallest);
                var width = ClientSize.Width - Edge * 2 - Gap * (tiles.Count - 1);
                if (tiles.Sum(t => TileWidth(t, 0)) > width)
                    return full;
                // Largest scale whose tiles, captions included, fit the width (tile widths grow with scale).
                float low = 0, high = full;
                for (var i = 0; i < 24; i++)
                {
                    var mid = (low + high) / 2;
                    if (tiles.Sum(t => TileWidth(t, mid)) <= width)
                        low = mid;
                    else
                        high = mid;
                }
                return System.Math.Max(low, full / 3);
            }
        }

        private void UpdateScrollSize()
        {
            var scale = PixelsPerUnit;
            var width = Edge * 2 + tiles.Sum(t => TileWidth(t, scale)) + Gap * System.Math.Max(0, tiles.Count - 1);
            AutoScrollMinSize = new System.Drawing.Size(width, 0);
        }

        private int TileWidth(Tile tile, float scale) =>
            System.Math.Max((int)System.Math.Ceiling(tile.Sheet.Length * scale), TextRenderer.MeasureText(Caption(tile), Font).Width);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (tiles.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var scale = PixelsPerUnit;
            if (scale <= 0)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var x = Edge + AutoScrollPosition.X;
            // Center the row vertically in the room above the scroll bar.
            var room = ClientSize.Height - SystemInformation.HorizontalScrollBarHeight;
            var content = (float)(tiles.Max(t => t.Sheet.Width) * scale) + CaptionHeight;
            var baseline = (room - content) / 2 + (content - CaptionHeight);
            using var partPen = new Pen(Color.FromArgb(60, 60, 60), 0);
            using var clampBrush = new SolidBrush(Color.FromArgb(90, 230, 170, 40));
            // The plate view's obstacle colors.
            using var obstacleBrush = new HatchBrush(HatchStyle.WideUpwardDiagonal,
                Color.FromArgb(170, 120, 60, 60), Color.FromArgb(60, 200, 80, 80));
            using var obstaclePen = new Pen(Color.FromArgb(170, 120, 60, 60), 1.5f);
            using var remnantCap = new AdjustableArrowCap(3, 4);
            using var remnantPen = new Pen(Color.FromArgb(20, 100, 190), 1.5f)
            {
                CustomStartCap = remnantCap,
                CustomEndCap = remnantCap,
            };
            foreach (var tile in tiles)
            {
                var width = TileWidth(tile, scale);
                var sheetWidth = (float)(tile.Sheet.Length * scale);
                var sheetHeight = (float)(tile.Sheet.Width * scale);
                var left = x + (width - sheetWidth) / 2;
                var sheetRect = new RectangleF(left, baseline - sheetHeight, sheetWidth, sheetHeight);

                var state = g.Save();
                // World Y grows up; the sheet's lower-left corner sits at the rectangle's bottom-left.
                g.TranslateTransform(left, baseline);
                g.ScaleTransform(scale, -scale);
                g.TranslateTransform((float)-tile.Sheet.X, (float)-tile.Sheet.Y);
                using var toDevice = g.Transform;
                g.Restore(state);

                // An outlined sheet shows its real shape, as the plate view draws it; its
                // paths go to device space so the pens and the hatch keep their pixel size.
                using var outline = Transformed(tile.Outline, toDevice);
                using var obstacles = Transformed(tile.Obstacles, toDevice);
                if (outline != null)
                {
                    g.FillPath(colorScheme.Gdi().LayoutFillBrush, outline);
                    if (obstacles != null)
                    {
                        g.FillPath(obstacleBrush, obstacles);
                        g.DrawPath(obstaclePen, obstacles);
                    }
                }
                else
                    g.FillRectangle(colorScheme.Gdi().LayoutFillBrush, sheetRect);

                state = g.Save();
                g.Transform = toDevice;
                foreach (var zone in tile.Clamps)
                    g.FillRectangle(clampBrush, zone);
                foreach (var (path, brush) in tile.Parts)
                {
                    g.FillPath(brush, path);
                    g.DrawPath(partPen, path);
                }
                g.Restore(state);

                if (outline != null)
                    g.DrawPath(colorScheme.Gdi().LayoutOutlinePen, outline);
                else
                    g.DrawRectangle(colorScheme.Gdi().LayoutOutlinePen, sheetRect.X, sheetRect.Y, sheetRect.Width, sheetRect.Height);
                if (showRemnant && tile.Remnant != null)
                {
                    // Device space, so the arrows keep their pixel size.
                    var ends = (PointF[])tile.RemnantSpan.Clone();
                    toDevice.TransformPoints(ends);
                    if (System.Math.Abs(ends[1].X - ends[0].X) + System.Math.Abs(ends[1].Y - ends[0].Y) >= 14)
                        g.DrawLine(remnantPen, ends[0], ends[1]);
                }
                var caption = new Rectangle(x, (int)baseline + 4, width, CaptionHeight);
                TextRenderer.DrawText(g, Caption(tile), Font, caption, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                x += width + Gap;
            }
        }

        private static GraphicsPath Transformed(GraphicsPath world, Matrix toDevice)
        {
            if (world == null)
                return null;
            var path = (GraphicsPath)world.Clone();
            path.Transform(toDevice);
            return path;
        }

        /// <summary>One distinct sheet layout with its world-space paths, built once per update.</summary>
        private sealed class Tile : IDisposable
        {
            public Tile(ProposedPlate plate, int firstNumber)
            {
                var stock = plate.Stock;
                Quantity = plate.Quantity;
                PartCount = plate.Parts.Count;
                // Sheet extents around the origin as the plate view places them for this quadrant.
                var length = stock.Size.Length;
                var width = stock.Size.Width;
                var x = stock.Quadrant is 2 or 3 ? -length : 0;
                var y = stock.Quadrant is 3 or 4 ? -width : 0;
                Sheet = new Geometry.Box(x, y, length, width);
                SheetArea = stock.Area;
                if (stock.Outline != null)
                {
                    // The outline is in plate coordinates; the sheet's corner moves it to the parts' frame.
                    Outline = new GraphicsPath(FillMode.Alternate);
                    Outline.AddPolygon(ToPoints(stock.Outline.Points, x, y));
                    if (stock.Outline.Obstacles.Count > 0)
                        Obstacles = new GraphicsPath(FillMode.Winding);
                    foreach (var obstacle in stock.Outline.Obstacles)
                    {
                        Outline.AddPolygon(ToPoints(obstacle, x, y));
                        Obstacles.AddPolygon(ToPoints(obstacle, x, y));
                    }
                }
                // Clamp zones are already in the placement frame, like the parts.
                foreach (var zone in stock.ClampZones)
                    Clamps.Add(new RectangleF((float)zone.Bounds.X, (float)zone.Bounds.Y,
                        (float)zone.Bounds.Length, (float)zone.Bounds.Width));
                foreach (var part in plate.Parts)
                {
                    PartArea += part.BaseDrawing.Area;
                    var color = part.BaseDrawing.Color.IsEmpty || part.BaseDrawing.Color.A == 0
                        ? Color.SteelBlue : part.BaseDrawing.Color;
                    Parts.Add((part.Program.GetFillPath(part.Location), new SolidBrush(Color.FromArgb(200, color))));
                }

                Remnant = SheetRemnant.Of(plate);
                if (Remnant is { } remnant)
                {
                    // Across the free strip, through the sheet's middle.
                    var (midX, midY) = ((float)(x + length / 2), (float)(y + width / 2));
                    var distance = (float)remnant.Distance;
                    RemnantSpan = remnant.Edge switch
                    {
                        SheetEdge.Right => new[] { new PointF((float)(x + length) - distance, midY), new PointF((float)(x + length), midY) },
                        SheetEdge.Left => new[] { new PointF((float)x, midY), new PointF((float)x + distance, midY) },
                        SheetEdge.Top => new[] { new PointF(midX, (float)(y + width) - distance), new PointF(midX, (float)(y + width)) },
                        _ => new[] { new PointF(midX, (float)y), new PointF(midX, (float)y + distance) },
                    };
                }

                Title = Quantity == 1 ? $"Sheet {firstNumber}" : $"Sheets {firstNumber}–{firstNumber + Quantity - 1} (×{Quantity})";
                var utilization = SheetArea > 0 ? PartArea / SheetArea : 0;
                Detail = $"{stock.Size} · {PartCount} parts · {utilization:P0}";
            }

            public Geometry.Box Sheet { get; }

            /// <summary>The sheet's material area: inside the outline when it has one.</summary>
            public double SheetArea { get; }

            /// <summary>An outlined sheet's shape with its obstacles as holes, in the parts' frame; null for a rectangle.</summary>
            public GraphicsPath Outline { get; }

            /// <summary>The outlined sheet's obstacles; null without any.</summary>
            public GraphicsPath Obstacles { get; }
            public int Quantity { get; }
            public int PartCount { get; }
            public double PartArea { get; }
            public string Title { get; }
            public string Detail { get; }

            /// <summary>The sheet's free strip; null when it has none to measure.</summary>
            public SheetRemnant? Remnant { get; }

            /// <summary>The two world points the free strip's dimension runs between.</summary>
            public PointF[] RemnantSpan { get; }
            public List<RectangleF> Clamps { get; } = new();
            public List<(GraphicsPath Path, Brush Brush)> Parts { get; } = new();

            private static PointF[] ToPoints(IReadOnlyList<Geometry.Vector> points, double x, double y) =>
                points.Select(p => new PointF((float)(p.X + x), (float)(p.Y + y))).ToArray();

            public void Dispose()
            {
                Outline?.Dispose();
                Obstacles?.Dispose();
                foreach (var (path, brush) in Parts)
                {
                    path.Dispose();
                    brush.Dispose();
                }
                Parts.Clear();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Engine.Jobs;

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

        public int SheetCount => tiles.Sum(t => t.Quantity);

        /// <summary>Material area over sheet area for every physical sheet; 0 with no sheets.</summary>
        public double Utilization
        {
            get
            {
                var sheetArea = tiles.Sum(t => t.Quantity * t.Sheet.Width * t.Sheet.Length);
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

        private int CaptionHeight => Font.Height * 2 + 4;

        /// <summary>
        /// Pixels per world unit, shared by every sheet. All sheets fit the width when they can;
        /// otherwise they shrink to a third of full height and the strip scrolls.
        /// </summary>
        private float PixelsPerUnit
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
                // Largest scale whose tiles, captions included, fit the width (tile widths grow with scale).
                var width = ClientSize.Width - Edge * 2 - Gap * (tiles.Count - 1);
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
            System.Math.Max((int)System.Math.Ceiling(tile.Sheet.Length * scale), TextRenderer.MeasureText(tile.Detail, Font).Width);

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
            foreach (var tile in tiles)
            {
                var width = TileWidth(tile, scale);
                var sheetWidth = (float)(tile.Sheet.Length * scale);
                var sheetHeight = (float)(tile.Sheet.Width * scale);
                var left = x + (width - sheetWidth) / 2;
                var sheetRect = new RectangleF(left, baseline - sheetHeight, sheetWidth, sheetHeight);
                g.FillRectangle(colorScheme.LayoutFillBrush, sheetRect);

                var state = g.Save();
                // World Y grows up; the sheet's lower-left corner sits at the rectangle's bottom-left.
                g.TranslateTransform(left, baseline);
                g.ScaleTransform(scale, -scale);
                g.TranslateTransform((float)-tile.Sheet.X, (float)-tile.Sheet.Y);
                foreach (var zone in tile.Clamps)
                    g.FillRectangle(clampBrush, zone);
                foreach (var (path, brush) in tile.Parts)
                {
                    g.FillPath(brush, path);
                    g.DrawPath(partPen, path);
                }
                g.Restore(state);

                g.DrawRectangle(colorScheme.LayoutOutlinePen, sheetRect.X, sheetRect.Y, sheetRect.Width, sheetRect.Height);
                var caption = new Rectangle(x, (int)baseline + 4, width, CaptionHeight);
                TextRenderer.DrawText(g, tile.Title + "\n" + tile.Detail, Font, caption, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                x += width + Gap;
            }
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

                Title = Quantity == 1 ? $"Sheet {firstNumber}" : $"Sheets {firstNumber}–{firstNumber + Quantity - 1} (×{Quantity})";
                var utilization = length * width > 0 ? PartArea / (length * width) : 0;
                Detail = $"{stock.Size} · {PartCount} parts · {utilization:P0}";
            }

            public Geometry.Box Sheet { get; }
            public int Quantity { get; }
            public int PartCount { get; }
            public double PartArea { get; }
            public string Title { get; }
            public string Detail { get; }
            public List<RectangleF> Clamps { get; } = new();
            public List<(GraphicsPath Path, Brush Brush)> Parts { get; } = new();

            public void Dispose()
            {
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

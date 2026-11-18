using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using OpenNest.Math;

namespace OpenNest.Controls
{
    /// <summary>
    /// The plate's grain direction as a brushed-metal swatch with a double-headed arrow, just
    /// off the plate's lower-left corner. It keeps a fixed screen size at any zoom, and clicking it turns the grain 90°.
    /// </summary>
    internal static class GrainIndicator
    {
        private const float Size = 64f;
        private const float Gap = 8f;

        /// <summary>
        /// The indicator's square in graph coordinates, diagonally off the plate's lower-left
        /// corner. When the view is too tight for that (Zoom to Fit leaves little margin), it
        /// is pulled inside the view's own lower-left corner so it never scrolls out of reach.
        /// </summary>
        public static RectangleF Bounds(DrawControl view, Plate plate)
        {
            var box = plate.BoundingBox(includeParts: false);
            var corner = view.PointWorldToGraph(box.X, box.Y);
            var x = corner.X - Gap - Size;
            var y = corner.Y + Gap;

            var viewTopLeft = view.PointControlToGraph(0, 0);
            var viewBottom = view.PointControlToGraph(0, view.ClientSize.Height).Y;
            x = System.Math.Max(x, viewTopLeft.X + Gap);
            y = System.Math.Min(y, viewBottom - Gap - Size);

            return new RectangleF(x, y, Size, Size);
        }

        /// <summary>The grain after a click: a quarter turn, as a line in [0, π).</summary>
        public static double Turned(double grainAngle) =>
            Plate.NormalizeGrainAngle(grainAngle + Angle.HalfPI);

        /// <summary>
        /// Draws a small card holding a brushed-metal swatch whose streaks run with the grain,
        /// a double-headed arrow along them and a "GRAIN" caption underneath.
        /// </summary>
        public static void Draw(Graphics g, RectangleF bounds, double grainAngle, bool hot, bool enabled)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var accent = Color.FromArgb(230, 120, 0);
            var ink = !enabled ? Color.FromArgb(150, 150, 150) : hot ? accent : Color.FromArgb(55, 60, 66);

            // The card leaves a pixel or two inside the bounds for its shadow.
            var card = new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            DrawShadow(g, card);

            using (var cardPath = RoundedRect(card, 6))
            {
                using (var fill = new LinearGradientBrush(card,
                    hot && enabled ? Color.FromArgb(250, 255, 249, 240) : Color.FromArgb(245, 255, 255, 255),
                    hot && enabled ? Color.FromArgb(250, 255, 236, 214) : Color.FromArgb(245, 240, 242, 245),
                    LinearGradientMode.Vertical))
                    g.FillPath(fill, cardPath);

                using var border = new Pen(hot && enabled ? accent : Color.FromArgb(140, 110, 116, 124), hot && enabled ? 1.5f : 1f);
                g.DrawPath(border, cardPath);
            }

            using var font = new Font("Segoe UI", 6.75f, FontStyle.Bold);
            const string label = "GRAIN";
            var labelSize = g.MeasureString(label, font);
            var labelTop = card.Bottom - labelSize.Height - 2;

            var swatch = new RectangleF(card.X + 6, card.Y + 6, card.Width - 12, labelTop - card.Y - 7);
            DrawSwatch(g, swatch, grainAngle, enabled);
            DrawArrow(g, swatch, grainAngle, ink);

            using (var textBrush = new SolidBrush(ink))
                g.DrawString(label, font, textBrush, card.X + (card.Width - labelSize.Width) / 2, labelTop);

            g.Restore(state);
        }

        private static void DrawShadow(Graphics g, RectangleF card)
        {
            for (var i = 1; i <= 2; i++)
            {
                var offset = new RectangleF(card.X + i * 0.5f, card.Y + i, card.Width, card.Height);
                using var path = RoundedRect(offset, 6);
                using var brush = new SolidBrush(Color.FromArgb(28, 0, 0, 0));
                g.FillPath(brush, path);
            }
        }

        /// <summary>A sheet-metal sample whose brushed streaks run with the grain.</summary>
        private static void DrawSwatch(Graphics g, RectangleF swatch, double grainAngle, bool enabled)
        {
            using var path = RoundedRect(swatch, 3);
            using (var fill = new LinearGradientBrush(swatch,
                enabled ? Color.FromArgb(228, 232, 236) : Color.FromArgb(236, 237, 238),
                enabled ? Color.FromArgb(198, 204, 211) : Color.FromArgb(220, 221, 222),
                LinearGradientMode.ForwardDiagonal))
                g.FillPath(fill, path);

            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            g.TranslateTransform(swatch.X + swatch.Width / 2, swatch.Y + swatch.Height / 2);
            // Screen Y runs down, so a counter-clockwise world angle turns the other way here.
            g.RotateTransform((float)-Angle.ToDegrees(grainAngle));

            // Fixed pseudo-random streaks so the texture reads as brushing yet never shimmers.
            var reach = (float)System.Math.Sqrt(swatch.Width * swatch.Width + swatch.Height * swatch.Height) / 2 + 2;
            using var pen = new Pen(Color.Empty, 1f);
            var row = 0;
            for (var y = -reach; y <= reach; y += 1.75f, row++)
            {
                var h = (uint)row * 2654435761u;
                var light = (h & 0x100) != 0;
                var alpha = (int)(enabled ? 14 + (h >> 24) % 46 : 8 + (h >> 24) % 20);
                pen.Color = light ? Color.FromArgb(alpha * 2, 255, 255, 255) : Color.FromArgb(alpha, 70, 78, 88);

                // Some streaks break part way along, as brushing marks do.
                var start = -reach + (h >> 12) % 9;
                var breakAt = (h & 0x3) == 0 ? -reach / 2 + (h >> 16) % (uint)reach : reach;
                g.DrawLine(pen, start, y, breakAt - 2, y);
                if (breakAt < reach)
                    g.DrawLine(pen, breakAt + 3 + (h >> 20) % 5, y, reach, y);
            }

            g.Restore(state);

            using var edge = new Pen(Color.FromArgb(enabled ? 110 : 60, 95, 102, 110), 1f);
            g.DrawPath(edge, path);
        }

        /// <summary>A double-headed arrow along the grain, as long as the swatch allows that way.</summary>
        private static void DrawArrow(Graphics g, RectangleF swatch, double grainAngle, Color ink)
        {
            var cos = System.Math.Abs(System.Math.Cos(grainAngle));
            var sin = System.Math.Abs(System.Math.Sin(grainAngle));
            var halfWidth = swatch.Width / 2 - 4;
            var halfHeight = swatch.Height / 2 - 4;
            var half = (float)System.Math.Min(
                cos > 1e-6 ? halfWidth / cos : double.MaxValue,
                sin > 1e-6 ? halfHeight / sin : double.MaxValue);

            const float headLength = 7f, headWidth = 4.5f, shaft = 1.1f;
            var outline = new[]
            {
                new PointF(-half, 0),
                new PointF(-half + headLength, -headWidth),
                new PointF(-half + headLength, -shaft),
                new PointF(half - headLength, -shaft),
                new PointF(half - headLength, -headWidth),
                new PointF(half, 0),
                new PointF(half - headLength, headWidth),
                new PointF(half - headLength, shaft),
                new PointF(-half + headLength, shaft),
                new PointF(-half + headLength, headWidth),
            };

            var state = g.Save();
            g.TranslateTransform(swatch.X + swatch.Width / 2, swatch.Y + swatch.Height / 2);
            g.RotateTransform((float)-Angle.ToDegrees(grainAngle));

            // A pale halo lifts the arrow off the brushed texture.
            using (var halo = new Pen(Color.FromArgb(210, 255, 255, 255), 3f) { LineJoin = LineJoin.Round })
                g.DrawPolygon(halo, outline);
            using (var brush = new SolidBrush(ink))
                g.FillPolygon(brush, outline);

            g.Restore(state);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Math;

namespace OpenNest.Controls
{
    /// <summary>
    /// The plate's grain direction as a double-headed arrow just off the plate's lower-left
    /// corner. It keeps a fixed screen size at any zoom, and clicking it turns the grain 90°.
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

        public static void Draw(Graphics g, RectangleF bounds, double grainAngle, bool hot, bool enabled)
        {
            var previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var color = !enabled ? Color.Gray : hot ? Color.FromArgb(255, 140, 0) : Color.FromArgb(70, 70, 70);

            // A light backing keeps the arrow readable where it overlaps the plate or parts.
            using (var backing = new SolidBrush(hot && enabled
                ? Color.FromArgb(200, 255, 225, 190)
                : Color.FromArgb(170, 255, 255, 255)))
                g.FillEllipse(backing, bounds);

            using var font = new Font(FontFamily.GenericSansSerif, 8f, FontStyle.Bold);
            using var textBrush = new SolidBrush(color);
            var label = "GRAIN";
            var labelSize = g.MeasureString(label, font);

            // The arrow sits above the label; screen Y runs down, so the world angle's sine flips.
            var center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + (bounds.Height - labelSize.Height) / 2);
            var half = (bounds.Width - 16) / 2;
            var dx = (float)(System.Math.Cos(grainAngle) * half);
            var dy = (float)(-System.Math.Sin(grainAngle) * half);

            using var head = new AdjustableArrowCap(3.5f, 3.5f);
            using var pen = new Pen(color, 2.5f)
            {
                CustomStartCap = head,
                CustomEndCap = head,
            };
            g.DrawLine(pen, center.X - dx, center.Y - dy, center.X + dx, center.Y + dy);

            g.DrawString(label, font, textBrush,
                bounds.X + (bounds.Width - labelSize.Width) / 2, bounds.Bottom - labelSize.Height);

            g.SmoothingMode = previous;
        }
    }
}

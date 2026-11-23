using System.Drawing;
using System.Globalization;

namespace OpenNest.Controls
{
    // Rulers along the left and bottom edges, in drawing units. Zoom to fit leaves them clear.
    public partial class EntityView
    {
        internal const int RulerSize = 22;
        private const float MinMajorTickSpacing = 70f;

        private readonly Font rulerFont = new Font("Segoe UI", 7f);
        private readonly SolidBrush rulerBackBrush = new SolidBrush(Color.FromArgb(44, 52, 61));
        private readonly SolidBrush rulerTextBrush = new SolidBrush(Color.FromArgb(190, 200, 210));
        private readonly Pen rulerTickPen = new Pen(Color.FromArgb(140, 150, 160));
        private readonly Pen rulerCursorPen = new Pen(Color.FromArgb(255, 140, 40));
        private Point? rulerCursor;

        /// <summary>
        /// The labeled tick step: the smallest 1, 2 or 5 times a power of ten that keeps labeled
        /// ticks at least <see cref="MinMajorTickSpacing"/> pixels apart, with the number of
        /// unlabeled divisions between them.
        /// </summary>
        internal static double RulerStep(float viewScale, out int divisions)
        {
            var raw = MinMajorTickSpacing / viewScale;
            var magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
            var mantissa = raw <= magnitude ? 1 : raw <= magnitude * 2 ? 2 : raw <= magnitude * 5 ? 5 : 10;
            divisions = mantissa == 2 ? 4 : mantissa == 5 ? 5 : 10;
            return magnitude * mantissa;
        }

        public override void ZoomToArea(double x, double y, double width, double height, bool redraw = true)
        {
            if (width <= 0 || height <= 0)
                return;

            var viewWidth = Width - RulerSize;
            var viewHeight = Height - RulerSize;
            var scale = (float)System.Math.Min(
                (viewHeight - BorderWidth) / height,
                (viewWidth - BorderWidth) / width);
            ViewScale = System.Math.Clamp(scale, ViewScaleMin, ViewScaleMax);

            origin.X = RulerSize + (viewWidth - LengthWorldToGui(width)) * 0.5f - LengthWorldToGui(x);
            origin.Y = (viewHeight + LengthWorldToGui(height)) * 0.5f + LengthWorldToGui(y);

            UpdateMatrix();

            if (redraw)
                Invalidate();
        }

        private void UpdateRulerCursor(Point? location)
        {
            if (rulerCursor == location)
                return;

            rulerCursor = location;
            Invalidate(new Rectangle(0, 0, RulerSize, Height));
            Invalidate(new Rectangle(0, Height - RulerSize, Width, RulerSize));
        }

        private void DrawRulers(Graphics g)
        {
            if (Width <= RulerSize || Height <= RulerSize || ViewScale <= 0)
                return;

            var step = RulerStep(ViewScale, out var divisions);
            var decimals = System.Math.Max(0, -(int)System.Math.Floor(System.Math.Log10(step) + 1e-9));

            DrawBottomRuler(g, step, divisions, decimals);
            DrawLeftRuler(g, step, divisions, decimals);
            g.FillRectangle(rulerBackBrush, 0, Height - RulerSize, RulerSize, RulerSize);
        }

        private float RulerTickLength(long index, int divisions)
        {
            if (index % divisions == 0)
                return RulerSize;
            return divisions % 2 == 0 && index % (divisions / 2) == 0 ? 8f : 4f;
        }

        private string RulerLabel(long index, double minor, int decimals)
        {
            var value = System.Math.Round(index * minor, decimals);
            if (value == 0)
                value = 0; // no "-0"
            return value.ToString("F" + decimals, CultureInfo.CurrentCulture);
        }

        private void DrawBottomRuler(Graphics g, double step, int divisions, int decimals)
        {
            var top = Height - RulerSize;
            var rect = new Rectangle(RulerSize, top, Width - RulerSize, RulerSize);
            g.FillRectangle(rulerBackBrush, rect);

            var state = g.Save();
            g.SetClip(rect);

            var minor = step / divisions;
            var first = (long)System.Math.Floor((RulerSize - origin.X) / ViewScale / minor);
            var last = (long)System.Math.Ceiling((Width - origin.X) / ViewScale / minor);
            for (var i = first; i <= last; i++)
            {
                var x = origin.X + (float)(i * minor * ViewScale);
                g.DrawLine(rulerTickPen, x, top, x, top + RulerTickLength(i, divisions));
                if (i % divisions == 0)
                    g.DrawString(RulerLabel(i, minor, decimals), rulerFont, rulerTextBrush, x + 1, top + 8);
            }

            if (rulerCursor is Point cursor)
                g.DrawLine(rulerCursorPen, cursor.X, top, cursor.X, Height);

            g.DrawLine(rulerTickPen, rect.Left, top, rect.Right, top);
            g.Restore(state);
        }

        private void DrawLeftRuler(Graphics g, double step, int divisions, int decimals)
        {
            var bottom = Height - RulerSize;
            var rect = new Rectangle(0, 0, RulerSize, bottom);
            g.FillRectangle(rulerBackBrush, rect);

            var state = g.Save();
            g.SetClip(rect);

            var minor = step / divisions;
            var first = (long)System.Math.Floor((origin.Y - bottom) / ViewScale / minor);
            var last = (long)System.Math.Ceiling(origin.Y / ViewScale / minor);
            var right = RulerSize - 1;
            for (var i = first; i <= last; i++)
            {
                var y = origin.Y - (float)(i * minor * ViewScale);
                g.DrawLine(rulerTickPen, right - RulerTickLength(i, divisions), y, right, y);
                if (i % divisions == 0)
                {
                    // Reads bottom to top, beside the tick.
                    var labelState = g.Save();
                    g.TranslateTransform(1, y - 1);
                    g.RotateTransform(-90);
                    g.DrawString(RulerLabel(i, minor, decimals), rulerFont, rulerTextBrush, 0, 0);
                    g.Restore(labelState);
                }
            }

            if (rulerCursor is Point cursor)
                g.DrawLine(rulerCursorPen, 0, cursor.Y, RulerSize, cursor.Y);

            g.DrawLine(rulerTickPen, right, 0, right, bottom);
            g.Restore(state);
        }

        private readonly Pen gridMinorPen = new Pen(Color.FromArgb(41, 49, 58));
        private readonly Pen gridMajorPen = new Pen(Color.FromArgb(50, 59, 69));

        /// <summary>
        /// Grid lines on the ruler ticks: faint at every division, stronger at each labeled
        /// tick, so the spacing follows the zoom.
        /// </summary>
        private void DrawGrid(Graphics g)
        {
            if (ViewScale <= 0)
                return;

            var step = RulerStep(ViewScale, out var divisions);
            var minor = step / divisions;
            var drawMinor = minor * ViewScale >= 8f;

            var firstX = (long)System.Math.Floor(-origin.X / ViewScale / minor);
            var lastX = (long)System.Math.Ceiling((Width - origin.X) / ViewScale / minor);
            for (var i = firstX; i <= lastX; i++)
            {
                var isMajor = i % divisions == 0;
                if (!isMajor && !drawMinor)
                    continue;
                var x = origin.X + (float)(i * minor * ViewScale);
                g.DrawLine(isMajor ? gridMajorPen : gridMinorPen, x, 0, x, Height);
            }

            var firstY = (long)System.Math.Floor((origin.Y - Height) / ViewScale / minor);
            var lastY = (long)System.Math.Ceiling(origin.Y / ViewScale / minor);
            for (var i = firstY; i <= lastY; i++)
            {
                var isMajor = i % divisions == 0;
                if (!isMajor && !drawMinor)
                    continue;
                var y = origin.Y - (float)(i * minor * ViewScale);
                g.DrawLine(isMajor ? gridMajorPen : gridMinorPen, 0, y, Width, y);
            }
        }

        private void DisposeRulers()
        {
            gridMinorPen.Dispose();
            gridMajorPen.Dispose();
            rulerFont.Dispose();
            rulerBackBrush.Dispose();
            rulerTextBrush.Dispose();
            rulerTickPen.Dispose();
            rulerCursorPen.Dispose();
        }
    }
}

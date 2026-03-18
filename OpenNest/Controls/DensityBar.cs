using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class DensityBar : Control
    {
        private static readonly Color TrackColor = Color.FromArgb(224, 224, 224);
        private static readonly Color LowColor = Color.FromArgb(245, 166, 35);
        private static readonly Color HighColor = Color.FromArgb(76, 175, 80);

        private double value;

        public DensityBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Size = new Size(60, 8);
        }

        public double Value
        {
            get => value;
            set
            {
                this.value = System.Math.Clamp(value, 0.0, 1.0);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Track background
            using var trackPath = CreateRoundedRect(rect, 4);
            using var trackBrush = new SolidBrush(TrackColor);
            g.FillPath(trackBrush, trackPath);

            // Fill
            var fillWidth = (int)(rect.Width * value);
            if (fillWidth > 0)
            {
                var fillRadius = System.Math.Min(4, fillWidth / 2);
                var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
                using var fillPath = CreateRoundedRect(fillRect, fillRadius);
                using var gradientBrush = new LinearGradientBrush(
                    new Point(rect.X, 0), new Point(rect.Right, 0),
                    LowColor, HighColor);
                g.FillPath(gradientBrush, fillPath);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

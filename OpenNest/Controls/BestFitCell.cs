using OpenNest.Engine.BestFit;
using OpenNest.Math;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class BestFitCell : PlateView
    {
        private string[] metadataLines;
        private Color? partColor;

        public BestFitResult Result { get; set; }

        public Color? PartColor
        {
            get => partColor;
            set => partColor = value;
        }

        public BestFitCell(ColorScheme colorScheme)
            : base(colorScheme)
        {
            DrawOrigin = false;
            DrawBounds = false;
            AllowPan = false;
            AllowSelect = false;
            AllowZoom = false;
            AllowDrop = false;
            Cursor = Cursors.Hand;

            Plate.PartAdded += (s, e) => ApplyPartColor();
        }

        public void SetMetadata(BestFitResult result, int rank)
        {
            Result = result;

            metadataLines = new[]
            {
                string.Format("#{0}  {1:F1}x{2:F1}  Area={3:F1}",
                    rank, result.BoundingHeight, result.BoundingWidth, result.RotatedArea),
                string.Format("Util={0:P1}  Rot={1:F1}\u00b0",
                    result.Utilization,
                    Angle.ToDegrees(result.OptimalRotation)),
                result.Keep ? "" : result.Reason
            };
        }

        protected override void OnResize(System.EventArgs e)
        {
            base.OnResize(e);

            if (Plate.Parts.Count > 0)
                ZoomToFit(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighSpeed;

            e.Graphics.TranslateTransform(origin.X, origin.Y);
            Renderer.DrawPlate(e.Graphics);
            Renderer.DrawParts(e.Graphics);
            e.Graphics.ResetTransform();

            PaintMetadata(e.Graphics);
        }

        private void ApplyPartColor()
        {
            if (!partColor.HasValue)
                return;

            foreach (var lp in parts)
                lp.Color = partColor.Value;
        }

        private void PaintMetadata(Graphics g)
        {
            if (metadataLines == null)
                return;

            var font = Font;
            var textColor = IsDarkBackground() ? Brushes.White : Brushes.Black;
            var lineHeight = font.GetHeight(g) + 1;
            var y = 2f;

            foreach (var line in metadataLines)
            {
                if (line.Length == 0)
                    continue;

                g.DrawString(line, font, textColor, 2, y);
                y += lineHeight;
            }
        }

        private bool IsDarkBackground()
        {
            var bg = ColorScheme.BackgroundColor;
            var brightness = bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114;
            return brightness < 128;
        }
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.Engine.BestFit;
using OpenNest.Math;

namespace OpenNest.Controls
{
    public class BestFitCell : PlateView
    {
        private string[] metadataLines;

        public BestFitResult Result { get; set; }

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
        }

        public void SetMetadata(BestFitResult result, int rank)
        {
            Result = result;

            metadataLines = new[]
            {
                string.Format("#{0}  {1:F1}x{2:F1}  Area={3:F1}",
                    rank, result.BoundingWidth, result.BoundingHeight, result.RotatedArea),
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
            DrawPlate(e.Graphics);
            DrawParts(e.Graphics);
            e.Graphics.ResetTransform();

            PaintMetadata(e.Graphics);
        }

        private void PaintMetadata(Graphics g)
        {
            if (metadataLines == null)
                return;

            var font = Font;
            var brush = Brushes.White;
            var lineHeight = font.GetHeight(g) + 1;
            var y = 2f;

            foreach (var line in metadataLines)
            {
                if (line.Length == 0)
                    continue;

                g.DrawString(line, font, brush, 2, y);
                y += lineHeight;
            }
        }
    }
}

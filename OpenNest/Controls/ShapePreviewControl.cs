using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class ShapePreviewControl : PlateView
    {
        private string[] infoLines;

        public ShapePreviewControl()
        {
            DrawOrigin = false;
            DrawBounds = false;
            AllowPan = false;
            AllowSelect = false;
            AllowZoom = false;
            AllowDrop = false;
            BackColor = Color.White;
        }

        public void SetInfo(params string[] lines)
        {
            infoLines = lines;
            Invalidate();
        }

        public void ShowDrawing(Drawing drawing)
        {
            Plate.Parts.Clear();
            Plate.Size = new Geometry.Size(0, 0);

            if (drawing?.Program != null)
            {
                AddPartFromDrawing(drawing, Geometry.Vector.Zero);
                ZoomToFit();
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnResize(System.EventArgs e)
        {
            base.OnResize(e);

            if (Plate.Parts.Count > 0)
                ZoomToFit(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            e.Graphics.TranslateTransform(origin.X, origin.Y);
            Renderer.DrawPlate(e.Graphics);
            Renderer.DrawParts(e.Graphics);
            e.Graphics.ResetTransform();

            PaintInfo(e.Graphics);
        }

        private void PaintInfo(Graphics g)
        {
            if (infoLines == null) return;

            var lineHeight = Font.GetHeight(g) + 1;
            var y = 4f;

            foreach (var line in infoLines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                g.DrawString(line, Font, Brushes.Black, 4, y);
                y += lineHeight;
            }
        }
    }
}

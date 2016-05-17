using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class QuadrantSelect : Control
    {
        private int quadrant;

        public QuadrantSelect()
        {
        }

        public int Quadrant
        {
            get { return quadrant; }
            set { quadrant = value <= 4 && value > 0 ? value : 1; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var midx = Width * 0.5f;
            var midy = Height * 0.5f;

            RectangleF rect;

            switch (Quadrant)
            {
                case 1:
                    rect = new RectangleF(midx, 0, midx - 1, midy);
                    break;

                case 2:
                    rect = new RectangleF(0, 0, midx, midy);
                    break;

                case 3:
                    rect = new RectangleF(0, midy, midx, midy - 1);
                    break;

                case 4:
                    rect = new RectangleF(midx, midy, midx - 1, midy - 1);
                    break;

                default:
                    return;
            }

            e.Graphics.DrawLine(Pens.Gray, midx, 0, midx, Height);
            e.Graphics.DrawLine(Pens.Gray, 0, midy, Width, midy);

            e.Graphics.FillRectangle(Brushes.LightSkyBlue, rect);
            e.Graphics.DrawRectangle(Pens.Blue, rect.X, rect.Y, rect.Width, rect.Height);

            e.Graphics.DrawString(
                Quadrant.ToString(),
                new Font(this.Font.FontFamily, 35, GraphicsUnit.Pixel),
                Brushes.White,
                rect,
                new StringFormat()
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                });
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            var midx = Width / 2;
            var midy = Height / 2;

            if (e.X < midx)
                Quadrant = e.Y < midy ? 2 : 3;
            else
                Quadrant = e.Y < midy ? 1 : 4;

            Invalidate();
        }
    }
}

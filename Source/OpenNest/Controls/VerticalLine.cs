using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class VerticalLine : Control
    {
        private readonly Pen lightPen;
        private readonly Pen darkPen;

        public VerticalLine()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);

            lightPen = new Pen(ProfessionalColors.SeparatorLight);
            darkPen = new Pen(ProfessionalColors.SeparatorDark);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float midpoint = Width * 0.5f;

            e.Graphics.DrawLine(darkPen, midpoint, 0, midpoint, Height);
            midpoint++;
            e.Graphics.DrawLine(lightPen, midpoint, 0, midpoint, Height);
        }
    }
}

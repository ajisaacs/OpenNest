using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class HorizontalLine : Control
    {
        private readonly Pen lightPen;
        private readonly Pen darkPen;

        public HorizontalLine()
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

            float midpoint = Height * 0.5f;

            e.Graphics.DrawLine(darkPen, 0, midpoint, Width, midpoint);
            midpoint++;
            e.Graphics.DrawLine(lightPen, 0, midpoint, Width, midpoint);
        }
    }
}

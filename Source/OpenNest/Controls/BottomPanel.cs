using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class BottomPanel : Panel
    {
        private readonly Pen lightPen;
        private readonly Pen darkPen;

        public BottomPanel()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.Height = 50;
            this.Dock = DockStyle.Bottom;

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

            e.Graphics.DrawLine(darkPen, 0, 0, Width, 0);
            e.Graphics.DrawLine(lightPen, 0, 1, Width, 1);
        }
    }
}

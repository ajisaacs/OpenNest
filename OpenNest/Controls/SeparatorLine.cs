using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    /// <summary>An etched separator: a dark line at the middle with a light line beside it.</summary>
    public abstract class SeparatorLine : Control
    {
        private readonly Pen lightPen;
        private readonly Pen darkPen;

        protected SeparatorLine()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);

            lightPen = new Pen(ProfessionalColors.SeparatorLight);
            darkPen = new Pen(ProfessionalColors.SeparatorDark);
        }

        /// <summary>The control's size across the line: Height for a horizontal line.</summary>
        protected abstract int Across { get; }

        /// <summary>Draws the full-length line at <paramref name="position"/> across it.</summary>
        protected abstract void DrawLine(Graphics graphics, Pen pen, float position);

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    lightPen.Dispose();
                    darkPen.Dispose();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float midpoint = Across * 0.5f;

            DrawLine(e.Graphics, darkPen, midpoint);
            midpoint++;
            DrawLine(e.Graphics, lightPen, midpoint);
        }
    }
}

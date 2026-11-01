using System.Drawing;

namespace OpenNest.Controls
{
    public class VerticalLine : SeparatorLine
    {
        protected override int Across => Width;

        protected override void DrawLine(Graphics graphics, Pen pen, float position) =>
            graphics.DrawLine(pen, position, 0, position, Height);
    }
}

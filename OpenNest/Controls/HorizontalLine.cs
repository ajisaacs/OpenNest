using System.Drawing;

namespace OpenNest.Controls
{
    public class HorizontalLine : SeparatorLine
    {
        protected override int Across => Height;

        protected override void DrawLine(Graphics graphics, Pen pen, float position) =>
            graphics.DrawLine(pen, 0, position, Width, position);
    }
}

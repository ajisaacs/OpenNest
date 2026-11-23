using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    /// <summary>Starting positions for copies slid into an area with a <see cref="Compactor"/> push.</summary>
    internal static class SlideStart
    {
        /// <summary>
        /// A copy of <paramref name="template"/> just beyond the area edge the slide starts from
        /// (one part spacing plus one unit clear of it), its lower bound across the slide at
        /// <paramref name="across"/>.
        /// </summary>
        /// <param name="slideAlongX">
        /// True when <paramref name="slide"/> is <see cref="PushDirection.Left"/> or
        /// <see cref="PushDirection.Right"/>: the copy's bottom goes to <paramref name="across"/>
        /// and it starts beyond the area's right or left edge. False for an up/down slide: its left
        /// goes to <paramref name="across"/> and it starts beyond the area's top or bottom edge.
        /// </param>
        internal static Part Beyond(
            Part template,
            Box area,
            double spacing,
            PushDirection slide,
            double across,
            bool slideAlongX
        )
        {
            var box = template.BoundingBox;
            var gap = spacing + 1.0;
            double dx, dy;
            if (slideAlongX)
            {
                dy = across - box.Bottom;
                dx = slide == PushDirection.Left
                    ? area.Right + gap - box.Left
                    : area.Left - gap - box.Right;
            }
            else
            {
                dx = across - box.Left;
                dy = slide == PushDirection.Down
                    ? area.Top + gap - box.Bottom
                    : area.Bottom - gap - box.Top;
            }

            var copy = template.CloneAtOffset(new Vector(dx, dy));
            copy.UpdateBounds();
            return copy;
        }
    }
}

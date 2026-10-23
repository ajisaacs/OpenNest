using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to minimize Y-extent (preserve top-side horizontal remnant).
    /// Tiebreak chain: count > smallest Y-extent > highest density.
    /// </summary>
    public class HorizontalRemnantComparer : RemnantComparer
    {
        protected override double Low(Box box) => box.Bottom;

        protected override double High(Box box) => box.Top;
    }
}

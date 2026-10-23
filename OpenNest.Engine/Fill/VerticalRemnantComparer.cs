using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to minimize X-extent (preserve right-side vertical remnant).
    /// Tiebreak chain: count > smallest X-extent > highest density.
    /// </summary>
    public class VerticalRemnantComparer : RemnantComparer
    {
        protected override double Low(Box box) => box.Left;

        protected override double High(Box box) => box.Right;
    }
}

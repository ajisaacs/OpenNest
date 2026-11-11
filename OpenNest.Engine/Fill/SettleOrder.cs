namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// How <see cref="Compactor.SettlePlacement"/> orders its pushes. Directions follow
    /// the plate's origin quadrant: "horizontal" is toward the origin's side, as is "vertical".
    /// </summary>
    public enum SettleOrder
    {
        /// <summary>Try both orders and keep the placement closest to the origin corner.</summary>
        ClosestToCorner,

        /// <summary>Push horizontally first, then vertically, repeating until settled.</summary>
        HorizontalFirst,

        /// <summary>Push vertically first, then horizontally, repeating until settled.</summary>
        VerticalFirst,
    }
}

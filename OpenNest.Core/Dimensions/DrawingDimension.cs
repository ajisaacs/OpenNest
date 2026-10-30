namespace OpenNest.Dimensions
{
    public enum DimensionKind
    {
        /// <summary>The part's overall size along X, drawn above or below it.</summary>
        OverallHorizontal,

        /// <summary>The part's overall size along Y, drawn right or left of it.</summary>
        OverallVertical,
    }

    /// <summary>
    /// An annotation on a drawing. Only its placement is stored: the value is measured from the
    /// geometry each time it is resolved, so it cannot go stale. It never affects the program.
    /// </summary>
    public sealed class DrawingDimension
    {
        public DimensionKind Kind { get; set; }

        /// <summary>
        /// Distance from the part's extents to the dimension line; never zero. Positive is above a
        /// horizontal dimension's part or right of a vertical one's, negative the opposite side.
        /// </summary>
        public double Offset { get; set; }

        public DrawingDimension Clone() => new() { Kind = Kind, Offset = Offset };
    }
}

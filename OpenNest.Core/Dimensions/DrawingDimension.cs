using System;

namespace OpenNest.Dimensions
{
    public enum DimensionKind
    {
        /// <summary>The part's overall size along X, drawn above or below it.</summary>
        OverallHorizontal,

        /// <summary>The part's overall size along Y, drawn right or left of it.</summary>
        OverallVertical,

        /// <summary>A circle's or arc's radius, on a leader from its center.</summary>
        Radius,

        /// <summary>A circle's or arc's diameter, on a leader across it.</summary>
        Diameter,
    }

    /// <summary>
    /// An annotation on a drawing. Only its placement is stored: the value is measured from the
    /// geometry each time it is resolved, so it cannot go stale. It never affects the program.
    /// </summary>
    public sealed class DrawingDimension
    {
        public DimensionKind Kind { get; set; }

        /// <summary>
        /// Never zero. Overall dimensions: distance from the part's extents to the dimension line,
        /// positive above a horizontal dimension's part or right of a vertical one's. Radius and
        /// diameter: distance from the curve to the label along the leader, positive outside.
        /// </summary>
        public double Offset { get; set; }

        /// <summary>The circle or arc a radius or diameter measures; empty for overall dimensions.</summary>
        public Guid EntityId { get; set; }

        /// <summary>Radius and diameter: the leader's direction from the center, in radians.</summary>
        public double Angle { get; set; }

        public bool IsRadial => IsRadialKind(Kind);

        public static bool IsRadialKind(DimensionKind kind) =>
            kind is DimensionKind.Radius or DimensionKind.Diameter;

        public DrawingDimension Clone() => new() { Kind = Kind, Offset = Offset, EntityId = EntityId, Angle = Angle };
    }
}

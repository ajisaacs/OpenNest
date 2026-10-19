using System;

namespace OpenNest
{
    /// <summary>
    /// The sheet edge a <see cref="ClampRail"/> clamp sits on, named relative to the plate
    /// origin so one rail resolves onto every sheet size and quadrant.
    /// </summary>
    public enum ClampEdge
    {
        /// <summary>The edge along X through the origin (operator side).</summary>
        OriginSide = 0,

        /// <summary>The edge along X across the sheet's width from the origin.</summary>
        FarSide = 1,

        /// <summary>The edge along Y through the origin.</summary>
        OriginEnd = 2,

        /// <summary>The edge along Y across the sheet's length from the origin.</summary>
        FarEnd = 3,
    }

    public static class ClampEdges
    {
        /// <summary>True for the two edges along X, whose clamp positions are X distances.</summary>
        public static bool RunsAlongX(this ClampEdge edge) =>
            edge is ClampEdge.OriginSide or ClampEdge.FarSide;

        public static bool IsDefined(ClampEdge edge) =>
            edge is ClampEdge.OriginSide or ClampEdge.FarSide or ClampEdge.OriginEnd or ClampEdge.FarEnd;
    }

    /// <summary>
    /// One clamp of a <see cref="ClampRail"/>: its edge and its center measured from the plate
    /// origin along that edge (X for the sides, Y for the ends). Orders by edge, then center.
    /// </summary>
    public readonly record struct RailClamp(ClampEdge Edge, double Center) : IComparable<RailClamp>
    {
        public int CompareTo(RailClamp other)
        {
            var edge = Edge.CompareTo(other.Edge);
            return edge != 0 ? edge : Center.CompareTo(other.Center);
        }
    }
}

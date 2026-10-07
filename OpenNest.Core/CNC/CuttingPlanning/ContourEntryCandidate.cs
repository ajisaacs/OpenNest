using System;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Automatic start preference, ordered: a lower value is more preferred.</summary>
internal enum AutomaticEntryKind
{
    /// <summary>A convex turn of the contour's own travel, classified exactly as emission classifies it.</summary>
    ConvexCorner = 0,

    /// <summary>The midpoint of a straight edge.</summary>
    StraightMidpoint = 1,

    /// <summary>A tangent line/arc joint. A collinear line/line split is not a joint and never appears.</summary>
    TangentJoint = 2,

    /// <summary>
    /// Tier 3 (fallback): a point on a straight edge meeting a convex corner, back from the
    /// corner by about twice the applicable lead-in length, strictly inside the edge.
    /// </summary>
    NearCorner = 3,

    /// <summary>
    /// Tier 3 (fallback): the exact native closest point facing the caller's look-ahead
    /// position (pass the arrival there when there is no next cut). Never a reflex/cusp vertex.
    /// </summary>
    TargetFacing = 4,

    /// <summary>Tier 3 (fallback): the native midpoint of an arc entity.</summary>
    ArcMidpoint = 5,

    /// <summary>Tier 3 (fallback): one of the eight compass points of a whole circle.</summary>
    CircleCompass = 6,
}

/// <summary>
/// One automatic start candidate: the owned contour choice plus its preference kind and a
/// stable geometry tie key for deterministic ranking. The choice keeps this preparation as
/// owner; nothing here exposes or mutates the underlying shape.
/// </summary>
internal sealed record ContourEntryCandidate(ContourChoice Choice, AutomaticEntryKind Kind)
{
    /// <summary>
    /// Stable geometric tie key: the point quantized to the preparation epsilon grid, so
    /// equal points rank together regardless of the entity that produced them.
    /// </summary>
    internal (long X, long Y) GeometryKey => (Quantize(Choice.Point.X), Quantize(Choice.Point.Y));

    private static long Quantize(double value) =>
        (long)System.Math.Round(value / PostVerificationGeometry.Epsilon);
}

using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest;

/// <summary>
/// Defines a split line at a position along an axis.
/// For Vertical, Position is the X coordinate. For Horizontal, Position is the Y coordinate.
/// </summary>
public class SplitLine
{
    public double Position { get; }
    public CutOffAxis Axis { get; }

    /// <summary>
    /// Optional custom center positions for features (tabs/spikes) along the split line.
    /// Values are absolute coordinates on the perpendicular axis.
    /// When empty, feature generators use their default even spacing.
    /// </summary>
    public List<double> FeaturePositions { get; set; } = new();

    public SplitLine(double position, CutOffAxis axis)
    {
        Position = position;
        Axis = axis;
    }

    /// <summary>
    /// Returns a Line entity at the split position spanning the given extent range.
    /// For Vertical: line from (Position, extentStart) to (Position, extentEnd).
    /// For Horizontal: line from (extentStart, Position) to (extentEnd, Position).
    /// </summary>
    public Line ToLine(double extentStart, double extentEnd)
    {
        return Axis == CutOffAxis.Vertical
            ? new Line(Position, extentStart, Position, extentEnd)
            : new Line(extentStart, Position, extentEnd, Position);
    }
}

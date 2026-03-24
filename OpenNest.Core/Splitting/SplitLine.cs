namespace OpenNest;

/// <summary>
/// Defines a split line at a position along an axis.
/// For Vertical, Position is the X coordinate. For Horizontal, Position is the Y coordinate.
/// </summary>
public class SplitLine
{
    public double Position { get; }
    public CutOffAxis Axis { get; }

    public SplitLine(double position, CutOffAxis axis)
    {
        Position = position;
        Axis = axis;
    }
}

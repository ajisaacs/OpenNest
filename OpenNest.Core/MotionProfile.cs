namespace OpenNest;

/// <summary>
/// A machine's motion limits for cut-time estimates, in the units of the
/// <see cref="CutParameters"/> it is attached to (inches or millimeters, per second squared).
/// </summary>
public class MotionProfile
{
    /// <summary>Acceleration while cutting or scribing, and the centripetal limit on arcs.</summary>
    public double CutAcceleration { get; set; }

    /// <summary>Acceleration for head-up travel.</summary>
    public double RapidAcceleration { get; set; }

    /// <summary>
    /// Junction deviation: how far the path may round a corner, which sets how fast the head
    /// may take it. Zero stops at every corner.
    /// </summary>
    public double CornerDeviation { get; set; }
}

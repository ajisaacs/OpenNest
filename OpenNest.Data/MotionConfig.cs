namespace OpenNest.Data;

/// <summary>
/// A machine's acceleration limits for cut-time estimates, in the machine's units
/// (in/s² or mm/s², and in or mm for the corner deviation).
/// </summary>
public class MotionConfig
{
    private const double MillimetersPerInch = 25.4;

    public double CutAcceleration { get; set; }
    public double RapidAcceleration { get; set; }
    public double CornerDeviation { get; set; }

    /// <summary>
    /// Stand-in limits for a machine whose controller values are not on file yet: about
    /// 0.5 G cutting, 1 G travel and a 0.001 in (0.025 mm) corner deviation. Replace them
    /// with the controller's own settings.
    /// </summary>
    public static MotionConfig Placeholder(UnitSystem units) =>
        units == UnitSystem.Millimeters
            ? new MotionConfig { CutAcceleration = 4900, RapidAcceleration = 9800, CornerDeviation = 0.025 }
            : new MotionConfig { CutAcceleration = 193, RapidAcceleration = 386, CornerDeviation = 0.001 };

    /// <summary>These limits converted from <paramref name="from"/> to the nest's <paramref name="to"/> units.</summary>
    public MotionProfile ToProfile(UnitSystem from, Units to)
    {
        var scale = (from, to) switch
        {
            (UnitSystem.Inches, Units.Millimeters) => MillimetersPerInch,
            (UnitSystem.Millimeters, Units.Inches) => 1 / MillimetersPerInch,
            _ => 1,
        };

        return new MotionProfile
        {
            CutAcceleration = CutAcceleration * scale,
            RapidAcceleration = RapidAcceleration * scale,
            CornerDeviation = CornerDeviation * scale,
        };
    }
}

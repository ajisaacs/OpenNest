using System;

namespace OpenNest;

public class CutParameters
{
    public double Feedrate { get; set; }
    public double RapidTravelRate { get; set; }
    public TimeSpan PierceTime { get; set; }
    public double LeadInLength { get; set; }
    public string PostProcessor { get; set; }
    public Units Units { get; set; }

    /// <summary>
    /// Acceleration limits; null estimates every move at full speed. Applied by
    /// <see cref="CNC.CutSimulation"/> and the plate/nest overloads of <see cref="Timing.CalculateTime(Plate, CutParameters)"/>.
    /// </summary>
    public MotionProfile Motion { get; set; }

    /// <summary>Feed for interior contours (holes); null or zero uses <see cref="Feedrate"/>.</summary>
    public double? HoleFeedrate { get; set; }

    /// <summary>Leave out contours that only scribe/etch, as the CI Fiber post does by default.</summary>
    public bool SkipScribe { get; set; }

    public static CutParameters Default =>
        new()
        {
            Feedrate = 100,
            RapidTravelRate = 300,
            PierceTime = TimeSpan.FromSeconds(0.5),
            Units = OpenNest.Units.Inches,
        };
}

using System;

namespace OpenNest.Api;

public class CutParameters
{
    public double Feedrate { get; set; }
    public double RapidTravelRate { get; set; }
    public TimeSpan PierceTime { get; set; }
    public double LeadInLength { get; set; }
    public string PostProcessor { get; set; }
    public Units Units { get; set; }

    public static CutParameters Default => new()
    {
        Feedrate = 100,
        RapidTravelRate = 300,
        PierceTime = TimeSpan.FromSeconds(0.5),
        Units = OpenNest.Units.Inches
    };
}

using System;

namespace OpenNest
{
    public class CutParameters
    {
        public double Feedrate { get; set; }

        public double RapidTravelRate { get; set; }

        public TimeSpan PierceTime { get; set; }

        public Units Units { get; set; }
    }
}

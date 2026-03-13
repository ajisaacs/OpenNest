using System.Collections.Generic;

namespace OpenNest
{
    public enum NestPhase
    {
        Linear,
        RectBestFit,
        Pairs,
        Nfp,
        Remainder
    }

    public class NestProgress
    {
        public NestPhase Phase { get; set; }
        public int PlateNumber { get; set; }
        public int BestPartCount { get; set; }
        public double BestDensity { get; set; }
        public double UsableRemnantArea { get; set; }
        public List<Part> BestParts { get; set; }
    }
}

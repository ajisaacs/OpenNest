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

    public class PhaseResult
    {
        public NestPhase Phase { get; set; }
        public int PartCount { get; set; }
        public long TimeMs { get; set; }

        public PhaseResult(NestPhase phase, int partCount, long timeMs)
        {
            Phase = phase;
            PartCount = partCount;
            TimeMs = timeMs;
        }
    }

    public class AngleResult
    {
        public double AngleDeg { get; set; }
        public NestDirection Direction { get; set; }
        public int PartCount { get; set; }
    }

    public class NestProgress
    {
        public NestPhase Phase { get; set; }
        public int PlateNumber { get; set; }
        public int BestPartCount { get; set; }
        public double BestDensity { get; set; }
        public double NestedWidth { get; set; }
        public double NestedLength { get; set; }
        public double NestedArea { get; set; }
        public double UsableRemnantArea { get; set; }
        public List<Part> BestParts { get; set; }
        public string Description { get; set; }
    }
}

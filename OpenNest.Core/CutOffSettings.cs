namespace OpenNest
{
    public enum CutDirection
    {
        TowardOrigin,
        AwayFromOrigin
    }

    public class CutOffSettings
    {
        public double PartClearance { get; set; } = 0.125;
        public double Overtravel { get; set; }
        public double MinSegmentLength { get; set; } = 0.05;
        public CutDirection CutDirection { get; set; } = CutDirection.AwayFromOrigin;
    }
}

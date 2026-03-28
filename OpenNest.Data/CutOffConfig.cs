namespace OpenNest.Data;

public class CutOffConfig
{
    public double PartClearance { get; set; } = 0.02;
    public double Overtravel { get; set; }
    public double MinSegmentLength { get; set; } = 0.05;
    public string Direction { get; set; } = "AwayFromOrigin";
}

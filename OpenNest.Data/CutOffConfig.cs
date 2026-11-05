namespace OpenNest.Data;

public class CutOffConfig
{
    public double PartClearance { get; set; } = 0.02;
    public double Overtravel { get; set; }
    public double MinSegmentLength { get; set; } = 0.05;
    public string Direction { get; set; } = "AwayFromOrigin";

    /// <summary>Fields a newer OpenNest wrote, kept so saving here does not drop them.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}

namespace OpenNest.Data;

public class LeadConfig
{
    public string Type { get; set; } = "Line";
    public double Length { get; set; }
    public double Angle { get; set; } = 90.0;
    public double Radius { get; set; }

    /// <summary>Fields a newer OpenNest wrote, kept so saving here does not drop them.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}

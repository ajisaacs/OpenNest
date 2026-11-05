namespace OpenNest.Data;

public class MaterialConfig
{
    public string Name { get; set; } = "";
    public string Grade { get; set; } = "";
    public double Density { get; set; }
    public List<ThicknessConfig> Thicknesses { get; set; } = new();

    /// <summary>Fields a newer OpenNest wrote, kept so saving here does not drop them.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}

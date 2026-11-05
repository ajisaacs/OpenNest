namespace OpenNest.Data;

/// <summary>A thickness's distance kept from each sheet edge, in the machine's units.</summary>
public class EdgeSpacingConfig
{
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }

    /// <summary>Fields a newer OpenNest wrote, kept so saving here does not drop them.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}

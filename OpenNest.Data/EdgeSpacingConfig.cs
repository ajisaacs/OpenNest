namespace OpenNest.Data;

/// <summary>A thickness's distance kept from each sheet edge, in the machine's units.</summary>
public class EdgeSpacingConfig
{
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
}

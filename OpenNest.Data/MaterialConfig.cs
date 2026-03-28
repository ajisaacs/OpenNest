namespace OpenNest.Data;

public class MaterialConfig
{
    public string Name { get; set; } = "";
    public string Grade { get; set; } = "";
    public double Density { get; set; }
    public List<ThicknessConfig> Thicknesses { get; set; } = new();
}

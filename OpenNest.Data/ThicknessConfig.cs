using System.Collections.Generic;

namespace OpenNest.Data;

public class ThicknessConfig
{
    public double Value { get; set; }
    public double Kerf { get; set; }
    public string AssistGas { get; set; } = "";
    public LeadConfig LeadIn { get; set; } = new();
    public LeadConfig LeadOut { get; set; } = new();
    public CutOffConfig CutOff { get; set; } = new();
    public List<string> PlateSizes { get; set; } = new();
}

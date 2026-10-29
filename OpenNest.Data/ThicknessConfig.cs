using System.Collections.Generic;

namespace OpenNest.Data;

public class ThicknessConfig
{
    public double Value { get; set; }

    /// <summary>Optional display name for the thickness, such as a gauge ("16 ga").</summary>
    public string Name { get; set; } = "";

    public double Kerf { get; set; }
    public string AssistGas { get; set; } = "";

    /// <summary>The lead-in of outside cuts (part perimeters), and of inside cuts while <see cref="InsideLeadIn"/> is null.</summary>
    public LeadConfig LeadIn { get; set; } = new();

    /// <summary>The lead-out of outside cuts, and of inside cuts while <see cref="InsideLeadOut"/> is null.</summary>
    public LeadConfig LeadOut { get; set; } = new();

    /// <summary>The lead-in of inside cuts (cutouts and holes); null uses <see cref="LeadIn"/>.</summary>
    public LeadConfig? InsideLeadIn { get; set; }

    /// <summary>The lead-out of inside cuts; null uses <see cref="LeadOut"/>.</summary>
    public LeadConfig? InsideLeadOut { get; set; }

    public CutOffConfig CutOff { get; set; } = new();
    public List<string> PlateSizes { get; set; } = new();
}

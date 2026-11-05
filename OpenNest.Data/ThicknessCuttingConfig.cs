using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

/// <summary>
/// The Cutting Parameters dialog's settings beyond leads for one thickness: lead-in angle
/// rounding, tabs and pierce. Lengths are in the machine's units. A thickness without them
/// (<see cref="ThicknessConfig.Cutting"/> null) takes this PC's last cutting settings.
/// </summary>
public class ThicknessCuttingConfig
{
    public bool RoundLeadInAngles { get; set; }
    public double LeadInAngleIncrement { get; set; } = 5;

    /// <summary>Whether parts are tabbed at all: by the support grid, or by size with <see cref="TabBySize"/>.</summary>
    public bool Tabs { get; set; }

    /// <summary>With <see cref="Tabs"/>, ignore the support grid and tab parts whose narrow side is in the size range.</summary>
    public bool TabBySize { get; set; }

    public double TabSize { get; set; } = 0.03;

    /// <summary>The longest part its tab alone holds; 0 turns the rule off.</summary>
    public double TabHeldMaxLength { get; set; } = 6;

    public double TabSizeMinWidth { get; set; }

    /// <summary>The widest part tabbed by size; 0 tabs nothing by size.</summary>
    public double TabSizeMaxWidth { get; set; }

    public double PierceClearance { get; set; } = 0.0625;

    /// <summary>The pierce zone's diameter; 0 turns it off.</summary>
    public double PierceDiameter { get; set; }

    /// <summary>Fields a newer OpenNest wrote, kept so saving here does not drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

using System;
using System.Collections.Generic;

namespace OpenNest.Desktop.Rendering;

/// <summary>
/// One plate view's View menu toggles, with the WinForms defaults: rapids off, bounds on, and
/// pierce points, offset outline, cut direction and support off. Only rapids and bounds are kept
/// between sessions, under the WinForms names <c>PlateViewDrawRapid</c> and <c>PlateViewDrawBounds</c>:
/// each nest view reads them when it opens and writes its own when it closes.
/// </summary>
public sealed class PlateViewOptions
{
    public const string DrawRapidName = "PlateViewDrawRapid";
    public const string DrawBoundsName = "PlateViewDrawBounds";

    /// <summary>The origin axes through world (0, 0).</summary>
    public bool DrawOrigin { get; set; } = true;

    public bool DrawRapid { get; set; }

    public bool DrawPiercePoints { get; set; }

    /// <summary>The box around the selected parts.</summary>
    public bool DrawBounds { get; set; } = true;

    /// <summary>Each part's outline offset by the plate's part spacing.</summary>
    public bool DrawOffset { get; set; }

    public bool DrawCutDirection { get; set; }

    /// <summary>The support bed, each part's contacts and center of gravity.</summary>
    public bool DrawSupport { get; set; }

    /// <summary>The chord tolerance of the offset outline.</summary>
    public double OffsetTolerance { get; set; } = 0.001;

    /// <summary>A view's toggles with the saved rapids and bounds; missing or unreadable values are defaults.</summary>
    public static PlateViewOptions Read(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = new PlateViewOptions();
        options.DrawRapid = UserSettingText.ReadBool(settings, DrawRapidName, options.DrawRapid);
        options.DrawBounds = UserSettingText.ReadBool(settings, DrawBoundsName, options.DrawBounds);
        return options;
    }

    /// <summary>Stores the rapids and bounds toggles in the WinForms format.</summary>
    public void WriteTo(IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings[DrawRapidName] = UserSettingText.Format(DrawRapid);
        settings[DrawBoundsName] = UserSettingText.Format(DrawBounds);
    }
}

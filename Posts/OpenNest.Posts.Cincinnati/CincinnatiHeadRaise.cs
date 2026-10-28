using OpenNest.CNC;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Chooses the M47 head raise (§3.47) written before the rapid that leaves a feature.
/// </summary>
internal static class CincinnatiHeadRaise
{
    /// <param name="isExterior">True when the feature just cut is a part exterior.</param>
    /// <param name="rapidDistance">Travel to the next pierce; null when unknown.</param>
    /// <param name="raised">
    /// True when Plan Cutting marked the rapid to raise the head over parts already cut
    /// (<see cref="HeadRaise"/>): it always gets a raise the operator cannot skip.
    /// </param>
    /// <returns>The block to write, or null for no head raise.</returns>
    internal static string Line(CincinnatiPostConfig config, bool isExterior, double? rapidDistance,
        bool raised = false)
    {
        if (IsSafetyRapid(config, rapidDistance))
            return SafetyLine(config);
        if (raised)
            return "M47";

        var mode = isExterior ? config.ExteriorM47 : config.InteriorM47;
        return mode switch
        {
            M47Mode.Always => "M47",
            M47Mode.BlockDelete => "/M47",
            M47Mode.Auto => IsLongRapid(config, rapidDistance) ? "M47" : "/M47",
            _ => null,
        };
    }

    /// <summary>
    /// True when the rapid is long enough to cross warped plate, so it takes the
    /// safety head raise. Unknown travel never does.
    /// </summary>
    internal static bool IsSafetyRapid(CincinnatiPostConfig config, double? rapidDistance) =>
        config.SafetyHeadraiseDistance.HasValue
        && config.SafetyHeadraiseRapidDistance is { } threshold
        && rapidDistance is { } distance
        && distance > threshold;

    internal static string SafetyLine(CincinnatiPostConfig config) =>
        $"M47 P{config.SafetyHeadraiseDistance.Value} (Safety Headraise)";

    // Unknown travel, or no threshold configured, always gets the full raise.
    private static bool IsLongRapid(CincinnatiPostConfig config, double? rapidDistance) =>
        config.M47OverrideDistanceThreshold is not { } threshold
        || rapidDistance is not { } distance
        || distance > threshold;
}

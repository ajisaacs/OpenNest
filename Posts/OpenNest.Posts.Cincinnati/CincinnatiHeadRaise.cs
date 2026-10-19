namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Chooses the M47 head raise (§3.47) written before the rapid that leaves a feature.
/// </summary>
internal static class CincinnatiHeadRaise
{
    /// <param name="isSafetyHeadraise">True when the rapid goes to a different part.</param>
    /// <param name="isExterior">True when the feature just cut is a part exterior.</param>
    /// <param name="rapidDistance">Travel to the next pierce; null when unknown.</param>
    /// <returns>The block to write, or null for no head raise.</returns>
    internal static string Line(
        CincinnatiPostConfig config,
        bool isSafetyHeadraise,
        bool isExterior,
        double? rapidDistance
    )
    {
        if (isSafetyHeadraise && config.SafetyHeadraiseDistance.HasValue)
            return $"M47 P{config.SafetyHeadraiseDistance.Value} (Safety Headraise)";

        var mode = isExterior ? config.ExteriorM47 : config.InteriorM47;
        return mode switch
        {
            M47Mode.Always => "M47",
            M47Mode.BlockDelete => "/M47",
            M47Mode.Auto => IsLongRapid(config, rapidDistance) ? "M47" : "/M47",
            _ => null,
        };
    }

    // Unknown travel, or no threshold configured, always gets the full raise.
    private static bool IsLongRapid(CincinnatiPostConfig config, double? rapidDistance) =>
        config.M47OverrideDistanceThreshold is not { } threshold
        || rapidDistance is not { } distance
        || distance > threshold;
}

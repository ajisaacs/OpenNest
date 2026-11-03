namespace OpenNest;

/// <summary>
/// Nesting priority places higher numbers first. Files, the drawing library and server job
/// documents written before that stored a lower-first value under <c>priority</c>; they keep
/// writing it beside the new value so older readers see the same order. Converting negates,
/// which keeps the order and is its own inverse.
/// </summary>
public static class LegacyPriority
{
    /// <summary>Converts between a higher-first priority and its lower-first legacy value, either way.</summary>
    public static int Convert(int priority) => priority == int.MinValue ? int.MaxValue : -priority;
}

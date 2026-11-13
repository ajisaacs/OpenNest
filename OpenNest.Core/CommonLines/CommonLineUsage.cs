namespace OpenNest.CommonLines
{
    /// <summary>
    /// When automatic nesting and fills may place parts one kerf apart on a common line (see
    /// docs/common-line-cutting.md). Only drawings with <see cref="Drawing.AllowCommonLine"/> take part.
    /// </summary>
    public enum CommonLineUsage
    {
        /// <summary>Never: engines and fills behave exactly as without common lines.</summary>
        Off,

        /// <summary>
        /// Only when it places more parts (or, in a whole job, uses fewer sheets) than the best
        /// layout without common lines; a tie keeps the layout without them.
        /// </summary>
        WhenItAddsParts,

        /// <summary>
        /// Whenever it shortens the cut or uses less material, never at the cost of a part:
        /// part count still ranks first.
        /// </summary>
        Whenever,
    }
}

namespace OpenNest.CommonLines
{
    /// <summary>The names <see cref="CommonLineUsage"/> is saved under (nest files and defaults.json).</summary>
    public static class CommonLineUsageNames
    {
        /// <summary>The saved name, or null for <see cref="CommonLineUsage.Off"/> (omitted).</summary>
        public static string Format(CommonLineUsage usage) => usage switch
        {
            CommonLineUsage.WhenItAddsParts => "whenItAddsParts",
            CommonLineUsage.Whenever => "whenever",
            _ => null,
        };

        /// <summary>Reads a saved name case-insensitively; anything else is off.</summary>
        public static CommonLineUsage Parse(string name) =>
            string.Equals(name, "whenItAddsParts", System.StringComparison.OrdinalIgnoreCase)
                ? CommonLineUsage.WhenItAddsParts
                : string.Equals(name, "whenever", System.StringComparison.OrdinalIgnoreCase)
                    ? CommonLineUsage.Whenever
                    : CommonLineUsage.Off;
    }
}

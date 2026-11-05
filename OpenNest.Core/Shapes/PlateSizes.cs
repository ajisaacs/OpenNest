using System;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    /// <summary>
    /// Catalog of standard mill sheet sizes (inches). Uses the project-wide
    /// (Width, Length) convention where Width is the short dimension and
    /// Length is the long dimension.
    /// </summary>
    public static class PlateSizes
    {
        public readonly record struct Entry(string Label, double Width, double Length)
        {
            public double Area => Width * Length;

            /// <summary>
            /// Returns true if a part of the given dimensions fits within this entry
            /// in either orientation.
            /// </summary>
            public bool Fits(double width, double length) =>
                (width <= Width && length <= Length) || (width <= Length && length <= Width);
        }

        /// <summary>
        /// Standard mill sheet sizes (inches), sorted by area ascending.
        /// Canonical orientation: Width &lt;= Length.
        /// </summary>
        public static IReadOnlyList<Entry> All { get; } =
            new[]
            {
                new Entry("48x96", 48, 96), // 4608
                new Entry("48x120", 48, 120), // 5760
                new Entry("48x144", 48, 144), // 6912
                new Entry("60x120", 60, 120), // 7200
                new Entry("60x144", 60, 144), // 8640
                new Entry("72x120", 72, 120), // 8640
                new Entry("72x144", 72, 144), // 10368
                new Entry("96x240", 96, 240), // 23040
            };

        /// <summary>
        /// Looks up a standard size by label. Case-insensitive.
        /// </summary>
        public static bool TryGet(string label, out Entry entry)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                foreach (var candidate in All)
                {
                    if (string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }
    }
}

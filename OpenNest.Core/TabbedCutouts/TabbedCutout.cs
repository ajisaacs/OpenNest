using System;
using System.Collections.Generic;

namespace OpenNest.TabbedCutouts
{
    /// <summary>
    /// A cutout (a closed hole in a part) whose slug stays attached by uncut tabs. It is identified
    /// by the source entities of the hole's contour. Building the drawing cuts the contour as open
    /// pieces between the tabs, each entered from the slug with no lead-out, and keeps the tabs on
    /// the <see cref="SpecialLayers.Tab"/> layer so the hole still bounds material. All values are
    /// in the source frame.
    /// </summary>
    public sealed class TabbedCutout
    {
        public const double DefaultTabWidth = 0.0625;
        public const int DefaultTabCount = 2;
        public const int MinTabCount = 1;

        /// <summary>Source entity IDs of the hole's closed contour.</summary>
        public List<Guid> EdgeIds { get; set; } = new();

        /// <summary>Uncut length of each tab along the contour.</summary>
        public double TabWidth { get; set; } = DefaultTabWidth;

        /// <summary>
        /// Tab centers as distances along the contour, counterclockwise from its lowest-left
        /// vertex (the smallest X, then Y), ascending. Empty means <see cref="DefaultTabCount"/>
        /// evenly spaced.
        /// </summary>
        public List<double> TabCenters { get; set; } = new();

        public TabbedCutout Clone() =>
            new()
            {
                EdgeIds = new List<Guid>(EdgeIds),
                TabWidth = TabWidth,
                TabCenters = new List<double>(TabCenters),
            };
    }
}

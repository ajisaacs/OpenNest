using System;
using System.Collections.Generic;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// Scrap drawn on a part (for example a forming backgauge tab) that stays attached by tabs and
    /// is snapped off later. It is identified by its joining line: a chain of source entities whose
    /// ends touch the outline around part and scrap and which splits that region in two. By
    /// default the smaller side is the scrap. Building the drawing cuts the joining line as open
    /// break cuts between tabs, entered from the scrap side. All values are in the source frame.
    /// </summary>
    public sealed class Breakoff
    {
        public const double DefaultTabWidth = 0.0625;
        public const int DefaultTabCount = 4;
        public const int MinTabCount = 2;

        /// <summary>Source entity IDs of the joining line between part and scrap.</summary>
        public List<Guid> EdgeIds { get; set; } = new();

        /// <summary>True when the larger side is the scrap instead of the smaller one.</summary>
        public bool Flip { get; set; }

        /// <summary>Uncut length of each tab along the joining line.</summary>
        public double TabWidth { get; set; } = DefaultTabWidth;

        /// <summary>
        /// Tab centers as distances along the joining line from its start, ascending; the first
        /// and last tab sit at the ends. Empty means <see cref="DefaultTabCount"/> evenly spaced.
        /// </summary>
        public List<double> TabCenters { get; set; } = new();

        public Breakoff Clone() =>
            new()
            {
                EdgeIds = new List<Guid>(EdgeIds),
                Flip = Flip,
                TabWidth = TabWidth,
                TabCenters = new List<double>(TabCenters),
            };
    }
}

using System;
using System.Collections.Generic;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// A scrap piece drawn outside a part (for example a forming backgauge tab) that stays
    /// attached to it by tabs and is snapped off later. The breakoff's outside edges are an open
    /// chain of source entities whose ends touch the part outline; the outline section between
    /// those ends is the break line. Building the drawing merges the breakoff into the outline and
    /// cuts the break line as closed slots carved into the breakoff, leaving the tabs between them
    /// uncut, so every pierce lands in scrap. All values are in the drawing's source frame.
    /// </summary>
    public sealed class Breakoff
    {
        public const double DefaultTabWidth = 0.0625;
        public const double DefaultSlotWidth = 0.25;
        public const int DefaultTabCount = 4;
        public const int MinTabCount = 2;

        /// <summary>Source entity IDs of the breakoff's outside edges.</summary>
        public List<Guid> EdgeIds { get; set; } = new();

        /// <summary>Uncut length of each tab along the break line.</summary>
        public double TabWidth { get; set; } = DefaultTabWidth;

        /// <summary>How far each slot reaches into the breakoff from the break line.</summary>
        public double SlotWidth { get; set; } = DefaultSlotWidth;

        /// <summary>
        /// Tab centers as distances along the break line from its start, ascending. Empty means
        /// <see cref="DefaultTabCount"/> tabs spaced evenly with the first and last at the ends.
        /// </summary>
        public List<double> TabCenters { get; set; } = new();

        public Breakoff Clone() =>
            new()
            {
                EdgeIds = new List<Guid>(EdgeIds),
                TabWidth = TabWidth,
                SlotWidth = SlotWidth,
                TabCenters = new List<double>(TabCenters),
            };
    }
}

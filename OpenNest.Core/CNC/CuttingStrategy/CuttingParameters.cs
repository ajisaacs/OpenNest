namespace OpenNest.CNC.CuttingStrategy
{
    public class CuttingParameters
    {
        public int Id { get; set; }

        public string MachineName { get; set; }
        public string MaterialName { get; set; }
        public string Grade { get; set; }
        public double Thickness { get; set; }

        public double Kerf { get; set; }
        public double PartSpacing { get; set; }

        public LeadIn ExternalLeadIn { get; set; } = new NoLeadIn();
        public LeadOut ExternalLeadOut { get; set; } = new NoLeadOut();

        /// <summary>
        /// The lead-in of every cutout, full circles included; circles still get their own
        /// geometry handling (radius clamp, angle rounding) when the lead is emitted.
        /// </summary>
        public LeadIn InternalLeadIn { get; set; } =
            new LineLeadIn { Length = 0.125, ApproachAngle = 90 };
        public LeadOut InternalLeadOut { get; set; } = new NoLeadOut();

        public double PierceClearance { get; set; } = 0.0625;

        /// <summary>
        /// Diameter of the crater a pierce blows through this plate; 0 turns the pierce zone off.
        /// When set, every lead-in pierce must keep half of it from all part material.
        /// </summary>
        public double PierceDiameter { get; set; }

        /// <summary>The pierce zone's radius, or 0 when <see cref="PierceDiameter"/> is off.</summary>
        public double GetPierceZoneRadius() =>
            PierceDiameter > 0 && double.IsFinite(PierceDiameter) ? PierceDiameter / 2 : 0;

        /// <summary>The clearance a pierce keeps from its own contour: the larger setting wins.</summary>
        public double GetEffectivePierceClearance() => System.Math.Max(PierceClearance, GetPierceZoneRadius());

        public bool RoundLeadInAngles { get; set; }
        public double LeadInAngleIncrement { get; set; } = 5.0;

        public Tab TabConfig { get; set; }

        /// <summary>
        /// Whether a contour is cut with a tab at its entry. The cutting settings never set it for
        /// every part: Plan Cutting, Place Lead-in and drawing pins set it on the copies of the
        /// settings they give the parts they tab. Parameters recorded with it on replay as planned.
        /// </summary>
        public bool TabsEnabled { get; set; }

        /// <summary>
        /// Tabs the parts that need one (see docs/support-tabs.md): with <see cref="TabBySize"/>
        /// off, each part its plate's support bed cannot hold level once it is cut free, at the
        /// perimeter point that best keeps its center of gravity over the bed's contacts, or on a
        /// plate with no bed each part in the size range; with it on, each part in the size range.
        /// Needs <see cref="TabConfig"/> for the tab size.
        /// </summary>
        public bool SupportTabs { get; set; }

        /// <summary>
        /// With <see cref="SupportTabs"/>, ignore the support bed and tab every part whose width is
        /// in <see cref="TabSizeMinWidth"/> to <see cref="TabSizeMaxWidth"/>
        /// (<see cref="TabsBySizeAt"/>).
        /// </summary>
        public bool TabBySize { get; set; }

        /// <summary>
        /// The narrowest part width that is tabbed by size: with <see cref="TabBySize"/>, or with
        /// <see cref="SupportTabs"/> on a plate that has no support bed (see <see cref="TabsBySizeAt"/>).
        /// </summary>
        public double TabSizeMinWidth { get; set; }

        /// <summary>
        /// The widest part width that is tabbed by size; 0 turns the rule off, so nothing is
        /// tabbed by size.
        /// </summary>
        public double TabSizeMaxWidth { get; set; }

        /// <summary>Whether <see cref="TabSizeMinWidth"/> to <see cref="TabSizeMaxWidth"/> is a usable range.</summary>
        public bool HasTabSizeRange() =>
            TabSizeMaxWidth > 0 && double.IsFinite(TabSizeMaxWidth)
            && TabSizeMinWidth >= 0 && TabSizeMinWidth <= TabSizeMaxWidth;

        /// <summary>
        /// Whether a part <paramref name="width"/> wide (the short side of its minimum-area
        /// bounding rectangle) is tabbed by size.
        /// </summary>
        public bool TabsBySizeAt(double width) =>
            HasTabSizeRange()
            && width >= TabSizeMinWidth - Math.Tolerance.Epsilon
            && width <= TabSizeMaxWidth + Math.Tolerance.Epsilon;

        /// <summary>
        /// With <see cref="SupportTabs"/> on a plate with a support bed, the longest part (the
        /// long side of its outline's minimum-area bounding rectangle) its tab alone holds: such a
        /// part the bed cannot hold level is tabbed, and any tab clear of inside corners steadies
        /// it. 0 turns the rule off. New settings start at 6; settings saved without it read as 0.
        /// </summary>
        public double TabHeldMaxLength { get; set; } = 6;

        /// <summary>Whether a part <paramref name="length"/> long is held by its tab alone (see <see cref="TabHeldMaxLength"/>).</summary>
        public bool TabHoldsAt(double length) =>
            TabHeldMaxLength > 0 && double.IsFinite(TabHeldMaxLength)
            && length <= TabHeldMaxLength + Math.Tolerance.Epsilon;

        public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
        public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
    }
}

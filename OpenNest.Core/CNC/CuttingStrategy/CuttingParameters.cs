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
        public bool TabsEnabled { get; set; }

        /// <summary>
        /// Tabs each part its plate's support bed cannot hold level once it is cut free, at the
        /// perimeter point that best keeps its center of gravity over the bed's contacts (see
        /// docs/support-tabs.md). Needs <see cref="TabConfig"/> for the tab size.
        /// </summary>
        public bool SupportTabs { get; set; }

        /// <summary>
        /// With <see cref="SupportTabs"/> on a plate that has no support bed, the narrowest part
        /// width that is tabbed instead (see <see cref="TabsWithoutBedAt"/>).
        /// </summary>
        public double NoBedTabMinWidth { get; set; }

        /// <summary>
        /// With <see cref="SupportTabs"/> on a plate that has no support bed, the widest part
        /// width that is tabbed instead; 0 turns the rule off, so nothing is tabbed without a bed.
        /// </summary>
        public double NoBedTabMaxWidth { get; set; }

        /// <summary>Whether <see cref="NoBedTabMinWidth"/> to <see cref="NoBedTabMaxWidth"/> is a usable range.</summary>
        public bool HasNoBedTabRange() =>
            NoBedTabMaxWidth > 0 && double.IsFinite(NoBedTabMaxWidth)
            && NoBedTabMinWidth >= 0 && NoBedTabMinWidth <= NoBedTabMaxWidth;

        /// <summary>
        /// Whether a part <paramref name="width"/> wide (the short side of its minimum-area
        /// bounding rectangle) is tabbed when its plate has no support bed.
        /// </summary>
        public bool TabsWithoutBedAt(double width) =>
            HasNoBedTabRange()
            && width >= NoBedTabMinWidth - Math.Tolerance.Epsilon
            && width <= NoBedTabMaxWidth + Math.Tolerance.Epsilon;

        /// <summary>
        /// With <see cref="SupportTabs"/> on a plate with a support bed, the longest part (the
        /// long side of its outline's minimum-area bounding rectangle) its tab alone holds: such a
        /// part the bed cannot hold level is tabbed, and any tab clear of inside corners steadies
        /// it. 0 turns the rule off.
        /// </summary>
        public double TabHeldMaxLength { get; set; }

        /// <summary>Whether a part <paramref name="length"/> long is held by its tab alone (see <see cref="TabHeldMaxLength"/>).</summary>
        public bool TabHoldsAt(double length) =>
            TabHeldMaxLength > 0 && double.IsFinite(TabHeldMaxLength)
            && length <= TabHeldMaxLength + Math.Tolerance.Epsilon;

        public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
        public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
    }
}

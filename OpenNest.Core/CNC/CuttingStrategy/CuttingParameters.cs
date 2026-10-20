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

        public LeadIn InternalLeadIn { get; set; } =
            new LineLeadIn { Length = 0.125, ApproachAngle = 90 };
        public LeadOut InternalLeadOut { get; set; } = new NoLeadOut();

        public LeadIn ArcCircleLeadIn { get; set; } = new NoLeadIn();
        public LeadOut ArcCircleLeadOut { get; set; } = new NoLeadOut();

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

        public double AutoTabMinSize { get; set; }
        public double AutoTabMaxSize { get; set; }

        public Tab TabConfig { get; set; }
        public bool TabsEnabled { get; set; }

        /// <summary>
        /// Tabs each part its plate's support bed cannot hold level once it is cut free, at the
        /// perimeter point that best keeps its center of gravity over the bed's contacts (see
        /// docs/support-tabs.md). Needs <see cref="TabConfig"/> for the tab size.
        /// </summary>
        public bool SupportTabs { get; set; }

        public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
        public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
    }
}

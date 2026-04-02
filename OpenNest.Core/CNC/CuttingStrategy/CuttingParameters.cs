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

        public LeadIn InternalLeadIn { get; set; } = new LineLeadIn { Length = 0.125, ApproachAngle = 90 };
        public LeadOut InternalLeadOut { get; set; } = new NoLeadOut();

        public LeadIn ArcCircleLeadIn { get; set; } = new NoLeadIn();
        public LeadOut ArcCircleLeadOut { get; set; } = new NoLeadOut();

        public double PierceClearance { get; set; } = 0.0625;

        public double AutoTabMinSize { get; set; }
        public double AutoTabMaxSize { get; set; }

        public Tab TabConfig { get; set; }
        public bool TabsEnabled { get; set; }

        public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
        public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
    }
}

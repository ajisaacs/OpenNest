using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.IO;

/// <summary>
/// JSON-safe cutting parameters shared by nest files and desktop settings.
/// Lead and tab discriminators avoid serializing abstract domain types.
/// </summary>
public class CuttingParametersDto
{
    public int Id { get; set; }
    public string MachineName { get; set; }
    public string MaterialName { get; set; }
    public string Grade { get; set; }
    public double Thickness { get; set; }
    public double Kerf { get; set; }
    public double PartSpacing { get; set; }
    public LeadInDto ExternalLeadIn { get; set; }
    public LeadOutDto ExternalLeadOut { get; set; }
    public LeadInDto InternalLeadIn { get; set; }
    public LeadOutDto InternalLeadOut { get; set; }
    public LeadInDto ArcCircleLeadIn { get; set; }
    public LeadOutDto ArcCircleLeadOut { get; set; }
    public bool TabsEnabled { get; set; }

    // Legacy settings only stored tabWidth. A missing TabConfig uses that width;
    // Type = "None" explicitly records a null tab in a nest parameter snapshot.
    public double TabWidth { get; set; }
    public TabDto TabConfig { get; set; }
    public double PierceClearance { get; set; }
    public bool RoundLeadInAngles { get; set; }
    public double LeadInAngleIncrement { get; set; }
    public double AutoTabMinSize { get; set; }
    public double AutoTabMaxSize { get; set; }
    public SequenceDto Sequencing { get; set; } = new();
    public AssignmentDto Assignment { get; set; } = new();

    public class LeadInDto
    {
        public string Type { get; set; } = "None";
        public double Length { get; set; }
        public double ApproachAngle { get; set; }
        public double Radius { get; set; }
        public double LineLength { get; set; }
        public double ArcRadius { get; set; }
        public double Kerf { get; set; }
        public double Length1 { get; set; }
        public double Angle1 { get; set; }
        public double Length2 { get; set; }
        public double Angle2 { get; set; }
    }

    public class LeadOutDto
    {
        public string Type { get; set; } = "None";
        public double Length { get; set; }
        public double ApproachAngle { get; set; }
        public double Radius { get; set; }
        public double GapSize { get; set; }
    }

    public class TabDto
    {
        public string Type { get; set; } = "None";
        public double Size { get; set; }
        public LeadInDto TabLeadIn { get; set; }
        public LeadOutDto TabLeadOut { get; set; }
        public double CutoutMinWidth { get; set; }
        public double CutoutMinHeight { get; set; }
        public double CutoutMaxWidth { get; set; }
        public double CutoutMaxHeight { get; set; }
        public int MachineTabId { get; set; }
        public double BreakerDepth { get; set; }
        public double BreakerLeadInLength { get; set; }
        public double BreakerAngle { get; set; }
    }

    public class SequenceDto
    {
        public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
        public double SmallCutoutWidth { get; set; } = 1.5;
        public double SmallCutoutHeight { get; set; } = 1.5;
        public double MediumCutoutWidth { get; set; } = 8.0;
        public double MediumCutoutHeight { get; set; } = 8.0;
        public double DistanceMediumSmall { get; set; }
        public bool AlternateRowsColumns { get; set; } = true;
        public bool AlternateCutoutsWithinRowColumn { get; set; } = true;
        public double MinDistanceBetweenRowsColumns { get; set; } = 0.25;
    }

    public class AssignmentDto
    {
        public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
        public string Preference { get; set; } = "ILAT";
        public double MinGeometryLength { get; set; } = 0.01;
    }
}

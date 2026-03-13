namespace OpenNest.CNC.CuttingStrategy
{
    // Values match PEP Technology's numbering scheme (value 6 intentionally skipped)
    public enum SequenceMethod
    {
        RightSide = 1,
        LeastCode = 2,
        Advanced = 3,
        BottomSide = 4,
        EdgeStart = 5,
        LeftSide = 7,
        RightSideAlt = 8
    }

    public class SequenceParameters
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
}

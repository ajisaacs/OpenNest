namespace OpenNest.CNC.CuttingStrategy
{
    public class AssignmentParameters
    {
        public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
        public string Preference { get; set; } = "ILAT";
        public double MinGeometryLength { get; set; } = 0.01;
    }
}

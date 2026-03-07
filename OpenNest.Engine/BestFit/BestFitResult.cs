namespace OpenNest.Engine.BestFit
{
    public class BestFitResult
    {
        public PairCandidate Candidate { get; set; }
        public double RotatedArea { get; set; }
        public double BoundingWidth { get; set; }
        public double BoundingHeight { get; set; }
        public double OptimalRotation { get; set; }
        public bool Keep { get; set; }
        public string Reason { get; set; }
        public double TrueArea { get; set; }

        public double Utilization
        {
            get { return RotatedArea > 0 ? TrueArea / RotatedArea : 0; }
        }

        public double LongestSide
        {
            get { return System.Math.Max(BoundingWidth, BoundingHeight); }
        }

        public double ShortestSide
        {
            get { return System.Math.Min(BoundingWidth, BoundingHeight); }
        }
    }

    public enum BestFitSortField
    {
        Area,
        LongestSide,
        ShortestSide,
        Type,
        OriginalSequence,
        Keep,
        WhyKeepDrop
    }
}

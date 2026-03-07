using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class PairCandidate
    {
        public Drawing Drawing { get; set; }
        public double Part1Rotation { get; set; }
        public double Part2Rotation { get; set; }
        public Vector Part2Offset { get; set; }
        public int StrategyType { get; set; }
        public int TestNumber { get; set; }
        public double Spacing { get; set; }
    }
}

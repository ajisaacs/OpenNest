using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit.Tiling
{
    public class TileResult
    {
        public BestFitResult BestFit { get; set; }
        public int PairsNested { get; set; }
        public int PartsNested { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
        public double Utilization { get; set; }
        public List<PairPlacement> Placements { get; set; }
        public bool PairRotated { get; set; }
    }

    public class PairPlacement
    {
        public Vector Position { get; set; }
        public double PairRotation { get; set; }
    }
}

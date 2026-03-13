using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class Tab
    {
        public double Size { get; set; } = 0.03;
        public LeadIn TabLeadIn { get; set; }
        public LeadOut TabLeadOut { get; set; }

        public abstract List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);
    }
}

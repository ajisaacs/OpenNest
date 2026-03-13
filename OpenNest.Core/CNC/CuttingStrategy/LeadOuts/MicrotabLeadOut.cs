using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class MicrotabLeadOut : LeadOut
    {
        public double GapSize { get; set; } = 0.03;

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>();
        }
    }
}

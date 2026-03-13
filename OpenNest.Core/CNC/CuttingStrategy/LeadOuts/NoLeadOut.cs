using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NoLeadOut : LeadOut
    {
        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>();
        }
    }
}

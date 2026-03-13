using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class LeadOut
    {
        public abstract List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);
    }
}

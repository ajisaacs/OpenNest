using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class LeadIn
    {
        public abstract List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);

        public abstract Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle);
    }
}

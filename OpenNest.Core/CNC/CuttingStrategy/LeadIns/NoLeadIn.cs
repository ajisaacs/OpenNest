using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NoLeadIn : LeadIn
    {
        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>
            {
                new RapidMove(contourStartPoint)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            return contourStartPoint;
        }
    }
}

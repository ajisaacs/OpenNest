using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ArcLeadIn : LeadIn
    {
        public double Radius { get; set; }

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var arcCenter = new Vector(
                contourStartPoint.X + Radius * System.Math.Cos(contourNormalAngle),
                contourStartPoint.Y + Radius * System.Math.Sin(contourNormalAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new ArcMove(contourStartPoint, arcCenter, winding)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var arcCenterX = contourStartPoint.X + Radius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + Radius * System.Math.Sin(contourNormalAngle);

            return new Vector(
                arcCenterX + Radius * System.Math.Cos(contourNormalAngle),
                arcCenterY + Radius * System.Math.Sin(contourNormalAngle));
        }
    }
}

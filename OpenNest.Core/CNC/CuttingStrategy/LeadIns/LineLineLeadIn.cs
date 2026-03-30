using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLineLeadIn : LeadIn
    {
        public double Length1 { get; set; }
        public double ApproachAngle1 { get; set; } = 90.0;
        public double Length2 { get; set; }
        public double ApproachAngle2 { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var secondAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle1);
            var midPoint = new Vector(
                contourStartPoint.X + Length2 * System.Math.Cos(secondAngle),
                contourStartPoint.Y + Length2 * System.Math.Sin(secondAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(midPoint) { Layer = LayerType.Leadin },
                new LinearMove(contourStartPoint) { Layer = LayerType.Leadin }
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var secondAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle1);
            var midX = contourStartPoint.X + Length2 * System.Math.Cos(secondAngle);
            var midY = contourStartPoint.Y + Length2 * System.Math.Sin(secondAngle);

            var firstAngle = secondAngle + Angle.ToRadians(ApproachAngle2);
            return new Vector(
                midX + Length1 * System.Math.Cos(firstAngle),
                midY + Length1 * System.Math.Sin(firstAngle));
        }
    }
}

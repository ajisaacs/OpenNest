using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLeadIn : LeadIn
    {
        public double Length { get; set; }
        public double ApproachAngle { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(contourStartPoint) { Layer = LayerType.Leadin }
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var approachAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            return new Vector(
                contourStartPoint.X + Length * System.Math.Cos(approachAngle),
                contourStartPoint.Y + Length * System.Math.Sin(approachAngle));
        }
    }
}

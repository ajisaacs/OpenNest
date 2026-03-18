using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLeadOut : LeadOut
    {
        public double Length { get; set; }
        public double ApproachAngle { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var overcutAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            var endPoint = new Vector(
                contourEndPoint.X + Length * System.Math.Cos(overcutAngle),
                contourEndPoint.Y + Length * System.Math.Sin(overcutAngle));

            return new List<ICode>
            {
                new LinearMove(endPoint)
            };
        }
    }
}

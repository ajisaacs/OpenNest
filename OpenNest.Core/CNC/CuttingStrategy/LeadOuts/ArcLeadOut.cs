using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ArcLeadOut : LeadOut
    {
        public double Radius { get; set; }

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var arcCenterX = contourEndPoint.X + Radius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourEndPoint.Y + Radius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            var endPoint = new Vector(
                arcCenterX + Radius * System.Math.Cos(contourNormalAngle + System.Math.PI / 2),
                arcCenterY + Radius * System.Math.Sin(contourNormalAngle + System.Math.PI / 2));

            return new List<ICode>
            {
                new ArcMove(endPoint, arcCenter, winding)
            };
        }
    }
}

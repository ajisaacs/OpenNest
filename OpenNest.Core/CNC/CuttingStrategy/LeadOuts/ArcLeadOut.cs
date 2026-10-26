using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// A quarter circle leaving the cut tangentially, turning <c>winding</c> about a center
    /// <see cref="Radius"/> along the contour normal.
    /// </summary>
    public class ArcLeadOut : LeadOut
    {
        public double Radius { get; set; }

        public override List<ICode> Generate(
            Vector contourEndPoint,
            double contourNormalAngle,
            RotationType winding = RotationType.CW
        )
        {
            var arcCenterX = contourEndPoint.X + Radius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourEndPoint.Y + Radius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            // The contour point sits opposite the normal from the center; turn a quarter on.
            var endAngle = contourNormalAngle
                + (winding == RotationType.CCW ? -System.Math.PI / 2 : System.Math.PI / 2);
            var endPoint = new Vector(
                arcCenterX + Radius * System.Math.Cos(endAngle),
                arcCenterY + Radius * System.Math.Sin(endAngle)
            );

            return new List<ICode>
            {
                new ArcMove(endPoint, arcCenter, winding) { Layer = LayerType.Leadout },
            };
        }
    }
}

using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class CleanHoleLeadIn : LeadIn
    {
        public double LineLength { get; set; }
        public double ArcRadius { get; set; }
        public double Kerf { get; set; }

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            var lineAngle = contourNormalAngle + Angle.ToRadians(135.0);
            var arcStart = new Vector(
                arcCenterX + ArcRadius * System.Math.Cos(lineAngle),
                arcCenterY + ArcRadius * System.Math.Sin(lineAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(arcStart) { Layer = LayerType.Leadin },
                new ArcMove(contourStartPoint, arcCenter, winding) { Layer = LayerType.Leadin }
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);

            var lineAngle = contourNormalAngle + Angle.ToRadians(135.0);
            var arcStartX = arcCenterX + ArcRadius * System.Math.Cos(lineAngle);
            var arcStartY = arcCenterY + ArcRadius * System.Math.Sin(lineAngle);

            return new Vector(
                arcStartX + LineLength * System.Math.Cos(lineAngle),
                arcStartY + LineLength * System.Math.Sin(lineAngle));
        }

        public override LeadIn Scale(double factor) =>
            new CleanHoleLeadIn { LineLength = LineLength * factor, ArcRadius = ArcRadius * factor, Kerf = Kerf };
    }
}

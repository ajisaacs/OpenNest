using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineArcLeadIn : LeadIn
    {
        public double LineLength { get; set; }
        public double ApproachAngle { get; set; } = 135.0;
        public double ArcRadius { get; set; }

        public override List<ICode> Generate(
            Vector contourStartPoint,
            double contourNormalAngle,
            RotationType winding = RotationType.CW
        ) => Approach(contourStartPoint, contourNormalAngle, winding).ToCodes(contourStartPoint, winding);

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW) =>
            Approach(contourStartPoint, contourNormalAngle, winding).PiercePoint;

        public override LeadIn Scale(double factor) =>
            new LineArcLeadIn
            {
                LineLength = LineLength * factor,
                ArcRadius = ArcRadius * factor,
                ApproachAngle = ApproachAngle,
            };

        private LineArcApproach Approach(Vector contourStartPoint, double contourNormalAngle, RotationType winding) =>
            LineArcApproach.Create(contourStartPoint, contourNormalAngle, ArcRadius, ApproachAngle, LineLength, winding);
    }
}

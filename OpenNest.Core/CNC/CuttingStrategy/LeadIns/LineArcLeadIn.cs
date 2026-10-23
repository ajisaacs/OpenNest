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
        ) => Approach(contourStartPoint, contourNormalAngle).ToCodes(contourStartPoint, winding);

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle) =>
            Approach(contourStartPoint, contourNormalAngle).PiercePoint;

        public override LeadIn Scale(double factor) =>
            new LineArcLeadIn
            {
                LineLength = LineLength * factor,
                ArcRadius = ArcRadius * factor,
                ApproachAngle = ApproachAngle,
            };

        private LineArcApproach Approach(Vector contourStartPoint, double contourNormalAngle) =>
            LineArcApproach.Create(contourStartPoint, contourNormalAngle, ArcRadius, ApproachAngle, LineLength);
    }
}

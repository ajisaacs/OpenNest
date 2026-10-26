using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// A straight line into an arc that ends on the contour start, shared by
    /// <see cref="LineArcLeadIn"/> and <see cref="CleanHoleLeadIn"/>. The arc's center lies
    /// the arc radius along the contour normal; the line meets the arc at the approach angle
    /// (degrees from the normal, on the side the arc turns from) and starts at the pierce point.
    /// </summary>
    internal readonly struct LineArcApproach
    {
        private LineArcApproach(Vector arcCenter, Vector arcStart, Vector piercePoint)
        {
            ArcCenter = arcCenter;
            ArcStart = arcStart;
            PiercePoint = piercePoint;
        }

        public Vector ArcCenter { get; }

        public Vector ArcStart { get; }

        public Vector PiercePoint { get; }

        public static LineArcApproach Create(Vector contourStartPoint, double contourNormalAngle,
            double arcRadius, double approachAngle, double lineLength, RotationType winding)
        {
            var arcCenterX = contourStartPoint.X + arcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + arcRadius * System.Math.Sin(contourNormalAngle);

            // The approach angle is measured back from the normal against the turn, so the arc
            // runs the short way round to the contour point whichever way it turns.
            var approach = Angle.ToRadians(approachAngle);
            var lineAngle = contourNormalAngle + (winding == RotationType.CCW ? approach : -approach);
            var arcStartX = arcCenterX + arcRadius * System.Math.Cos(lineAngle);
            var arcStartY = arcCenterY + arcRadius * System.Math.Sin(lineAngle);

            return new LineArcApproach(
                new Vector(arcCenterX, arcCenterY),
                new Vector(arcStartX, arcStartY),
                new Vector(
                    arcStartX + lineLength * System.Math.Cos(lineAngle),
                    arcStartY + lineLength * System.Math.Sin(lineAngle)));
        }

        /// <summary>Rapid to the pierce point, then the line and arc on the lead-in layer.</summary>
        public List<ICode> ToCodes(Vector contourStartPoint, RotationType winding) =>
            new List<ICode>
            {
                new RapidMove(PiercePoint),
                new LinearMove(ArcStart) { Layer = LayerType.Leadin },
                new ArcMove(contourStartPoint, ArcCenter, winding) { Layer = LayerType.Leadin },
            };
    }
}

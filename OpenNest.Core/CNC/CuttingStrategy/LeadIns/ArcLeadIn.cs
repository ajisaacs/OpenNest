using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// A single arc that ends tangent to the cut. Its center lies <see cref="Radius"/> along the
    /// contour normal and it turns <see cref="SweepAngle"/> degrees in the lead's turn direction:
    /// 90 is a quarter circle, 180 (the default) a half circle starting two radii out.
    /// </summary>
    public class ArcLeadIn : LeadIn
    {
        public const double DefaultSweepAngle = 180.0;

        public double Radius { get; set; }

        /// <summary>Degrees of arc, in (0, 180].</summary>
        public double SweepAngle { get; set; } = DefaultSweepAngle;

        public override List<ICode> Generate(
            Vector contourStartPoint,
            double contourNormalAngle,
            RotationType winding = RotationType.CW
        )
        {
            return new List<ICode>
            {
                new RapidMove(GetPiercePoint(contourStartPoint, contourNormalAngle, winding)),
                new ArcMove(contourStartPoint, ArcCenter(contourStartPoint, contourNormalAngle), winding)
                {
                    Layer = LayerType.Leadin,
                },
            };
        }

        public override Vector GetPiercePoint(
            Vector contourStartPoint,
            double contourNormalAngle,
            RotationType winding = RotationType.CW
        )
        {
            // The contour point sits opposite the normal from the center; the pierce is the
            // sweep back from there, against the turn. A half circle pierces on the normal.
            var shortfall = Angle.ToRadians(DefaultSweepAngle - SweepAngle);
            var angle = contourNormalAngle + (winding == RotationType.CCW ? shortfall : -shortfall);
            var center = ArcCenter(contourStartPoint, contourNormalAngle);
            return new Vector(
                center.X + Radius * System.Math.Cos(angle),
                center.Y + Radius * System.Math.Sin(angle)
            );
        }

        public override LeadIn Scale(double factor) =>
            new ArcLeadIn { Radius = Radius * factor, SweepAngle = SweepAngle };

        private Vector ArcCenter(Vector contourStartPoint, double contourNormalAngle) => new Vector(
            contourStartPoint.X + Radius * System.Math.Cos(contourNormalAngle),
            contourStartPoint.Y + Radius * System.Math.Sin(contourNormalAngle)
        );
    }
}

using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// A lead into a contour point. <c>winding</c> is the direction a lead arc turns to meet the
    /// cut tangentially: the cut's own direction around the arc center, which lies along the
    /// normal (see <see cref="ContourCuttingStrategy.LeadTurn"/>). Straight styles ignore it.
    /// </summary>
    public abstract class LeadIn
    {
        public abstract List<ICode> Generate(
            Vector contourStartPoint,
            double contourNormalAngle,
            RotationType winding = RotationType.CW
        );

        public abstract Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);

        public virtual LeadIn Scale(double factor) => this;
    }
}

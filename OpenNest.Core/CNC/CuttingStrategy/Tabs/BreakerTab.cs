using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class BreakerTab : Tab
    {
        public double BreakerDepth { get; set; }
        public double BreakerLeadInLength { get; set; }
        public double BreakerAngle { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var codes = new List<ICode>();

            if (TabLeadOut != null)
                codes.AddRange(TabLeadOut.Generate(tabStartPoint, contourNormalAngle, winding));

            var scoreAngle = contourNormalAngle + System.Math.PI;
            var scoreEnd = new Vector(
                tabStartPoint.X + BreakerDepth * System.Math.Cos(scoreAngle),
                tabStartPoint.Y + BreakerDepth * System.Math.Sin(scoreAngle));
            codes.Add(new LinearMove(scoreEnd));
            codes.Add(new RapidMove(tabEndPoint));

            if (TabLeadIn != null)
                codes.AddRange(TabLeadIn.Generate(tabEndPoint, contourNormalAngle, winding));

            return codes;
        }
    }
}

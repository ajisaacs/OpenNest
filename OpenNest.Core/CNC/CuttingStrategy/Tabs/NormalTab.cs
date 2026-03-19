using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NormalTab : Tab
    {
        public double CutoutMinWidth { get; set; }
        public double CutoutMinHeight { get; set; }
        public double CutoutMaxWidth { get; set; }
        public double CutoutMaxHeight { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var codes = new List<ICode>();

            if (TabLeadOut != null)
                codes.AddRange(TabLeadOut.Generate(tabStartPoint, contourNormalAngle, winding));

            codes.Add(new RapidMove(tabEndPoint));

            if (TabLeadIn != null)
                codes.AddRange(TabLeadIn.Generate(tabEndPoint, contourNormalAngle, winding));

            return codes;
        }

        public bool AppliesToCutout(double cutoutWidth, double cutoutHeight)
        {
            return cutoutWidth >= CutoutMinWidth && cutoutWidth <= CutoutMaxWidth
                && cutoutHeight >= CutoutMinHeight && cutoutHeight <= CutoutMaxHeight;
        }
    }
}

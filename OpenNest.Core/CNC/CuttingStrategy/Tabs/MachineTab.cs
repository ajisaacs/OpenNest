using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class MachineTab : Tab
    {
        public int MachineTabId { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>
            {
                new RapidMove(tabEndPoint)
            };
        }
    }
}

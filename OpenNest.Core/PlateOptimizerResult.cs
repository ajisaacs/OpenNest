using System.Collections.Generic;

namespace OpenNest
{
    public class PlateOptimizerResult
    {
        public List<Part> Parts { get; set; } = new();
        public PlateOption ChosenSize { get; set; }
        public double NetCost { get; set; }
        public double Utilization { get; set; }
    }
}

using System.Collections.Generic;

namespace OpenNest
{
    public class MultiPlateResult
    {
        public List<PlateResult> Plates { get; set; } = new();
        public List<NestItem> UnplacedItems { get; set; } = new();
    }

    public class PlateResult
    {
        public Plate Plate { get; set; }
        public List<Part> Parts { get; set; } = new();
        public PlateOption ChosenSize { get; set; }
        public bool IsNew { get; set; }
    }
}

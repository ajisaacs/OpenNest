using System.Collections.Generic;

namespace OpenNest
{
    public class MultiPlateNestOptions
    {
        public Plate Template { get; set; }
        public List<PlateOption> PlateOptions { get; set; }
        public double SalvageRate { get; set; } = 0.5;
        public PartSortOrder SortOrder { get; set; } = PartSortOrder.BoundingBoxArea;
        public double MinRemnantSize { get; set; } = 12.0;
        public bool AllowPlateCreation { get; set; } = true;
    }

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

        public void AddParts(IList<Part> parts)
        {
            Plate.Parts.AddRange(parts);
            Parts.AddRange(parts);
        }
    }
}

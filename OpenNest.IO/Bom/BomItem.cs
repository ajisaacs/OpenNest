namespace OpenNest.IO.Bom
{
    public class BomItem
    {
        [Column("Item #", "Item Number", "Item Num")]
        public int? ItemNum { get; set; }

        [Column("File Name")]
        public string FileName { get; set; }

        [Column("Qty", "Quantity")]
        public int? Qty { get; set; }

        [Column("Description")]
        public string Description { get; set; }

        [Column("Part", "Part Name")]
        public string PartName { get; set; }

        [Column("Config", "Configuration")]
        public string ConfigurationName { get; set; }

        [Column("Thickness")]
        public double? Thickness { get; set; }

        [Column("Material")]
        public string Material { get; set; }

        [Column("K-Factor")]
        public double? KFactor { get; set; }

        [Column("Default Bend Radius")]
        public double? DefaultBendRadius { get; set; }
    }
}

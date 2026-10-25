namespace OpenNest
{
    public class PlateOption
    {
        public double Width { get; set; }
        public double Length { get; set; }
        public double Cost { get; set; }

        /// <summary>Sheets on hand for this request; null offers the size without limit. Not saved with the nest.</summary>
        public int? Quantity { get; set; }

        /// <summary>The engine stock id for this request; null uses the option's position. Not saved with the nest.</summary>
        public string Id { get; set; }

        public double Area => Width * Length;
    }
}

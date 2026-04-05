namespace OpenNest
{
    public class PlateOption
    {
        public double Width { get; set; }
        public double Length { get; set; }
        public double Cost { get; set; }

        public double Area => Width * Length;
    }
}

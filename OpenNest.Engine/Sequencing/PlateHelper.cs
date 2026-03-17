using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    internal static class PlateHelper
    {
        public static Vector GetExitPoint(Plate plate)
        {
            var xExtent = plate.Size.Length;
            var yExtent = plate.Size.Width;

            return plate.Quadrant switch
            {
                1 => new Vector(xExtent, yExtent),
                2 => new Vector(0, yExtent),
                3 => new Vector(0, 0),
                4 => new Vector(xExtent, 0),
                _ => new Vector(xExtent, yExtent)
            };
        }
    }
}

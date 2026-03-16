using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    internal static class PlateHelper
    {
        public static Vector GetExitPoint(Plate plate)
        {
            var w = plate.Size.Width;
            var l = plate.Size.Length;

            return plate.Quadrant switch
            {
                1 => new Vector(w, l),
                2 => new Vector(0, l),
                3 => new Vector(0, 0),
                4 => new Vector(w, 0),
                _ => new Vector(w, l)
            };
        }
    }
}

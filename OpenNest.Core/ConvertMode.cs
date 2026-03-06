using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    public static class ConvertMode
    {
        /// <summary>
        /// Converts the program to absolute coordinates.
        /// Does NOT check program mode before converting.
        /// </summary>
        /// <param name="pgm"></param>
        public static void ToAbsolute(Program pgm)
        {
            var pos = new Vector(0, 0);

            for (int i = 0; i < pgm.Codes.Count; ++i)
            {
                var code = pgm.Codes[i];
                var motion = code as Motion;

                if (motion != null)
                {
                    motion.Offset(pos);
                    pos = motion.EndPoint;
                }
            }
        }

        /// <summary>
        /// Converts the program to intermental coordinates.
        /// Does NOT check program mode before converting.
        /// </summary>
        /// <param name="pgm"></param>
        public static void ToIncremental(Program pgm)
        {
            var pos = new Vector(0, 0);

            for (int i = 0; i < pgm.Codes.Count; ++i)
            {
                var code = pgm.Codes[i];
                var motion = code as Motion;

                if (motion != null)
                {
                    var pos2 = motion.EndPoint;
                    motion.Offset(-pos.X, -pos.Y);
                    pos = pos2;
                }
            }
        }
    }
}

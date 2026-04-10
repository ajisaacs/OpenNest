using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Converters
{
    public static class ConvertMode
    {
        /// <summary>
        /// Converts the program to absolute coordinates.
        /// Does NOT check program mode before converting.
        /// </summary>
        public static void ToAbsolute(Program pgm)
        {
            var pos = new Vector(0, 0);

            for (int i = 0; i < pgm.Codes.Count; ++i)
            {
                var code = pgm.Codes[i];

                if (code is SubProgramCall subCall && subCall.Program != null)
                {
                    // Sub-program is placed at Offset in this program's frame.
                    // After it runs, the tool is at Offset + (sub's end in its own frame).
                    pos = ComputeEndPosition(subCall.Program, subCall.Offset);
                    continue;
                }

                if (code is Motion motion)
                {
                    motion.Offset(pos.X, pos.Y);
                    pos = motion.EndPoint;
                }
            }
        }

        /// <summary>
        /// Converts the program to incremental coordinates.
        /// Does NOT check program mode before converting.
        /// </summary>
        public static void ToIncremental(Program pgm)
        {
            var pos = new Vector(0, 0);

            for (int i = 0; i < pgm.Codes.Count; ++i)
            {
                var code = pgm.Codes[i];

                if (code is SubProgramCall subCall && subCall.Program != null)
                {
                    // Sub-program is placed at Offset in this program's frame,
                    // regardless of where the tool was before the call.
                    pos = ComputeEndPosition(subCall.Program, subCall.Offset);
                    continue;
                }

                if (code is Motion motion)
                {
                    var pos2 = motion.EndPoint;
                    motion.Offset(-pos.X, -pos.Y);
                    pos = pos2;
                }
            }
        }

        /// <summary>
        /// Computes the tool position after executing <paramref name="pgm"/>,
        /// given that the program's frame origin is at <paramref name="startPos"/>
        /// in the caller's frame. Walks nested sub-program calls recursively.
        /// </summary>
        private static Vector ComputeEndPosition(Program pgm, Vector startPos)
        {
            var pos = startPos;

            for (int i = 0; i < pgm.Codes.Count; ++i)
            {
                var code = pgm.Codes[i];

                if (code is SubProgramCall subCall && subCall.Program != null)
                {
                    // Nested sub's frame origin in the caller's frame is startPos + Offset.
                    pos = ComputeEndPosition(subCall.Program, startPos + subCall.Offset);
                    continue;
                }

                if (code is Motion motion)
                {
                    if (pgm.Mode == Mode.Incremental)
                        pos = pos + motion.EndPoint;
                    else
                        pos = startPos + motion.EndPoint;
                }
            }

            return pos;
        }
    }
}

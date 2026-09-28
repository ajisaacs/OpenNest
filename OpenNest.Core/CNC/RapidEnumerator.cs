using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC
{
    public static class RapidEnumerator
    {
        public readonly record struct Segment(Vector From, Vector To);

        public static List<Segment> Enumerate(Program pgm, Vector basePos, Vector startPos)
        {
            var results = new List<Segment>();

            // Draw the rapid from the previous tool position to the program's first
            // pierce point. The walk then starts at the program origin (basePos), not
            // the pierce: the skipped first rapid still advances pos, so starting at
            // the pierce would apply a nonzero Incremental first delta twice (as in
            // lead-in programs) and shift every later rapid by it.
            var firstPierce = FirstPiercePoint(pgm, basePos);
            results.Add(new Segment(startPos, firstPierce));

            var pos = basePos;
            Walk(pgm, basePos, ref pos, skipFirst: true, results);
            return results;
        }

        private static Vector FirstPiercePoint(Program pgm, Vector basePos)
        {
            for (var i = 0; i < pgm.Length; i++)
            {
                if (pgm[i] is SubProgramCall call && call.Program != null)
                    return FirstPiercePoint(call.Program, basePos + call.Offset);

                if (pgm[i] is Motion motion)
                    return motion.EndPoint + basePos;
            }
            return basePos;
        }

        private static void Walk(
            Program pgm,
            Vector basePos,
            ref Vector pos,
            bool skipFirst,
            List<Segment> results
        )
        {
            var skipped = !skipFirst;

            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code is SubProgramCall { Program: { } program } call)
                {
                    var holeBase = basePos + call.Offset;
                    var firstPierce = FirstPiercePoint(program, holeBase);

                    if (!skipped)
                        skipped = true;
                    else
                        results.Add(new Segment(pos, firstPierce));

                    var subPos = holeBase;
                    Walk(program, holeBase, ref subPos, skipFirst: true, results);
                    pos = subPos;
                }
                else if (code is Motion motion)
                {
                    var endpt =
                        pgm.Mode == Mode.Incremental
                            ? motion.EndPoint + pos
                            : motion.EndPoint + basePos;

                    if (code.Type == CodeType.RapidMove)
                    {
                        if (!skipped)
                            skipped = true;
                        else
                            results.Add(new Segment(pos, endpt));
                    }

                    pos = endpt;
                }
            }
        }
    }
}

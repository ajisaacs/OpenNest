using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.CNC
{
    /// <summary>
    /// The never-cut outline of a supplied blank, kept at the end of an applied cutting program.
    /// An applied program for a blank with a <see cref="LayerType.DisplayPerimeter"/> outline cuts
    /// only its cutouts; the outline follows them after <see cref="Marker"/> so the part keeps the
    /// blank's extent for bounds, overlap and cut-offs. Nothing that executes or posts a program may
    /// run the tail: executed reads stop at the marker and posts take <see cref="ForPosting"/>.
    /// </summary>
    public static class DisplayPerimeterTail
    {
        /// <summary>The comment that starts the tail; every code after it is display only.</summary>
        public const string Marker = "DISPLAY PERIMETER NEVER CUT";

        /// <summary>The index of the tail's marker in <paramref name="program"/>'s codes, or -1.</summary>
        public static int IndexOf(Program program)
        {
            ArgumentNullException.ThrowIfNull(program);
            return program.Codes.FindIndex(code => code is Comment { Value: Marker });
        }

        public static bool HasTail(Program program) => IndexOf(program) >= 0;

        /// <summary>
        /// Appends the outline after the marker: a rapid to its start, then its entities on the
        /// display perimeter layer. <paramref name="program"/> must still be in absolute mode.
        /// </summary>
        internal static void Append(Program program, IReadOnlyList<ICode> outline, Vector start)
        {
            if (program.Mode != Mode.Absolute)
                throw new InvalidOperationException("Append the display tail before switching to incremental mode.");
            program.Codes.Add(new Comment(Marker));
            program.Codes.Add(new RapidMove(start));
            program.Codes.AddRange(outline);
        }

        /// <summary>
        /// The executed program: <paramref name="program"/> itself when it has no tail, otherwise a
        /// shallow copy of the codes before the marker (shared instructions and sub-programs).
        /// </summary>
        public static Program WithoutTail(Program program)
        {
            var marker = IndexOf(program);
            return marker < 0 ? program : program.HeadCopy(marker);
        }

        /// <summary>Whether <paramref name="program"/> or any sub-program it calls moves on the display perimeter layer.</summary>
        public static bool HasDisplayPerimeterMotion(Program program) =>
            HasDisplayPerimeterMotion(program, new HashSet<Program>(ReferenceEqualityComparer.Instance));

        private static bool HasDisplayPerimeterMotion(Program program, HashSet<Program> visited)
        {
            if (program?.Codes == null || !visited.Add(program))
                return false;
            foreach (var code in program.Codes)
            {
                if (code is LinearMove { Layer: LayerType.DisplayPerimeter } or ArcMove { Layer: LayerType.DisplayPerimeter })
                    return true;
                if (code is SubProgramCall call && HasDisplayPerimeterMotion(call.Program, visited))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The program a post may write for a part: its tail removed. A display perimeter anywhere
        /// else means the part was never planned, and posting it would cut the blank's outline.
        /// </summary>
        public static Program ForPosting(Program program, string partName)
        {
            var executed = WithoutTail(program);
            if (HasDisplayPerimeterMotion(executed))
                throw new InvalidOperationException(
                    $"{partName} has a display perimeter (a supplied blank's outline that is never cut) but no "
                    + "cutting plan. Run Plan Cutting before posting so only its cutouts are cut.");
            return executed;
        }
    }
}

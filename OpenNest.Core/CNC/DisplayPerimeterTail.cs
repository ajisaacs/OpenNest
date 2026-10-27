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

        /// <summary>Why the manual and legacy lead-in tools leave a display-perimeter part alone.</summary>
        public const string LeadInRefusal =
            "This part's outline is a display perimeter: a supplied blank that is never cut. "
            + "Lead-ins for its cutouts come from Plan Cutting, which keeps every pierce inside a cutout.";

        /// <summary>
        /// The index of the tail's marker in <paramref name="program"/>'s codes, or -1. A marker that is
        /// repeated, sits in a sub-program, or is followed by anything but one rapid and moves on the
        /// display perimeter layer is a malformed program (<see cref="ArgumentException"/>): cuts after
        /// it must never be dropped silently.
        /// </summary>
        public static int IndexOf(Program program)
        {
            ArgumentNullException.ThrowIfNull(program);
            var marker = -1;
            for (var i = 0; i < program.Codes.Count; i++)
            {
                if (program.Codes[i] is Comment { Value: Marker })
                {
                    if (marker >= 0)
                        throw new ArgumentException("A program has more than one display perimeter tail.");
                    marker = i;
                }
                else if (program.Codes[i] is SubProgramCall call
                    && HasMarker(call.Program, new HashSet<Program>(ReferenceEqualityComparer.Instance)))
                    throw new ArgumentException("A display perimeter tail can only end the part's own program, not a sub-program.");
            }
            if (marker < 0)
                return -1;
            var tail = program.Codes.Count - marker - 1;
            if (tail < 2 || program.Codes[marker + 1] is not RapidMove
                || !program.Codes.Skip(marker + 2).All(code =>
                    code is LinearMove { Layer: LayerType.DisplayPerimeter } or ArcMove { Layer: LayerType.DisplayPerimeter }))
                throw new ArgumentException(
                    "A display perimeter tail must be one rapid followed only by the outline on the display perimeter layer.");
            return marker;
        }

        private static bool HasMarker(Program program, HashSet<Program> visited)
        {
            if (program?.Codes == null || !visited.Add(program))
                return false;
            foreach (var code in program.Codes)
                if (code is Comment { Value: Marker } || code is SubProgramCall call && HasMarker(call.Program, visited))
                    return true;
            return false;
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
        /// The executed program for estimates and drawing: <paramref name="program"/> itself when it
        /// has no well-formed tail, otherwise a shallow copy of the codes before the marker. Posts
        /// and verification use the strict <see cref="ForPosting"/> and executed reads instead.
        /// </summary>
        public static Program WithoutTail(Program program)
        {
            try
            {
                var marker = IndexOf(program);
                return marker < 0 ? program : program.HeadCopy(marker);
            }
            catch (ArgumentException)
            {
                return program;
            }
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
            int marker;
            try
            {
                marker = IndexOf(program);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"{partName} cannot be posted: {ex.Message}", ex);
            }
            var executed = marker < 0 ? program : program.HeadCopy(marker);
            if (HasDisplayPerimeterMotion(executed))
                throw new InvalidOperationException(
                    $"{partName} has a display perimeter (a supplied blank's outline that is never cut) but no "
                    + "cutting plan. Plan cutting first (Plate > Plan Cutting..., or --plan-cutting) so only its cutouts are cut.");
            return executed;
        }

        /// <summary>The name posts give a part in a refusal.</summary>
        public static string PartName(Part part) => $"Part '{part.BaseDrawing?.Name}'";

        /// <summary>
        /// Refuses a nest a post cannot write, before any output is created: every part's program
        /// must pass <see cref="ForPosting"/>, so a refusal never leaves an empty or partial file.
        /// </summary>
        public static void EnsurePostable(Nest nest)
        {
            ArgumentNullException.ThrowIfNull(nest);
            foreach (var plate in nest.Plates)
                foreach (var part in plate.Parts)
                    if (part?.Program != null && part.BaseDrawing?.IsCutOff != true)
                        ForPosting(part.Program, PartName(part));
        }
    }
}

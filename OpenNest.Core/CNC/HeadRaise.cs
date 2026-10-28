using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.CNC
{
    /// <summary>
    /// A rapid the head must make raised because it passes over parts already cut, which may have
    /// tipped. Plan Cutting marks such a rapid only when no route avoids the crossing: a
    /// <see cref="Marker"/> comment immediately before the <see cref="RapidMove"/>. Rapid checks accept
    /// a crossing on a marked rapid. Posts write their machine's head raise before every marked rapid,
    /// and refuse to post a marked program when they have none, since the rapid is unsafe head down.
    /// See docs/cutting-planner.md.
    /// </summary>
    public static class HeadRaise
    {
        /// <summary>The comment immediately before a rapid made with the head raised.</summary>
        public const string Marker = "HEAD RAISE";

        public static bool IsMarker(ICode code) => code is Comment { Value: Marker };

        /// <summary>Whether <paramref name="program"/>'s own codes hold a marker.</summary>
        public static bool IsMarked(Program program)
        {
            if (program?.Codes == null)
                return false;
            foreach (var code in program.Codes)
                if (IsMarker(code))
                    return true;
            return false;
        }

        /// <summary>
        /// For each of <paramref name="codes"/>, whether it is a rapid made with the head raised. A
        /// marker must be followed directly by a rapid; anything else is a malformed program
        /// (<see cref="ArgumentException"/>), since a post could raise before the wrong move.
        /// </summary>
        public static bool[] RaisedMask(IReadOnlyList<ICode> codes)
        {
            ArgumentNullException.ThrowIfNull(codes);
            var mask = new bool[codes.Count];
            for (var i = 0; i < codes.Count; i++)
            {
                if (!IsMarker(codes[i]))
                    continue;
                if (i + 1 >= codes.Count || codes[i + 1] is not RapidMove)
                    throw new ArgumentException("A head-raise marker is not followed by a rapid.");
                mask[i + 1] = true;
            }
            return mask;
        }

        /// <summary>
        /// A copy of <paramref name="program"/> with a marker before each of its own codes at
        /// <paramref name="rapids"/>, which must be rapids not marked already.
        /// </summary>
        public static Program Mark(Program program, IReadOnlyCollection<int> rapids)
        {
            ArgumentNullException.ThrowIfNull(program);
            ArgumentNullException.ThrowIfNull(rapids);
            var marked = RaisedMask(program.Codes);
            var copy = (Program)program.Clone();
            for (var i = copy.Codes.Count - 1; i >= 0; i--)
            {
                if (!rapids.Contains(i))
                    continue;
                if (copy.Codes[i] is not RapidMove || marked[i])
                    throw new ArgumentException("Only an unmarked rapid can be marked for a head raise.");
                copy.Codes.Insert(i, new Comment(Marker));
            }
            return copy;
        }

        /// <summary>
        /// Refuses a nest a post cannot write, before any output is created: a marked rapid needs
        /// the post's head-raise code, and its markers must be well formed.
        /// </summary>
        public static void EnsurePostable(Nest nest, bool hasCode, string postName)
        {
            ArgumentNullException.ThrowIfNull(nest);
            foreach (var plate in nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part?.Program == null || !IsMarked(part.Program))
                        continue;
                    var name = DisplayPerimeterTail.PartName(part);
                    if (!hasCode)
                        throw new InvalidOperationException(
                            $"{name} has rapids that must raise the head over parts already cut, but the {postName} "
                            + "post has no head-raise code. Enter it in the post's configuration before posting.");
                    try
                    {
                        RaisedMask(part.Program.Codes);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException($"{name} cannot be posted: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}

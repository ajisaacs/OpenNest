using System;
using System.Collections.Generic;

namespace OpenNest.CNC
{
    /// <summary>
    /// The head-height lock around a cut-off's overcut past the nominal far sheet edge. Stock can
    /// arrive wider than nominal, so a cut-off overcuts past the far edge to cut the oversize off;
    /// past the real edge there is no material to sense, so the head height is locked first. A
    /// cut-off program marks the locked stretch with a <see cref="Lock"/> comment and ends it with an
    /// <see cref="Unlock"/> comment; a rapid between them keeps the head locked and is not a head
    /// raise. Posts write each marker as their machine's lock or unlock code and refuse to post a
    /// marked program when no code is configured, because the overcut is unsafe without the lock.
    /// See docs/automatic-scrap-cutoffs.md.
    /// </summary>
    public static class HeadHeightLock
    {
        /// <summary>The comment before the first motion made with the head height locked.</summary>
        public const string Lock = "HEAD HEIGHT LOCK";

        /// <summary>The comment after the last motion made with the head height locked.</summary>
        public const string Unlock = "HEAD HEIGHT UNLOCK";

        public static bool IsLock(ICode code) => code is Comment { Value: Lock };

        public static bool IsUnlock(ICode code) => code is Comment { Value: Unlock };

        /// <summary>Whether <paramref name="program"/>'s own codes hold a lock marker.</summary>
        public static bool IsMarked(Program program)
        {
            if (program?.Codes == null)
                return false;
            foreach (var code in program.Codes)
                if (IsLock(code))
                    return true;
            return false;
        }

        /// <summary>
        /// For each of <paramref name="codes"/>, whether the head height is locked while it runs: the
        /// codes after a lock marker up to its unlock marker. Markers must alternate, starting with a
        /// lock and ending unlocked; anything else is a malformed program (<see cref="ArgumentException"/>),
        /// since a post that lost track of the lock could cut off the sheet unlocked or never unlock.
        /// </summary>
        public static bool[] LockedMask(IReadOnlyList<ICode> codes)
        {
            ArgumentNullException.ThrowIfNull(codes);
            var mask = new bool[codes.Count];
            var locked = false;
            for (var i = 0; i < codes.Count; i++)
            {
                if (IsLock(codes[i]))
                {
                    if (locked)
                        throw new ArgumentException("A head-height lock is set twice without an unlock.");
                    locked = true;
                }
                else if (IsUnlock(codes[i]))
                {
                    if (!locked)
                        throw new ArgumentException("A head-height unlock has no lock before it.");
                    locked = false;
                }
                else
                    mask[i] = locked;
            }
            if (locked)
                throw new ArgumentException("A head-height lock is never released.");
            return mask;
        }

        /// <summary>
        /// Refuses a nest a post cannot write, before any output is created: a cut-off with a locked
        /// overcut needs the post's lock and unlock codes, and its markers must be well formed.
        /// </summary>
        public static void EnsurePostable(Nest nest, bool hasCodes, string postName)
        {
            ArgumentNullException.ThrowIfNull(nest);
            foreach (var plate in nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part?.Program == null || !IsMarked(part.Program))
                        continue;
                    var name = DisplayPerimeterTail.PartName(part);
                    if (!hasCodes)
                        throw new InvalidOperationException(
                            $"{name} overcuts past the sheet edge with the head height locked, but the {postName} post "
                            + "has no head-height lock and unlock codes. Enter them in the post's configuration, "
                            + "or delete the cut-off and add it again with the head-height lock off.");
                    try
                    {
                        LockedMask(part.Program.Codes);
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
